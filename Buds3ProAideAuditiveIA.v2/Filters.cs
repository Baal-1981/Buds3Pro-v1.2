using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Passe-haut 1er ordre : y[n] = a * (y[n-1] + x[n] - x[n-1])
    /// Par défaut ~100–120 Hz. État en double, E/S en Int16.
    /// </summary>
    public sealed class Highpass100Hz
    {
        // État (espace linéaire -1..+1)
        private double _xPrev, _yPrev;
        // Coefficient a (0..1)
        private double _a;

        /// <param name="sampleRate">Hz (ex: 48000)</param>
        /// <param name="fc">Fréquence de coupure (Hz, ex: 100–150)</param>
        public Highpass100Hz(int sampleRate = 48000, double fc = 100.0)
        {
            Reconfigure(sampleRate, fc);
        }

        /// <summary>Recalcule le coefficient à partir de SR/fc.</summary>
        public void Reconfigure(int sampleRate, double fc)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            // bornes raisonnables pour stabilité/usage audio
            fc = Math.Max(20.0, Math.Min(fc, sampleRate * 0.45));

            double dt = 1.0 / sampleRate;
            double rc = 1.0 / (2.0 * Math.PI * fc);
            _a = rc / (rc + dt);              // 0..1
        }

        /// <summary>Réinitialise l’état interne.</summary>
        public void Reset()
        {
            _xPrev = 0; _yPrev = 0;
        }

        /// <summary>Traite un échantillon 16-bit.</summary>
        public short Process(short s)
        {
            // convertit en [-1,1]
            double x = s / 32768.0;
            // y = a * (y_prev + x - x_prev)
            double y = _a * (_yPrev + x - _xPrev);
            _xPrev = x; _yPrev = y;

            // clamp -> Int16
            if (y > 1.0) y = 1.0; else if (y < -1.0) y = -1.0;
            return (short)Math.Round(y * 32767.0);
        }

        /// <summary>Traite un buffer in-place.</summary>
        public void ProcessBuffer(short[] buf, int count)
        {
            if (buf == null) return;
            if (count <= 0 || count > buf.Length) count = buf.Length;

            double a = _a, xPrev = _xPrev, yPrev = _yPrev;

            for (int i = 0; i < count; i++)
            {
                double x = buf[i] / 32768.0;
                double y = a * (yPrev + x - xPrev);
                xPrev = x; yPrev = y;

                if (y > 1.0) y = 1.0; else if (y < -1.0) y = -1.0;
                buf[i] = (short)Math.Round(y * 32767.0);
            }

            _xPrev = xPrev; _yPrev = yPrev;
        }
    }

    /// <summary>
    /// Tilt/Clarity très léger : ajoute une composante "hautes fréquences"
    /// type (x - a * x[n-1]) et un léger makeup. Simple et peu coûteux.
    /// </summary>
    public sealed class TiltEqClarity
    {
        // État
        private double _xPrev;
        // Coeff de "suivi" (0..1) pour la composante HP approx (x - a*x[n-1])
        private double _a;
        // Gain de compensation
        private double _makeup;
        // Mix dry/wet (0..1) : 0 = dry pur, 1 = uniquement accent HF
        private double _mix;

        /// <param name="a">Coefficient 0..1 (ex: 0.85)</param>
        /// <param name="makeupDb">Makeup en dB (ex: +2 dB)</param>
        /// <param name="mix">Mix HF 0..1 (ex: 0.8)</param>
        public TiltEqClarity(double a = 0.85, double makeupDb = 2.0, double mix = 0.8)
        {
            SetAlpha(a);
            SetMakeupDb(makeupDb);
            SetMix(mix);
        }

        /// <summary>Réinitialise l’état interne.</summary>
        public void Reset() => _xPrev = 0;

        /// <summary>Règle le coefficient (0..1). Plus près de 1 = effet plus doux.</summary>
        public void SetAlpha(double a)
        {
            _a = Math.Min(0.999, Math.Max(0.0, a));
        }

        /// <summary>Règle le makeup en dB.</summary>
        public void SetMakeupDb(double makeupDb)
        {
            // borne raisonnable pour éviter trop de gain
            makeupDb = Math.Max(-6.0, Math.Min(6.0, makeupDb));
            _makeup = Math.Pow(10.0, makeupDb / 20.0);
        }

        /// <summary>Règle le mix dry/wet (0..1).</summary>
        public void SetMix(double mix)
        {
            _mix = Math.Min(1.0, Math.Max(0.0, mix));
        }

        /// <summary>Traite un échantillon.</summary>
        public short Process(short s)
        {
            double x = s / 32768.0;

            // composante "HF" simple : hp ~ x - a*x[n-1]
            double hp = x - _a * _xPrev;
            _xPrev = x;

            // mix : y = dry*(1-mix) + (hp*makeup)*mix
            double y = (1.0 - _mix) * x + _mix * (hp * _makeup);

            if (y > 1.0) y = 1.0; else if (y < -1.0) y = -1.0;
            return (short)Math.Round(y * 32767.0);
        }

        /// <summary>Traite un buffer in-place.</summary>
        public void ProcessBuffer(short[] buf, int count)
        {
            if (buf == null) return;
            if (count <= 0 || count > buf.Length) count = buf.Length;

            double a = _a, makeup = _makeup, mix = _mix, xPrev = _xPrev;

            for (int i = 0; i < count; i++)
            {
                double x = buf[i] / 32768.0;
                double hp = x - a * xPrev;
                xPrev = x;

                double y = (1.0 - mix) * x + mix * (hp * makeup);

                if (y > 1.0) y = 1.0; else if (y < -1.0) y = -1.0;
                buf[i] = (short)Math.Round(y * 32767.0);
            }

            _xPrev = xPrev;
        }
    }
}
