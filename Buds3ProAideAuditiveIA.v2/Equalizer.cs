using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Égaliseur 3 bandes léger : Low-shelf (Bass), Peaking (Presence), High-shelf (Treble).
    /// API:
    ///   - Equalizer3Band(int sampleRate)
    ///   - SetEnabled(bool)
    ///   - SetBassDb(int), SetBassFreqHz(int)
    ///   - SetPresenceDb(int), SetPresenceHz(int), SetPresenceQ(float)
    ///   - SetTrebleDb(int), SetTrebleFreqHz(int)
    ///   - Reconfigure(int sampleRate), Reset()
    ///   - ProcessBuffer(short[] buf, int count)
    /// </summary>
    public sealed class Equalizer3Band
    {
        private int _fs;

        // Réglages
        private bool _enabled = false;

        private int _bassDb = 0;        // [-12..+12]
        private int _bassHz = 120;      // [40..400]

        private int _presenceDb = 0;    // [-8..+8]
        private int _presenceHz = 2000; // [1000..3000]
        private float _presenceQ = 1.0f;// [0.4..2.5]

        private int _trebleDb = 0;      // [-12..+12]
        private int _trebleHz = 6500;   // [2000..10000]

        // Sections biquad internes (RBJ), DF-II transposée
        private readonly BiquadSec _low = new BiquadSec();
        private readonly BiquadSec _mid = new BiquadSec();
        private readonly BiquadSec _high = new BiquadSec();

        public Equalizer3Band(int sampleRate) => Reconfigure(sampleRate);

        public void SetEnabled(bool on) => _enabled = on;

        public void SetBassDb(int db) { _bassDb = Clamp(db, -12, +12); UpdateCoeffs(); }
        public void SetBassFreqHz(int hz) { _bassHz = Clamp(hz, 40, 400); UpdateCoeffs(); }

        public void SetPresenceDb(int db) { _presenceDb = Clamp(db, -8, +8); UpdateCoeffs(); }
        public void SetPresenceHz(int hz) { _presenceHz = Clamp(hz, 1000, 3000); UpdateCoeffs(); }
        public void SetPresenceQ(float q) { _presenceQ = Clamp(q, 0.4f, 2.5f); UpdateCoeffs(); }

        public void SetTrebleDb(int db) { _trebleDb = Clamp(db, -12, +12); UpdateCoeffs(); }
        public void SetTrebleFreqHz(int hz) { _trebleHz = Clamp(hz, 2000, 10000); UpdateCoeffs(); }

        public void Reconfigure(int sampleRate)
        {
            _fs = Math.Max(8000, sampleRate);
            UpdateCoeffs();
            Reset();
        }

        public void Reset()
        {
            _low.Reset(); _mid.Reset(); _high.Reset();
        }

        /// <summary>Traite un buffer 16-bit in-place. Sans effet si désactivé ou gains = 0.</summary>
        public void ProcessBuffer(short[] buf, int count)
        {
            if (!_enabled || buf == null || count <= 0) return;
            if (count > buf.Length) count = buf.Length;

            bool doLow = (_bassDb != 0);
            bool doMid = (_presenceDb != 0);
            bool doHigh = (_trebleDb != 0);

            if (!(doLow || doMid || doHigh)) return;

            for (int i = 0; i < count; i++)
            {
                double x = buf[i];

                if (doLow) x = _low.Process(x);
                if (doMid) x = _mid.Process(x);
                if (doHigh) x = _high.Process(x);

                int v = (int)Math.Round(x);
                if (v > short.MaxValue) v = short.MaxValue;
                else if (v < short.MinValue) v = short.MinValue;
                buf[i] = (short)v;
            }
        }

        private void UpdateCoeffs()
        {
            int fs = Math.Max(8000, _fs);

            // Low-shelf (RBJ)
            if (_bassDb == 0)
                _low.SetBypass();
            else
                _low.DesignLowShelf(fs, _bassHz, _bassDb, 1.0);

            // Presence (peaking)
            if (_presenceDb == 0)
                _mid.SetBypass();
            else
                _mid.DesignPeaking(fs, _presenceHz, _presenceQ, _presenceDb);

            // High-shelf (RBJ)
            if (_trebleDb == 0)
                _high.SetBypass();
            else
                _high.DesignHighShelf(fs, _trebleHz, _trebleDb, 1.0);
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        // ====== Section biquad RBJ (coeffs normalisés, a0=1), DF-II transposée ======
        private sealed class BiquadSec
        {
            // Coeffs
            double b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
            // États
            double z1 = 0, z2 = 0;

            public void Reset() { z1 = z2 = 0; }
            public void SetBypass() { b0 = 1; b1 = b2 = a1 = a2 = 0; Reset(); }

            public double Process(double x)
            {
                double y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }

            public void DesignPeaking(int fs, double fc, double q, int gainDb)
            {
                if (gainDb == 0) { SetBypass(); return; }
                double A = Math.Pow(10.0, gainDb / 40.0);
                double w0 = 2.0 * Math.PI * fc / fs;
                double cosw = Math.Cos(w0), sinw = Math.Sin(w0);
                double alpha = sinw / (2.0 * q);

                double b0d = 1 + alpha * A;
                double b1d = -2 * cosw;
                double b2d = 1 - alpha * A;
                double a0d = 1 + alpha / A;
                double a1d = -2 * cosw;
                double a2d = 1 - alpha / A;

                double inv = 1.0 / a0d;
                b0 = b0d * inv; b1 = b1d * inv; b2 = b2d * inv;
                a1 = a1d * inv; a2 = a2d * inv;
                Reset();
            }

            public void DesignLowShelf(int fs, double fc, int gainDb, double S = 1.0)
            {
                if (gainDb == 0) { SetBypass(); return; }
                double A = Math.Pow(10.0, gainDb / 40.0);
                double w0 = 2.0 * Math.PI * fc / fs;
                double cosw = Math.Cos(w0), sinw = Math.Sin(w0);
                double alpha = sinw / 2.0 * Math.Sqrt((A + 1.0 / A) * (1.0 / S - 1.0) + 2.0);

                double b0d = A * ((A + 1) - (A - 1) * cosw + 2 * Math.Sqrt(A) * alpha);
                double b1d = 2 * A * ((A - 1) - (A + 1) * cosw);
                double b2d = A * ((A + 1) - (A - 1) * cosw - 2 * Math.Sqrt(A) * alpha);
                double a0d = (A + 1) + (A - 1) * cosw + 2 * Math.Sqrt(A) * alpha;
                double a1d = -2 * ((A - 1) + (A + 1) * cosw);
                double a2d = (A + 1) + (A - 1) * cosw - 2 * Math.Sqrt(A) * alpha;

                double inv = 1.0 / a0d;
                b0 = b0d * inv; b1 = b1d * inv; b2 = b2d * inv;
                a1 = a1d * inv; a2 = a2d * inv;
                Reset();
            }

            public void DesignHighShelf(int fs, double fc, int gainDb, double S = 1.0)
            {
                if (gainDb == 0) { SetBypass(); return; }
                double A = Math.Pow(10.0, gainDb / 40.0);
                double w0 = 2.0 * Math.PI * fc / fs;
                double cosw = Math.Cos(w0), sinw = Math.Sin(w0);
                double alpha = sinw / 2.0 * Math.Sqrt((A + 1.0 / A) * (1.0 / S - 1.0) + 2.0);

                double b0d = A * ((A + 1) + (A - 1) * cosw + 2 * Math.Sqrt(A) * alpha);
                double b1d = -2 * A * ((A - 1) + (A + 1) * cosw);
                double b2d = A * ((A + 1) + (A - 1) * cosw - 2 * Math.Sqrt(A) * alpha);
                double a0d = (A + 1) - (A - 1) * cosw + 2 * Math.Sqrt(A) * alpha;
                double a1d = 2 * ((A - 1) - (A + 1) * cosw);
                double a2d = (A + 1) - (A - 1) * cosw - 2 * Math.Sqrt(A) * alpha;

                double inv = 1.0 / a0d;
                b0 = b0d * inv; b1 = b1d * inv; b2 = b2d * inv;
                a1 = a1d * inv; a2 = a2d * inv;
                Reset();
            }
        }
    }
}
