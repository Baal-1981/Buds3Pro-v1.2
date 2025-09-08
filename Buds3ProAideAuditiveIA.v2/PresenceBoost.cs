using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Petit “peak EQ” autour de 2.5 kHz (+3 à +5 dB) pour rendre les consonnes plus nettes.
    /// Biquad peaking standard (RBJ).
    /// </summary>
    public sealed class PresenceBoost
    {
        private double a0, a1, a2, b1, b2;
        private double z1, z2;

        public PresenceBoost(int sampleRate, double freqHz = 2500.0, double gainDb = 4.0, double q = 0.9)
        {
            Reconfigure(sampleRate, freqHz, gainDb, q);
        }

        public void Reconfigure(int sr, double freq, double gainDb, double q)
        {
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * Math.Max(20.0, Math.Min(freq, sr * 0.45)) / sr;
            double alpha = Math.Sin(w0) / (2.0 * Math.Max(0.2, q));
            double cosw0 = Math.Cos(w0);

            double b0 = 1.0 + alpha * A;
            double b1n = -2.0 * cosw0;
            double b2n = 1.0 - alpha * A;
            double a0n = 1.0 + alpha / A;
            double a1n = -2.0 * cosw0;
            double a2n = 1.0 - alpha / A;

            // normalisation en Direct Form I
            a0 = b0 / a0n;
            a1 = b1n / a0n;
            a2 = b2n / a0n;
            b1 = a1n / a0n;
            b2 = a2n / a0n;

            z1 = z2 = 0.0;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private double ProcessOne(double x)
        {
            // Direct Form I
            double y = a0 * x + z1;
            z1 = a1 * x - b1 * y + z2;
            z2 = a2 * x - b2 * y;
            return y;
        }

        public void ProcessBuffer(short[] buf, int nSamples)
        {
            for (int i = 0; i < nSamples; i++)
            {
                double x = buf[i] * (1.0 / 32768.0);
                double y = ProcessOne(x);
                int v = (int)Math.Round(y * 32768.0);
                if (v > short.MaxValue) v = short.MaxValue;
                else if (v < short.MinValue) v = short.MinValue;
                buf[i] = (short)v;
            }
        }
    }
}
