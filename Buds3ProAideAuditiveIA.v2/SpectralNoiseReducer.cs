using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Réducteur de bruit spectral simple, autonome.
    /// - Profil de bruit: moyenne glissante des magnitudes en absence de voix (ou via UpdateNoiseProfile).
    /// - Atténuation: soustraction spectrale avec plancher (floor).
    /// - Fenêtrage: Hann.
    /// API:
    ///   - SpectralNoiseReducer(int frameSamplesPow2, int sampleRate)
    ///   - UpdateNoiseProfile(short[] frame)
    ///   - Process(short[] frame, float alpha = 0.60f, float floor = 0.50f, bool adapt = true)
    /// </summary>
    public sealed class SpectralNoiseReducer
    {
        private readonly int _fs;
        private readonly int _n;         // FFT size = frame size (puissance de 2)
        private readonly double[] _win;  // fenêtre Hann
        private readonly double[] _re;   // partie réelle
        private readonly double[] _im;   // partie imaginaire

        // Profil de bruit et suiveur de minimum
        private readonly double[] _noiseMag;
        private readonly double[] _prevMin;

        // Paramètres
        private const float DefaultAlpha = 0.60f; // poids de soustraction
        private const float DefaultFloor = 0.50f; // plancher d’atténuation (0..1)
        private const double AdaptDecay = 0.995; // suiveur de minimum

        public SpectralNoiseReducer(int frameSamples, int sampleRate)
        {
            if (frameSamples <= 0) throw new ArgumentOutOfRangeException(nameof(frameSamples));
            if (!IsPowerOfTwo(frameSamples)) throw new ArgumentException("frameSamples doit être une puissance de 2.", nameof(frameSamples));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            _fs = sampleRate;
            _n = frameSamples;

            _win = new double[_n];
            for (int i = 0; i < _n; i++)
                _win[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (_n - 1)); // Hann

            _re = new double[_n];
            _im = new double[_n];

            _noiseMag = new double[_n / 2];
            _prevMin = new double[_n / 2];

            for (int k = 0; k < _noiseMag.Length; k++)
                _noiseMag[k] = 1e-6;
            Array.Copy(_noiseMag, _prevMin, _noiseMag.Length);
        }

        public int SampleRate => _fs;
        public int FrameSamples => _n;

        /// <summary>Mise à jour du profil de bruit à partir d’une trame silencieuse.</summary>
        public void UpdateNoiseProfile(short[] frame)
        {
            if (frame == null || frame.Length < _n) throw new ArgumentException("frame invalide.", nameof(frame));

            PrepareFftInput(frame);
            FFT(_re, _im, inverse: false);

            int bins = _n / 2;
            for (int k = 0; k < bins; k++)
            {
                double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) + 1e-9;
                _noiseMag[k] = 0.9 * _noiseMag[k] + 0.1 * mag;                                  // EMA lente
                _prevMin[k] = Math.Min(_prevMin[k] * AdaptDecay + 1e-3, _noiseMag[k]);          // suiveur de minimum
            }
        }

        /// <summary>Réduction de bruit IN-PLACE. Retourne true si traitée.</summary>
        public bool Process(short[] frame, float alpha = DefaultAlpha, float floor = DefaultFloor, bool adapt = true)
        {
            if (frame == null || frame.Length < _n) return false;

            // clamp manuel (compat .NET) au lieu de Math.Clamp
            double a = Clamp(alpha, 0.0, 1.0);
            double f = Clamp(floor, 0.0, 1.0);

            PrepareFftInput(frame);
            FFT(_re, _im, inverse: false);

            int bins = _n / 2;

            // Soustraction spectrale
            for (int k = 0; k < bins; k++)
            {
                double re = _re[k], im = _im[k];
                double mag = Math.Sqrt(re * re + im * im) + 1e-9;
                double nmag = _noiseMag[k];

                // estimation "clean"
                double clean = Math.Max(mag - a * nmag, f * mag);
                double g = clean / mag;

                _re[k] *= g; _im[k] *= g;
                if (k != 0)
                {
                    int k2 = _n - k;     // symétrie conjuguée
                    _re[k2] *= g; _im[k2] *= g;
                }
            }

            FFT(_re, _im, inverse: true);
            ApplyWindowAndStore(frame);

            // Adaptation lente du bruit si on pense être hors-voix
            if (adapt)
            {
                // Heuristique simple sur énergie de trame
                double e = 0;
                for (int i = 0; i < _n; i++) e += (double)frame[i] * frame[i];
                double rms = Math.Sqrt(e / _n);

                if (rms < 400) // seuil conservateur
                {
                    PrepareFftInput(frame);
                    FFT(_re, _im, inverse: false);
                    for (int k = 0; k < bins; k++)
                    {
                        double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) + 1e-9;
                        _prevMin[k] = Math.Min(_prevMin[k] * AdaptDecay + 1e-3, mag);
                        _noiseMag[k] = 0.98 * _noiseMag[k] + 0.02 * _prevMin[k];
                    }
                }
            }

            return true;
        }

        // ===== Helpers =====
        private static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi) ? hi : v;

        private void PrepareFftInput(short[] frame)
        {
            for (int i = 0; i < _n; i++)
            {
                double x = frame[i];
                _re[i] = x * _win[i];
                _im[i] = 0.0;
            }
        }

        private void ApplyWindowAndStore(short[] frame)
        {
            for (int i = 0; i < _n; i++)
            {
                double y = _re[i] * _win[i];
                if (y > short.MaxValue) y = short.MaxValue;
                else if (y < short.MinValue) y = short.MinValue;
                frame[i] = (short)y;
            }
        }

        private static bool IsPowerOfTwo(int x) => (x & (x - 1)) == 0;

        /// <summary>FFT complexe radix-2 in-place (tuple swap pour lisibilité).</summary>
        private static void FFT(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;

            // Bit-reversal (i démarre à 1 pour éviter l’échange 0<->0)
            for (int i = 1, j = 0; i < n; ++i)
            {
                int bit = n >> 1;
                for (; j >= bit; bit >>= 1) j -= bit;
                j += bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len * (inverse ? 1 : -1);
                double wlr = Math.Cos(ang), wli = Math.Sin(ang);

                for (int i = 0; i < n; i += len)
                {
                    double wr = 1.0, wi = 0.0;
                    int half = len >> 1;

                    for (int j = 0; j < half; j++)
                    {
                        int u = i + j;
                        int v = u + half;

                        double vr = re[v] * wr - im[v] * wi;
                        double vi = re[v] * wi + im[v] * wr;

                        re[v] = re[u] - vr; im[v] = im[u] - vi;
                        re[u] += vr; im[u] += vi;

                        double nwr = wr * wlr - wi * wli;
                        wi = wr * wli + wi * wlr;
                        wr = nwr;
                    }
                }
            }

            if (inverse)
            {
                for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
            }
        }
    }
}
