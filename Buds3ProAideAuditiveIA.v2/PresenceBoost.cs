using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Accent de présence (peaking EQ) autour de ~2–3 kHz pour rendre les consonnes plus nettes.
    /// Implémentation RBJ en Direct Form II transposée.
    /// API:
    ///   - PresenceBoost(int sampleRate, double freqHz = 2500, double gainDb = 4, double q = 0.9)
    ///   - Reconfigure(int sr, double freq, double gainDb, double q)
    ///   - ProcessBuffer(short[] buf, int nSamples)
    /// </summary>
    public sealed class PresenceBoost
    {
        // Coefficients normalisés (a0 = 1) : y = b0*x + z1 ; z1 = b1*x - a1*y + z2 ; z2 = b2*x - a2*y
        private double _b0, _b1, _b2; // feedforward
        private double _a1, _a2;      // feedback
        // États (DF-II)
        private double _z1, _z2;

        public PresenceBoost(int sampleRate, double freqHz = 2500.0, double gainDb = 4.0, double q = 0.9)
        {
            Reconfigure(sampleRate, freqHz, gainDb, q);
        }

        /// <summary>Recalcule les coefficients du peaking EQ (RBJ).</summary>
        public void Reconfigure(int sr, double freq, double gainDb, double q)
        {
            if (sr <= 0) throw new ArgumentOutOfRangeException(nameof(sr));
            // Bornes sûres
            double fs = sr;
            double fc = Math.Max(100.0, Math.Min(freq, fs * 0.45)); // évite Nyquist
            double Q = Math.Max(0.2, q);
            double A = Math.Pow(10.0, gainDb / 40.0);

            double w0 = 2.0 * Math.PI * fc / fs;
            double cosw = Math.Cos(w0);
            double sinw = Math.Sin(w0);
            double alpha = sinw / (2.0 * Q);

            // RBJ Peaking (coeffs non normalisés)
            double b0d = 1.0 + alpha * A;
            double b1d = -2.0 * cosw;
            double b2d = 1.0 - alpha * A;
            double a0d = 1.0 + alpha / A;
            double a1d = -2.0 * cosw;
            double a2d = 1.0 - alpha / A;

            // Normalisation a0 = 1
            double invA0 = 1.0 / a0d;
            _b0 = b0d * invA0;
            _b1 = b1d * invA0;
            _b2 = b2d * invA0;
            _a1 = a1d * invA0;
            _a2 = a2d * invA0;

            // Reset états
            _z1 = _z2 = 0.0;
        }

        /// <summary>Traite un buffer 16-bit mono in-place.</summary>
        public void ProcessBuffer(short[] buf, int nSamples)
        {
            if (buf == null) throw new ArgumentNullException(nameof(buf));
            if (nSamples <= 0 || nSamples > buf.Length) nSamples = buf.Length;

            // Copies locales pour JIT et perf
            double b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
            double z1 = _z1, z2 = _z2;

            for (int i = 0; i < nSamples; i++)
            {
                double x = buf[i]; // on travaille directement en "short-space" (±32768)
                // DF-II transposée
                double y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;

                // Clamp et quantif vers short
                int v = (int)Math.Round(y);
                if (v > short.MaxValue) v = short.MaxValue;
                else if (v < short.MinValue) v = short.MinValue;
                buf[i] = (short)v;
            }

            _z1 = z1; _z2 = z2;
        }
    }
}
