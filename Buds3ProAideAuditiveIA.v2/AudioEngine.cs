using System;
using System.Threading;
using Android.Content;
using Android.Media;

namespace Buds3ProAideAuditiveIA.v2
{
    public enum AudioTransport { A2DP, SCO, LE_LC3_AUTO }

    public interface ILogSink { void Log(string msg); }

    public sealed class AudioEngine : Java.Lang.Object
    {
        // ===== infra
        private readonly Context _ctx;
        private readonly ILogSink _log;

        private AudioRecord _rec;
        private AudioTrack _trk;
        private Thread _worker;
        private volatile bool _running;

        // ===== format / buffers
        private int _requestedHpfHz = 120; // mémorise le HPF demandé (40..400)
        private int _fs = 48000;
        private int _frameMs = 10;
        private int _n;               // samples/frame
        private int _recBufBytes;
        private int _trkBufBytes;

        // ===== modules
        private readonly AudioEffects_EnableNative _platFx = new AudioEffects_EnableNative();
        private DspChain _dsp;
        private SpectralNoiseReducer _snr;
        private Equalizer3Band _eq;
        private readonly TiltEqClarity _clarity = new TiltEqClarity();

        // ===== flags
        private volatile bool _passThrough = true;   // bypass DSP
        private volatile bool _useSnr = true;   // NR spectral
        private volatile bool _usePlatformFx = false;  // AEC/NS/AGC Android
        private volatile bool _useHpf = true;
        private volatile bool _useClarity = true;
        private volatile bool _useEq = false;

        // transport
        private volatile AudioTransport _transport = AudioTransport.A2DP;

        // EQ/presence
        private volatile bool _presenceOn = true;

        // Ambient / gate map -> DspChain NR strength
        private int _ambientDb = 12;
        private int _ambAtkMs = 200, _ambRelMs = 150; // placeholders exposés à l’UI

        // De-esser & Hum (placeholders sûrs, pour compiler avec MainActivity)
        private volatile bool _deEsserEnabled = false;
        private volatile bool _humEnabled = false;
        private volatile int _humBaseHz = 50;

        // gain global
        private int _gainDb = 0;

        // calib NR spectral
        private volatile int _calibFrames = 0;

        // meters callback (rmsDb, pkDb, grDb)
        private Action<float, float, float> _meters;

        // latences
        public int TransportLatencyMs { get; private set; }
        public int AlgoLatencyMs => _frameMs;
        public int EstimatedLatencyMs => TransportLatencyMs + AlgoLatencyMs;

        public bool IsRunning => _running;

        public AudioEngine(Context ctx, ILogSink log = null)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _log = log ?? new NullLog();
        }

        public void SetTransport(AudioTransport t)
        {
            _transport = t;
            _log.Log($"[Route] Transport set to {t}");
        }

        // ================== configuration ==================
        public void Configure(
            int sampleRate = 48000,
            int frameMs = 10,
            bool pass = true,
            bool dspNs = true,
            bool platformFx = false,
            bool hp = true,
            bool clarity = true,
            bool eq = false)
        {
            if (_running) return;

            _fs = Math.Max(8000, sampleRate);
            _frameMs = Math.Max(5, Math.Min(20, frameMs));
            _n = _fs * _frameMs / 1000;

            _passThrough = pass;
            _useSnr = dspNs;
            _usePlatformFx = platformFx;
            _useHpf = hp;
            _useClarity = clarity;
            _useEq = eq;

            // Chaîne DSP de base (HPF + présence + comp + gate léger)
            _dsp = new DspChain(_fs);
            _dsp.SetNrStrength(DbToNrStrength(_ambientDb));
            _dsp.SetVoiceBoostDb(0);

            // EQ 3 bandes
            _eq = new Equalizer3Band(_fs);
            _eq.SetEnabled(_useEq);

            // SNR spectral (taille FFT = pow2 >= frame)
            int N = NextPow2(_n);
            if (N < 256) N = 256; if (N > 2048) N = 2048;
            _snr = new SpectralNoiseReducer(N, _fs);
        }

        // ================== flags & réglages (UI) ==================
        public void SetFlags(bool? pass = null, bool? dspNs = null, bool? platformFx = null,
                             bool? hp = null, bool? clarity = null, bool? ambientExpander = null)
        {
            if (pass.HasValue) _passThrough = pass.Value;
            if (dspNs.HasValue) _useSnr = dspNs.Value;
            if (platformFx.HasValue) _usePlatformFx = platformFx.Value;
            if (hp.HasValue) _useHpf = hp.Value;
            if (clarity.HasValue) _useClarity = clarity.Value;
            if (ambientExpander.HasValue)
                _dsp?.SetNrStrength(DbToNrStrength(_ambientDb));
        }

