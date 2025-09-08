using System;

namespace Buds3ProAideAuditiveIA.v2
{
    public class Biquad
    {
        double a0 = 1, a1 = 0, a2 = 0, b1 = 0, b2 = 0;
        double z1 = 0, z2 = 0;

        public void DesignHighpass(int sr, double fc, double q)
        {
            double w0 = 2 * System.Math.PI * fc / sr;
            double alpha = System.Math.Sin(w0) / (2 * q);
            double cos = System.Math.Cos(w0);

            double b0 = (1 + cos) / 2.0;
            double b1n = -(1 + cos);
            double b2 = (1 + cos) / 2.0;
            double a0n = 1 + alpha;
            double a1n = -2 * cos;
            double a2n = 1 - alpha;

            a0 = b0 / a0n; a1 = b1n / a0n; a2 = b2 / a0n; b1 = a1n / a0n; b2 = a2n / a0n;
        }

        public void DesignPeaking(int sr, double fc, double q, double gainDb)
        {
            double A = System.Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2 * System.Math.PI * fc / sr;
            double alpha = System.Math.Sin(w0) / (2 * q);
            double cos = System.Math.Cos(w0);

            double b0 = 1 + alpha * A;
            double b1n = -2 * cos;
            double b2 = 1 - alpha * A;
            double a0n = 1 + alpha / A;
            double a1n = -2 * cos;
            double a2n = 1 - alpha / A;

            a0 = b0 / a0n; a1 = b1n / a0n; a2 = b2 / a0n; b1 = a1n / a0n; b2 = a2n / a0n;
        }

        public void ProcessInPlace(float[] x, int n)
        {
            double z1l = z1, z2l = z2;
            double a0l = a0, a1l = a1, a2l = a2, b1l = b1, b2l = b2;

            for (int i = 0; i < n; i++)
            {
                double v = x[i];
                double y = v * a0l + z1l;
                z1l = v * a1l + z2l - b1l * y;
                z2l = v * a2l - b2l * y;
                x[i] = (float)System.Math.Max(-1.0, System.Math.Min(1.0, y));
            }

            z1 = z1l; z2 = z2l;
        }
    }
}
