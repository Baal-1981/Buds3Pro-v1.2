using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Réduction de bruit spectrale légère : STFT Hann (N=256, overlap 50%),
    /// estimation du bruit par "minimum statistics" + lissage, gain Wiener
    /// borné (floor) + lissage spectral pour éviter l'effet "robot".
    /// Traite in-place un bloc de samples PCM16 (short[]).
    /// </summary>
    public sealed class SpectralNoiseReducer
    {
        // params principaux (exposés)
        public double FloorGain { get; set; } = 0.12;     // plancher (0..1) – 0.12 ~ -18 dB
        public double Smoothing { get; set; } = 0.80;     // lissage du gain spectral (0..1)
        public int FrameSize { get; }      // 256
        public int HopSize { get; }      // 128

        // internes
        readonly int _n, _hop, _bins;
        readonly double[] _win, _ola, _noiseP, _minP, _smoothP, _prevG;
        readonly double[] _re, _im, _time;
        int _minCount, _minPeriod;

        public SpectralNoiseReducer(int frameSize = 256, int overlap = 2)
        {
            _n = FrameSize = frameSize;
            _hop = HopSize = frameSize / overlap;
            _bins = _n / 2 + 1;

            _win = new double[_n];
            for (int i = 0; i < _n; i++) _win[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_n - 1))); // Hann

            _ola = new double[_n];
            _noiseP = new double[_bins];   // estimation bruit (puissance)
            _minP = new double[_bins];   // minimum courant
            _smoothP = new double[_bins];   // lissage magnitude^2
            _prevG = new double[_bins];   // lissage gain
            _re = new double[_n];
            _im = new double[_n];
            _time = new double[_n];

            for (int k = 0; k < _bins; k++)
            {
                _noiseP[k] = 1e-6;
                _minP[k] = 1e6;
                _prevG[k] = 1.0;
            }

            // ~ 0.5 s de fenêtre pour la stat mini (Hop 128 @ 48 kHz -> ~375 hop/s)
            _minPeriod = 180; // ~0.48 s
            _minCount = 0;
        }

        /// <summary>
        /// Traite nSamples du buffer mono PCM16 in-place. nSamples DOIT être un multiple de HopSize (128).
        /// </summary>
        public void ProcessInPlace(short[] buf, int nSamples)
        {
            if (nSamples <= 0) return;
            // Converti le bloc en double
            double[] input = new double[nSamples];
            for (int i = 0; i < nSamples; i++) input[i] = buf[i] / 32768.0;

            // pipeline par trames, avec OLA
            int produced = 0;
            for (int t = 0; t + _hop <= nSamples; t += _hop)
            {
                // assemble une trame : [derniers n-hop de l'OLA] + [hop échantillons nouveaux]
                // -> plus simple : l'OLA contient la "queue" déjà décalée après extraction.
                // on remplit _time avec hop nouveaux + (n-hop) zéros au début, puis on overlay.
                // Variante robuste : tampon local 'frame' = n échantillons fenêtrés.
                Array.Clear(_re, 0, _n);
                Array.Clear(_im, 0, _n);
                for (int i = 0; i < _n - _hop; i++) _time[i] = 0.0; // pas de lookback, on laisse la continuité gérée par _ola
                for (int i = 0; i < _hop; i++) _time[_n - _hop + i] = input[t + i];

                // fenêtrage + FFT
                for (int i = 0; i < _n; i++)
                {
                    double v = _time[i] * _win[i];
                    _re[i] = v;
                    _im[i] = 0.0;
                }
                FFT(_re, _im, false);

                // estimation bruit + gain
                const double eps = 1e-12;
                const double beta = 0.9; // lissage mag^2
                for (int k = 0; k < _bins; k++)
                {
                    double mag2 = _re[k] * _re[k] + _im[k] * _im[k];
                    _smoothP[k] = beta * _smoothP[k] + (1.0 - beta) * mag2;

                    // minimum statistics
                    _minP[k] = Math.Min(_minP[k], _smoothP[k]);
                }
                _minCount++;
                if (_minCount >= _minPeriod)
                {
                    // met à jour le bruit vers le minimum observé (avec inertie)
                    for (int k = 0; k < _bins; k++)
                        _noiseP[k] = 0.98 * _noiseP[k] + 0.02 * _minP[k];
                    for (int k = 0; k < _bins; k++) _minP[k] = 1e6;
                    _minCount = 0;
                }

                // applique gain Wiener borné + lissage spectral
                for (int k = 0; k < _bins; k++)
                {
                    double post = (_re[k] * _re[k] + _im[k] * _im[k]) / (_noiseP[k] + eps);
                    double prior = Math.Max(post - 1.0, 0.0);
                    double g = prior / (prior + 1.0);
                    g = Math.Max(g, FloorGain);
                    g = Smoothing * _prevG[k] + (1.0 - Smoothing) * g;
                    _prevG[k] = g;

                    _re[k] *= g;
                    _im[k] *= g;

                    // conj sym pour IFFT (les bins supérieurs)
                    if (k > 0 && k < _n - k)
                    {
                        _re[_n - k] = _re[k];
                        _im[_n - k] = -_im[k];
                    }
                }

                // IFFT + défenêtrage
                FFT(_re, _im, true);
                for (int i = 0; i < _n; i++)
                    _time[i] = (_re[i] * _win[i]); // re-applique Hann pour OLA cohérente

                // OLA : ajoute la trame et sort hop échantillons
                for (int i = 0; i < _n; i++) _ola[i] += _time[i];
                // sort hop
                for (int i = 0; i < _hop; i++)
                {
                    double y = _ola[i];
                    int s = (int)Math.Round(y * 32768.0);
                    if (s > short.MaxValue) s = short.MaxValue;
                    else if (s < short.MinValue) s = short.MinValue;
                    buf[produced + i] = (short)s;
                }
                produced += _hop;

                // décale l’OLA
                Array.Copy(_ola, _hop, _ola, 0, _n - _hop);
                Array.Clear(_ola, _n - _hop, _hop);
            }

            // si jamais pas un multiple, on laisse la fin pour l'appel suivant (notre boucle audio alimente par multiples de hop)
        }

        // FFT complexe radix-2 (in-place)
        static void FFT(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            // bit-reversal
            for (int i = 1, j = 0; i < n; ++i)
            {
                int bit = n >> 1;
                for (; j >= bit; bit >>= 1) j -= bit;
                j += bit;
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len * (inverse ? -1 : 1);
                double wlenr = Math.Cos(ang), wleni = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double wr = 1.0, wi = 0.0;
                    int half = len >> 1;
                    for (int j = 0; j < half; ++j)
                    {
                        int u = i + j, v = i + j + half;
                        double vr = re[v] * wr - im[v] * wi;
                        double vi = re[v] * wi + im[v] * wr;
                        re[v] = re[u] - vr; im[v] = im[u] - vi;
                        re[u] += vr; im[u] += vi;

                        double tmp = wr * wlenr - wi * wleni;
                        wi = wr * wleni + wi * wlenr;
                        wr = tmp;
                    }
                }
            }
            if (inverse)
            {
                for (int i = 0; i < n; ++i) { re[i] /= n; im[i] /= n; }
            }
        }
    }
}