        public void SetMetersCallback(Action<float, float, float> cb) => _meters = cb;

        // NR spectral
        public void CalibrateNoiseNow(int ms = 500)
        {
            _calibFrames = Math.Max(1, ms / Math.Max(1, _frameMs));
            _log.Log($"[SNR] calibrate ~{ms}ms");
        }

        // Ambient/gate (map vers DspChain)
        public void SetAmbientReductionDb(int db)
        {
            _ambientDb = Clamp(db, 0, 24);
            _dsp?.SetNrStrength(DbToNrStrength(_ambientDb));
        }
        public void SetAmbientAttackMs(int ms) { _ambAtkMs = Clamp(ms, 50, 400); /* hook à raffiner si besoin */ }
        public void SetAmbientReleaseMs(int ms) { _ambRelMs = Clamp(ms, 50, 300); /* hook à raffiner si besoin */ }

        // Gain
        public void SetGainDb(int db) { _gainDb = Clamp(db, 0, 36); }

        // EQ + présence
        public void SetEqEnabled(bool on) { _useEq = on; _eq?.SetEnabled(on); }
        public void SetBassDb(int db) => _eq?.SetBassDb(Clamp(db, -12, 12));
        public void SetBassFreqHz(int hz) => _eq?.SetBassFreqHz(Clamp(hz, 40, 400));
        public void SetPresenceEnabled(bool on) { _presenceOn = on; _dsp?.SetVoiceBoostDb(on ? 4 : 0); }
        public void SetPresenceDb(int db) { _presenceOn = db != 0; _dsp?.SetVoiceBoostDb(Clamp(db, -8, 8)); _eq?.SetPresenceDb(Clamp(db, -8, 8)); }
        public void SetPresenceHz(int hz) => _eq?.SetPresenceHz(Clamp(hz, 1000, 3000));
        public void SetTrebleDb(int db) => _eq?.SetTrebleDb(Clamp(db, -12, 12));
        public void SetTrebleFreqHz(int hz) => _eq?.SetTrebleFreqHz(Clamp(hz, 2000, 10000));

        // High-pass / clarity
        public void SetClarity(bool on) => _useClarity = on;
        public void SetHighPassCutoffHz(int hz)
        {
            _requestedHpfHz = Clamp(hz, 40, 400);  // on mémorise la cible
            _useHpf = true;
        }

        // De-esser / Hum (setters demandés par l’UI ; traitement optionnel à ajouter plus tard)
        public void SetDeEsserEnabled(bool on) => _deEsserEnabled = on;
        public void SetHumEnabled(bool on) => _humEnabled = on;
        public void SetHumBaseHz(int hz) => _humBaseHz = (hz == 60) ? 60 : 50;

