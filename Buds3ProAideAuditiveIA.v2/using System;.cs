using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Compresseur/limiteur simple en crête, soft-knee implicite via lissage de l'enveloppe.
    /// Entrée/Sortie en PCM 16-bit (short) traité in-place.
    /// </summary>
    public sealed class SimpleCompressor
    {
        readonly double _thrDb;
        readonly double _ratio;
        readonly double _makeupDb;
        readonly double _attA, _relA;

        double _env;        // enveloppe crête lissée [0..1]
        double _gainDb;     // gain lissé en dB

        public SimpleCompressor(int sampleRate, double thresholdDb, double ratio, double makeupDb, int attackMs, int releaseMs)
        {
            _thrDb = thresholdDb;
            _ratio = Math.Max(1.0, ratio);
            _makeupDb = makeupDb;

            _attA = Math.Exp(-1.0 / (attackMs * 0.001 * sampleRate));
            _relA = Math.Exp(-1.0 / (releaseMs * 0.001 * sampleRate));
            _env = 0.0;
            _gainDb = 0.0;
        }

        public void ProcessInPlace(short[] buf, int nSamples)
        {
            // constantes
            const double eps = 1e-12;
            for (int i = 0; i < nSamples; i++)
            {
                // normalisé
                double x = buf[i] / 32768.0;
                double a = Math.Abs(x);

                // enveloppe crête lissée
                if (a > _env) _env = _attA * _env + (1.0 - _attA) * a;
                else _env = _relA * _env + (1.0 - _relA) * a;

                // niveau dBFS
                double levelDb = 20.0 * Math.Log10(_env + eps);

                // gain réducteur en dB : si au-dessus du seuil → compresse
                double over = levelDb - _thrDb;
                double grDb = (over > 0.0) ? -(over - over / _ratio) : 0.0;

                // cible = réduction + make-up
                double targetGainDb = grDb + _makeupDb;

                // lissage du gain utilisé (attaque/release inversés pour éviter pompage)
                if (targetGainDb < _gainDb) // plus de réduction → attaque rapide
                    _gainDb = _attA * _gainDb + (1.0 - _attA) * targetGainDb;
                else                         // moins de réduction → release lent
                    _gainDb = _relA * _gainDb + (1.0 - _relA) * targetGainDb;

                // applique
                double g = Math.Pow(10.0, _gainDb / 20.0);
                int y = (int)Math.Round(x * g * 32768.0);

                // saturation dure (sécurité)
                if (y > short.MaxValue) y = short.MaxValue;
                else if (y < short.MinValue) y = short.MinValue;

                buf[i] = (short)y;
            }
        }
    }
}
