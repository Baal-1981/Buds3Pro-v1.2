using System;

namespace Buds3ProAideAuditiveIA.v2
{
    public class DspChain
    {
        readonly Biquad _hpf = new Biquad();
        readonly Biquad _peak = new Biquad();
        readonly Compressor _comp = new Compressor();

        int _sr = 16000;
        int _nrStrength = 6;          // 0..10
        float _noiseFloor = 0.02f;    // calibré

        public DspChain()
        {
            Init(16000);
        }

        public void Init(int sampleRate)
        {
            _sr = sampleRate;
            _hpf.DesignHighpass(_sr, 120.0, 0.707);
            _peak.DesignPeaking(_sr, 2800.0, 1.0, 4.0);

            _comp.SampleRate = _sr;
            _comp.Threshold = DbToLin(-18.0);
            _comp.Ratio = 3.0;
            _comp.AttackMs = 5.0;
            _comp.ReleaseMs = 80.0;
            _comp.MakeupDb = 0.0;
            _comp.Reset();
        }

        public void SetVoiceBoostDb(int db)
        {
            _peak.DesignPeaking(_sr, 2800.0, 1.0, db);
        }

        public void SetNrStrength(int s)
        {
            _nrStrength = Math.Max(0, Math.Min(10, s));
        }

        public void Calibrate()
        {
            _noiseFloor = 0.0f; // sera mesuré au prochain Process
        }

        public void Process(short[] buf, int n)
        {
            // short -> float
            var x = new float[n];
            for (int i = 0; i < n; i++) x[i] = buf[i] / 32768f;

            // Estimation bruit (RMS)
            double rms = 0.0;
            for (int i = 0; i < n; i++) { double v = x[i]; rms += v * v; }
            rms = System.Math.Sqrt(rms / n);
            if (_noiseFloor == 0.0f) _noiseFloor = (float)(rms * 0.8);

            // Noise gate léger dépendant du NR
            float gate = (float)System.Math.Max(0.2, 1.0 - (_nrStrength / 12.0));
            float thr = _noiseFloor * (float)(1.2 - _nrStrength * 0.05);
            for (int i = 0; i < n; i++)
            {
                float a = System.Math.Abs(x[i]);
                if (a < thr) x[i] *= gate;
            }

            // Filtres + compression
            _hpf.ProcessInPlace(x, n);
            _peak.ProcessInPlace(x, n);
            _comp.ProcessInPlace(x, n);

            // Limiteur soft + retour en short
            float limit = (float)DbToLin(-1.0);
            for (int i = 0; i < n; i++)
            {
                float v = x[i];
                if (v > limit) v = limit; if (v < -limit) v = -limit;
                int s = (int)(v * 32767f);
                if (s > 32767) s = 32767; if (s < -32768) s = -32768;
                buf[i] = (short)s;
            }
        }

        static double DbToLin(double db) => System.Math.Pow(10.0, db / 20.0);
    }
}