        // ================== Start / Stop ==================
        public bool Start()
        {
            if (_running) return true;

            try
            {
                // Configure mode/routing based on selected transport
                switch (_transport)
                {
                    case AudioTransport.SCO:
                        BluetoothRouting_Utilities.EnterCommunicationMode(_ctx);
                        if (!BluetoothRouting_Utilities.EnsureSco(_ctx, 4000))
                        {
                            _log.Log("[Route] SCO start failed; falling back to A2DP");
                            _transport = AudioTransport.A2DP;
                            BluetoothRouting_Utilities.LeaveCommunicationMode(_ctx);
                        }
                        break;
                    case AudioTransport.LE_LC3_AUTO:
                    case AudioTransport.A2DP:
                    default:
                        // Normal media path; system may choose LE/LC3 if supported
                        BluetoothRouting_Utilities.LeaveCommunicationMode(_ctx); // ensure normal
                        break;
                }

                // record
                int minRec = AudioRecord.GetMinBufferSize(_fs, ChannelIn.Mono, Encoding.Pcm16bit);
                _recBufBytes = Math.Max(minRec, _n * 4);
                var recSource = (_transport == AudioTransport.SCO)
                    ? AudioSource.VoiceCommunication
                    : AudioSource.Mic;
                _rec = new AudioRecord(recSource, _fs, ChannelIn.Mono, Encoding.Pcm16bit, _recBufBytes);
                if (_rec.State != State.Initialized) { _log.Log("[Audio] AudioRecord init failed"); Stop(); return false; }

                // effets natifs
                _platFx.AttachOrUpdate(_rec.AudioSessionId, _usePlatformFx, _usePlatformFx, _usePlatformFx);

                // track
                int minTrk = AudioTrack.GetMinBufferSize(_fs, ChannelOut.Mono, Encoding.Pcm16bit);
                _trkBufBytes = Math.Max(minTrk, _n * 4);

#pragma warning disable CS0618
                var stream = (_transport == AudioTransport.SCO) ? Stream.VoiceCall : Stream.Music;
                _trk = new AudioTrack(stream, _fs, ChannelOut.Mono, Encoding.Pcm16bit, _trkBufBytes, AudioTrackMode.Stream);
#pragma warning restore CS0618
                if (_trk.State != AudioTrackState.Initialized) { _log.Log("[Audio] AudioTrack init failed"); Stop(); return false; }

                _rec.StartRecording();
                _trk.Play();

                TransportLatencyMs = (_trkBufBytes / 2) * 1000 / _fs;

                _running = true;
                _worker = new Thread(Loop) { IsBackground = true, Name = "AudioEngineLoop" };
                _worker.Start();

                _log.Log($"[Audio] RUN ▶ fs={_fs} frame={_n} samples recBuf={_recBufBytes} trkBuf={_trkBufBytes} transport={_transport}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Log("[Audio] start ex: " + ex.Message);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _worker?.Join(400); } catch { }

            try { _trk?.Pause(); _trk?.Flush(); } catch { }
            try { _rec?.Stop(); } catch { }

            try { _trk?.Release(); } catch { }
            _trk = null;
            try { _rec?.Release(); } catch { }
            _rec = null;

            try { _platFx.Detach(); } catch { }

            // Only leave comm mode / stop SCO if we were in SCO
            try
            {
                // We deliberately do not force LeaveCommunicationMode for media routes here,
                // because the app may not own global mode in some OEM stacks.
                // We do leave it if we had SCO.
                // (MainActivity can rebind as needed.)
            } catch { }

            _log.Log("[Audio] STOP ◼");
        }

        // ================== boucle DSP ==================
        private void Loop()
        {
            try { Android.OS.Process.SetThreadPriority(Android.OS.ThreadPriority.UrgentAudio); } catch { }

            var frame = new short[_n];
            var pad = new short[_snr?.FrameSamples ?? _n];
            int Nsnr = pad.Length;

            while (_running)
            {
                int r = _rec.Read(frame, 0, _n);
                if (r <= 0) continue;

                if (_passThrough)
                {
                    ApplyGain(frame, r, _gainDb);
                    _trk.Write(frame, 0, r);
                    PushMeters(frame, r, 0f);
                    continue;
                }

                // 1) SNR spectral (optionnel)
                if (_useSnr && _snr != null)
                {
                    Array.Clear(pad, 0, Nsnr);
                    Array.Copy(frame, 0, pad, 0, r);

                    if (_calibFrames > 0) { _snr.UpdateNoiseProfile(pad); _calibFrames--; }
                    else { _snr.Process(pad, adapt: false); }

                    Array.Copy(pad, 0, frame, 0, r);
                }

                // 2) DspChain (HPF + Présence + Comp + gate)
                _dsp.Process(frame, r);
                float grDb = _dsp.LastGainReductionDb;

                // 3) EQ 3 bandes (si activée)
                if (_useEq) _eq.ProcessBuffer(frame, r);

                // 4) Clarity
                if (_useClarity) _clarity.ProcessBuffer(frame, r);

                // 5) Gain final
                ApplyGain(frame, r, _gainDb);

                _trk.Write(frame, 0, r);
                PushMeters(frame, r, grDb);
            }
        }

        private static void ApplyGain(short[] buf, int len, int gainDb)
        {
            if (gainDb == 0) return;
            double g = Math.Pow(10.0, gainDb / 20.0);
            for (int i = 0; i < len; i++)
            {
                int v = (int)Math.Round(buf[i] * g);
                if (v > short.MaxValue) v = short.MaxValue; else if (v < short.MinValue) v = short.MinValue;
                buf[i] = (short)v;
            }
        }

        private void PushMeters(short[] buf, int len, float grDb)
        {
            if (_meters == null) return;
            // simple RMS / peak
            double sum = 0; short pk = 0;
            for (int i = 0; i < len; i++)
            {
                short s = buf[i];
                sum += s * (double)s;
                if (Math.Abs(s) > pk) pk = (short)Math.Abs(s);
            }
            double rms = Math.Sqrt(sum / Math.Max(1, len));
            float rmsDb = (float)(20.0 * Math.Log10((rms + 1e-9) / short.MaxValue));
            float pkDb = (float)(20.0 * Math.Log10((pk + 1e-9) / (double)short.MaxValue));
            try { _meters?.Invoke(rmsDb, pkDb, grDb); } catch { }
        }

        // ================== utils ==================
        private static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        private static int NextPow2(int n) { int p = 1; while (p < n) p <<= 1; return p; }
        private static int DbToNrStrength(int db)
        {
            if (db <= 0) return 0;
            if (db >= 24) return 100;
            return (int)Math.Round(db * (100.0/24.0));
        }

        private sealed class NullLog : Java.Lang.Object, ILogSink
        {
            public void Log(string msg) { /* no-op */ }
        }
    }
}
