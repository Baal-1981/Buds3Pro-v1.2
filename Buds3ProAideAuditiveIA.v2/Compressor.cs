using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Compresseur à genou doux (soft-knee) avec détection en dB, attack/release lissés,
    /// make-up gain et ceiling (limiteur simple).
    /// API:
    ///   - régler ThresholdDb, Ratio, KneeDb, AttackMs, ReleaseMs, MakeupDb, CeilingDb, SampleRate
    ///   - Reset()
    ///   - ProcessInPlace(float[] x, int n)
    ///   - LastGainReductionDb (lecture seule)
    /// </summary>
    public class Compressor
    {
        // ==== Paramètres utilisateur ====
        public double ThresholdDb = -10.0; // dBFS (p.ex. -10 dB)
        public double Ratio = 4.0;   // 4:1
        public double KneeDb = 6.0;   // largeur du genou doux en dB (0 = hard-knee)
        public double AttackMs = 5.0;   // ms
        public double ReleaseMs = 50.0;  // ms
        public double MakeupDb = 0.0;   // dB
        public double CeilingDb = -0.5;  // dBFS (limiteur final)
        public int SampleRate = 48000; // Hz

        // ==== États internes ====
        private double _grDb = 0.0;  // réduction de gain lissée (dB, négative ou 0)
        public float LastGainReductionDb { get; private set; } // valeur positive à afficher

        // détecteur RMS/peak simple (en dB) — on utilise |x| (peak) lissé par attack/release
        private double _envDb = double.NegativeInfinity;

        public void Reset()
        {
            _grDb = 0.0;
            _envDb = double.NegativeInfinity;
            LastGainReductionDb = 0f;
        }

        public void ProcessInPlace(float[] x, int n)
        {
            if (x == null || n <= 0) return;
            if (n > x.Length) n = x.Length;

            // Coefficients de lissage (0..1). Formule standard exp(-1/(tau*fs)).
            // On reste en échelle "linéaire" pour calculer la vitesse, mais on lisse des dB.
            double attA = ExpCoef(AttackMs, SampleRate);
            double relA = ExpCoef(ReleaseMs, SampleRate);

            double knee = Math.Max(0.0, KneeDb);
            double thr = ThresholdDb;
            double ratio = Math.Max(1.0, Ratio);
            double makeupLin = DbToLin(MakeupDb);
            double ceilLin = DbToLin(CeilingDb);

            for (int i = 0; i < n; i++)
            {
                // 1) Détection niveau instantané (peak) en dB
                double a = Math.Abs(x[i]) + 1e-12; // évite log(0)
                double levelDb = 20.0 * Math.Log10(a);

                // Lissage du détecteur (en dB) — plus rapide quand ça monte (attack)
                if (levelDb > _envDb)
                    _envDb = levelDb + attA * (_envDb - levelDb);
                else
                    _envDb = levelDb + relA * (_envDb - levelDb);

                // 2) Courbe de compression soft-knee
                // Formule standard:
                //   e = L - T
                //   si 2e < -K: GR = 0
                //   si 2e >  K: GR = (1 - 1/R) * e
                //   sinon:       GR = (1 - 1/R) * (e + K/2)^2 / (2K)
                // Ici GR >= 0 (en dB). Le gain appliqué sera -GR (réduction).
                double e = _envDb - thr;
                double GR; // positive
                if (knee <= 0.0)
                {
                    GR = (e > 0.0) ? (1.0 - 1.0 / ratio) * e : 0.0;
                }
                else
                {
                    if (2 * e <= -knee) GR = 0.0;
                    else if (2 * e >= knee) GR = (1.0 - 1.0 / ratio) * e;
                    else
                    {
                        double t = e + knee / 2.0;
                        GR = (1.0 - 1.0 / ratio) * (t * t) / (2.0 * knee);
                    }
                }

                // 3) Lissage de la réduction de gain (en dB)
                double targetGrDb = -GR; // négatif (0 ou <0)
                if (targetGrDb < _grDb) // plus de réduction -> attack (va vers des valeurs plus négatives)
                    _grDb = targetGrDb - attA * (targetGrDb - _grDb);
                else                    // relâche -> release
                    _grDb = targetGrDb - relA * (targetGrDb - _grDb);

                LastGainReductionDb = (float)(-_grDb); // positif pour l’affichage

                // 4) Applique le gain + make-up
                double g = DbToLin(_grDb) * makeupLin; // _grDb est <= 0
                double y = x[i] * g;

                // 5) Ceiling / Limiter simple (clip doux)
                if (y > ceilLin) y = ceilLin;
                if (y < -ceilLin) y = -ceilLin;

                x[i] = (float)y;
            }
        }

        // ===== Utils =====
        private static double DbToLin(double db) => Math.Pow(10.0, db / 20.0);

        // coef de lissage exp(-1/(tau*fs)) avec tau en ms
        private static double ExpCoef(double ms, int fs)
        {
            ms = Math.Max(0.1, ms);
            fs = Math.Max(1000, fs);
            double tau = ms / 1000.0;
            return Math.Exp(-1.0 / (tau * fs));
        }
    }
}
