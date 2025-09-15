using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Biquad IIR en Direct Form II (RBJ).
    /// Corrigé pour éliminer IDE0059 (noms de variables clairs, pas d’ombre),
    /// et éviter l’assignation à un champ readonly.
    /// </summary>
    public class Biquad
    {
        // Coefficients normalisés (a0 = 1)
        private double _b0 = 1.0, _b1 = 0.0, _b2 = 0.0;
        private double _a1 = 0.0, _a2 = 0.0;

        // États (DF-II)
        private double _z1 = 0.0, _z2 = 0.0;

        /// <summary>Réinitialise l’état interne (z1/z2).</summary>
        public void Reset()
        {
            _z1 = 0.0; _z2 = 0.0;
        }

        /// <summary>
        /// Conçoit un filtre passe-haut (High-pass) RBJ.
        /// sr: sample rate, fc: fréquence de coupure, q: facteur de qualité.
        /// </summary>
        public void DesignHighpass(int sr, double fc, double q)
        {
            double w0 = 2.0 * Math.PI * fc / sr;
            double cosw = Math.Cos(w0);
            double sinw = Math.Sin(w0);
            double alpha = sinw / (2.0 * q);

            // Coeffs non normalisés (RBJ)
            double b0 = (1 + cosw) / 2.0;
            double b1 = -(1 + cosw);
            double b2 = (1 + cosw) / 2.0;
            double a0 = 1 + alpha;
            double a1 = -2 * cosw;
            double a2 = 1 - alpha;

            // Normalisation a0 = 1
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;

            Reset();
        }

        /// <summary>
        /// Conçoit un peaking EQ (RBJ).
        /// gainDb &gt; 0 = bosse, &lt; 0 = creux.
        /// </summary>
        public void DesignPeaking(int sr, double fc, double q, double gainDb)
        {
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * fc / sr;
            double cosw = Math.Cos(w0);
            double sinw = Math.Sin(w0);
            double alpha = sinw / (2.0 * q);

            // Coeffs non normalisés (RBJ)
            double b0 = 1 + alpha * A;
            double b1 = -2 * cosw;
            double b2 = 1 - alpha * A;
            double a0 = 1 + alpha / A;
            double a1 = -2 * cosw;
            double a2 = 1 - alpha / A;

            // Normalisation a0 = 1
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;

            Reset();
        }

        /// <summary>
        /// Traite un buffer mono in-place (amplitude attendue [-1;1]).
        /// </summary>
        public void ProcessInPlace(float[] x, int n)
        {
            double b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
            double z1 = _z1, z2 = _z2;

            for (int i = 0; i < n; i++)
            {
                double v = x[i];

                // DF-II transposée
                double y = v * b0 + z1;
                z1 = v * b1 + z2 - a1 * y;
                z2 = v * b2 - a2 * y;

                // Clamp doux pour éviter les dépassements
                if (y > 1.0) y = 1.0;
                else if (y < -1.0) y = -1.0;

                x[i] = (float)y;
            }

            _z1 = z1; _z2 = z2;
        }
    }
}
