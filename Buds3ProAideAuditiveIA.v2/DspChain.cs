using System;

namespace Buds3ProAideAuditiveIA.v2
{
    public class DspChain
    {
        // Étages
        private readonly Biquad _hpf = new Biquad();  // passe-haut ~120 Hz
        private readonly Biquad _peak = new Biquad();  // présence ~2.8 kHz
        private readonly Compressor _comp = new Compressor(); // compresseur soft-knee

        // État
        private int _sr = 48000;
        private int _nrStrength = 6;       // 0..10 (NR/gate)
        private float _noiseFloor = 0.02f;   // calibré au premier passage

        public DspChain(int sampleRate = 48000)
        {
            Init(sampleRate);
        }

        public void Init(int sampleRate)
        {
            _sr = Math.Max(8000, sampleRate);

            // Filtres
            _hpf.DesignHighpass(_sr, 120.0, 0.707);     // HPF doux
            _peak.DesignPeaking(_sr, 2800.0, 1.0, 4.0); // boost présence par défaut +4 dB

            // Compresseur (notre version utilise des dB pour le threshold)
            _comp.SampleRate = _sr;
            _comp.ThresholdDb = -18.0;   // dBFS
            _comp.Ratio = 3.0;
            _comp.KneeDb = 6.0;
            _comp.AttackMs = 5.0;
            _comp.ReleaseMs = 80.0;
            _comp.MakeupDb = 0.0;
            _comp.CeilingDb = -1.0;   // petit plafond
            _comp.Reset();

            _noiseFloor = 0.02f; // valeur sûre, recalibrée à la demande
        }

        // === Réglages exposés ===
        public void SetVoiceBoostDb(int db)
        {
            // Plage raisonnable pour éviter l’over-EQ
            db = Math.Max(-8, Math.Min(+8, db));
            _peak.DesignPeaking(_sr, 2800.0, 1.0, db);
        }

        public void SetNrStrength(int s)
        {
            _nrStrength = Math.Max(0, Math.Min(10, s));
        }

        public void Calibrate()
        {
            // Au prochain Process, on mesurera le plancher automatique sur quelques frames
            _noiseFloor = 0.0f;
        }

        public float LastGainReductionDb => _comp.LastGainReductionDb;

        // === Chaîne de traitement courte et efficace ===
        public void Process(short[] buf, int n)
        {
            if (buf == null || n <= 0) return;
            if (n > buf.Length) n = buf.Length;

            // short -> float [-1..+1]
            var x = new float[n];
            for (int i = 0; i < n; i++) x[i] = buf[i] / 32768f;

            // Estimation RMS (pour gate + calibration bruit)
            double sum = 0.0;
            for (int i = 0; i < n; i++) { double v = x[i]; sum += v * v; }
            double rms = Math.Sqrt(sum / Math.Max(1, n));

            if (_noiseFloor == 0.0f)
                _noiseFloor = (float)(rms * 0.8); // calibrage “silence” conservateur

            // Gate très léger dépendant du NR
            float gateMul = (float)Math.Max(0.2, 1.0 - (_nrStrength / 12.0)); // plus NR élevé => gate un poil plus fort
            float thr = _noiseFloor * (float)(1.2 - _nrStrength * 0.05);
            for (int i = 0; i < n; i++)
            {
                float a = Math.Abs(x[i]);
                if (a < thr) x[i] *= gateMul;
            }

            // Filtres
            _hpf.ProcessInPlace(x, n);
            _peak.ProcessInPlace(x, n);

            // Compression (soft-knee + ceiling interne)
            _comp.ProcessInPlace(x, n);

            // Clamp doux final (sécurité)
            float limit = (float)Math.Pow(10.0, (-1.0 / 20.0)); // ~ -1 dBFS
            for (int i = 0; i < n; i++)
            {
                float v = x[i];
                if (v > limit) v = limit; else if (v < -limit) v = -limit;

                int s = (int)(v * 32767f);
                if (s > 32767) s = 32767; else if (s < -32768) s = -32768;
                buf[i] = (short)s;
            }
        }
    }
}
