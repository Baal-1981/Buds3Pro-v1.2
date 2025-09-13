using System;
using System.Threading;
using Android.Media;
using Android.Media.Audiofx;

namespace Buds3ProAideAuditivelA.v2
{
    public interface ILogSink { void Log(string msg); }

    public sealed class AudioEngine : Java.Lang.Object
    {
        private readonly ILogSink _log;

        // Matériel
        private AudioRecord _rec;
        private AudioTrack _track;

        // Effets Android optionnels
        private NoiseSuppressor _ns;
        private AutomaticGainControl _agc;
        private AcousticEchoCanceler _aec;

        // Threading
        private Thread _ioThread;
        private volatile bool _run;
        private readonly ManualResetEventSlim _loopExited = new ManualResetEventSlim(true);

        // Compteurs pour estimation transport
        private long _framesWritten;
        private long _framesRead;

        // Flags runtime
        public volatile bool PassThroughEnabled = true;
        public volatile bool HighPassEnabled;
        public volatile bool ClarityEnabled;
        public volatile bool PlatformNS_AGC_AEC_Enabled;
        public volatile bool DspNoiseSuppressEnabled = false;
        public volatile bool VadEnabled = true;
        public volatile bool AmbientExpanderEnabled = true;

        // ===== Égalisation large bandes =====
        public volatile bool EqEnabled = false;
        private int _bassDb = 0, _trebleDb = 0;
        private int _bassHz = 120, _trebleHz = 4000;
        private Biquad _ls = new Biquad();
        private Biquad _hs = new Biquad();

        // ===== Présence voix =====
        private volatile bool _presenceEnabled = true;
        private int _presenceDb = 0;
        private int _presenceHz = 2000;
        private float _presenceQ = 1.0f;
        private Biquad _presence = new Biquad();

        // ===== Expander ambiant =====
        private volatile int _ambientReductionDb = 12;
        private float _currentAmbGain = 1.0f;
        private int _ambAttackMs = 200;
        private int _ambReleaseMs = 150;
        private const int AMB_HANG_MS = 280;
        private int _hangFramesLeft = 0;

        // ===== Hum remover =====
        private volatile bool _humEnabled = false;
        private int _humBaseHz = 60;
        private Biquad _hum1 = new Biquad(), _hum2 = new Biquad(), _hum3 = new Biquad();

        // ===== De-esser =====
        private volatile bool _deEsserEnabled = false;
        private int _deEsserMaxDb = 6;
        private const int DEESS_START_HZ = 6000;
        private float _hpDeessAlpha = 0f;
        private float _hpDeessY = 0f, _hpDeessXprev = 0f;
        private Biquad _deessShelf = new Biquad();

        // ===== Meters =====
        private Action<float, float, float> _metersCb;

        // Paramètres généraux
        public int SampleRate { get; private set; } = 0;
        public int FrameMs { get; private set; } = 10;
        private int _samplesPerFrame;
        private int _bytesPerFrame;

        // Buffers effectifs
        private int _recBufBytes;
        private int _trackBufBytes;

        // Gain
        private volatile float _preGainLinear = 1.0f;
        public void SetGainDb(int db) { if (db < 0) db = 0; if (db > 36) db = 36; _preGainLinear = (float)Math.Pow(10.0, db / 20.0); }

        // Compresseur/limiteur
        private const float COMP_KNEE_DB = 6f;
        private const float COMP_THRESHOLD_DB = -10f;
        private const float COMP_RATIO = 4f;
        private const float LIMIT_CEIL = 0.98f;

        // VAD / énergie
        private double _noiseRmsRef = 90;

        // High-pass (1er ordre)
        private int _hpCutHz = 120;
        private float _hpAlpha = 0.0f;
        private short _hpXprev = 0;
        private float _hpYprev = 0f;

        // NR spectrale
        private volatile bool _haveNoiseProfile;
        private float[] _noiseMag;
        private float[] _magPrevMin;
        private const float NR_ALPHA = 0.6f;
        private const float NR_FLOOR = 0.5f;
        private const float NR_ADAPT_DECAY = 0.995f;

        // FFT scratch
        private float[] _fftRe, _fftIm, _win;
        private int _fftN, _fftBits;

        public AudioEngine(ILogSink log) { _log = log ?? new NullSink(); }

        public void Configure(int sampleRate, int frameMs,
                              bool pass, bool gateIgnored, bool duckerIgnored, bool hp, bool clarity)
        {
            SampleRate = sampleRate;
            FrameMs = frameMs;
            PassThroughEnabled = pass;
            HighPassEnabled = hp;
            ClarityEnabled = clarity;
        }

        public void SetFlags(bool? pass = null, bool? gate = null, bool? hp = null, bool? clarity = null,
                             bool? platformFx = null, bool? dspNs = null, bool? vad = null, bool? ambientExpander = null)
        {
            if (pass.HasValue) PassThroughEnabled = pass.Value;
            if (hp.HasValue) HighPassEnabled = hp.Value;
            if (clarity.HasValue) ClarityEnabled = clarity.Value;
            if (platformFx.HasValue) PlatformNS_AGC_AEC_Enabled = platformFx.Value;
            if (dspNs.HasValue) DspNoiseSuppressEnabled = dspNs.Value;
            if (vad.HasValue) VadEnabled = vad.Value;
            if (gate.HasValue) AmbientExpanderEnabled = gate.Value;
            if (ambientExpander.HasValue) AmbientExpanderEnabled = ambientExpander.Value;
        }

        // EQ API
        public void SetEqEnabled(bool on) { EqEnabled = on; UpdateEqCoeffs(); }
        public void SetBassDb(int db) { _bassDb = Clamp(db, -12, +12); UpdateEqCoeffs(); }
        public void SetTrebleDb(int db) { _trebleDb = Clamp(db, -12, +12); UpdateEqCoeffs(); }
        public void SetBassFreqHz(int hz) { _bassHz = Clamp(hz, 40, 400); UpdateEqCoeffs(); }
        public void SetTrebleFreqHz(int hz) { _trebleHz = Clamp(hz, 2000, 10000); UpdateEqCoeffs(); }

        private void UpdateEqCoeffs()
        {
            int fs = CurrentSampleRate();
            _ls.MakeLowShelf(fs, _bassHz, _bassDb);
            _hs.MakeHighShelf(fs, _trebleHz, _trebleDb);
            _presence.MakePeaking(fs, _presenceHz, _presenceQ, _presenceDb);
            _deessShelf.MakeHighShelf(fs, 6500, 0);
            MakeHumNotches(fs);
            RecomputeHpAlpha();
            RecomputeHpDeessAlpha();
        }

        // Présence
        public void SetPresenceEnabled(bool on) { _presenceEnabled = on; UpdateEqCoeffs(); }
        public void SetPresenceDb(int db) { _presenceDb = Clamp(db, -8, +8); UpdateEqCoeffs(); }
        public void SetPresenceHz(int hz) { _presenceHz = Clamp(hz, 1000, 3000); UpdateEqCoeffs(); }

        // Expander
        public void SetAmbientReductionDb(int db) { if (db < 0) db = 0; if (db > 24) db = 24; _ambientReductionDb = db; }
        public void SetAmbientReleaseMs(int ms) { _ambReleaseMs = Clamp(ms, 50, 300); }
        public void SetAmbientAttackMs(int ms) { _ambAttackMs = Clamp(ms, 50, 400); }

        // Hum remover
        public void SetHumEnabled(bool on) { _humEnabled = on; UpdateEqCoeffs(); }
        public void SetHumBaseHz(int hz) { _humBaseHz = (hz < 55) ? 50 : 60; UpdateEqCoeffs(); }

        // De-esser
        public void SetDeEsserEnabled(bool on) { _deEsserEnabled = on; UpdateEqCoeffs(); }
        public void SetDeEsserMaxDb(int db) { _deEsserMaxDb = Clamp(db, 0, 8); }

        public void SetMetersCallback(Action<float, float, float> cb) => _metersCb = cb;

        private static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi) ? hi : v;

        public bool IsRunning => _run;

        public bool Start()
        {
            if (_run) return true;

            int sr = SelectSampleRate(SampleRate);
            int bytesPerSample = 2;
            _samplesPerFrame = Math.Max(64, (sr / 1000) * FrameMs);
            _bytesPerFrame = _samplesPerFrame * bytesPerSample;

            int recMin = AudioRecord.GetMinBufferSize(sr, ChannelIn.Mono, Encoding.Pcm16bit);
            if (recMin <= 0) { _log.Log("MinBuf(rec) invalide: " + recMin); return false; }
            int recBuf = AlignFrames(Math.Max(recMin, _bytesPerFrame * 6), bytesPerSample);

            int outMin = AudioTrack.GetMinBufferSize(sr, ChannelOut.Mono, Encoding.Pcm16bit);
            if (outMin <= 0) { _log.Log("MinBuf(track) invalide: " + outMin); return false; }
            int outBuf = AlignFrames(Math.Max(outMin, _bytesPerFrame * 6), bytesPerSample);

            try
            {
                _rec = new AudioRecord(AudioSource.VoiceCommunication, sr, ChannelIn.Mono, Encoding.Pcm16bit, recBuf);
                if (_rec.State != Android.Media.State.Initialized) { _log.Log("AudioRecord non init"); ReleaseRecord(); return false; }

                _track = new AudioTrack.Builder()
                    .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media).SetContentType(AudioContentType.Speech).Build())
                    .SetAudioFormat(new AudioFormat.Builder().SetEncoding(Encoding.Pcm16bit).SetSampleRate(sr).SetChannelMask(ChannelOut.Mono).Build())
                    .SetBufferSizeInBytes(outBuf)
                    .Build();
                if (_track.State != AudioTrackState.Initialized) { _log.Log("AudioTrack non init"); ReleaseTrack(); ReleaseRecord(); return false; }

                ReleasePlatformFx();
                if (PlatformNS_AGC_AEC_Enabled)
                {
                    try
                    {
                        int sess = _rec.AudioSessionId;
                        if (NoiseSuppressor.IsAvailable) _ns = NoiseSuppressor.Create(sess);
                        if (AutomaticGainControl.IsAvailable) _agc = AutomaticGainControl.Create(sess);
                        if (AcousticEchoCanceler.IsAvailable) _aec = AcousticEchoCanceler.Create(sess);
                    }
                    catch { }
                }

                InitFftState(_samplesPerFrame);
                _haveNoiseProfile = false;
                _noiseMag = new float[_fftN / 2];
                _magPrevMin = new float[_fftN / 2];

                _currentAmbGain = 1.0f;
                _hangFramesLeft = 0;
                _noiseRmsRef = 90;

                UpdateEqCoeffs();

                _loopExited.Reset();
                _run = true;

                _recBufBytes = recBuf;
                _trackBufBytes = outBuf;

                _framesWritten = 0;
                _framesRead = 0;

                _ioThread = new Thread(IOThreadMain) { IsBackground = true, Name = "B3P-AudioIO" };
                _ioThread.Start();

                _log.Log($"RUN ▶ SR={sr}Hz  frame={FrameMs}ms  fftN={_fftN}  recBuf={recBuf}  outBuf={outBuf}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Log("Start EX: " + ex);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            if (!_run) return;
            try
            {
                _run = false;
                _loopExited.Wait(1000);
                try { _ioThread?.Join(1000); } catch { }
            }
            finally
            {
                ReleasePlatformFx();
                ReleaseTrack();
                ReleaseRecord();
                _ioThread = null;
            }
            _log.Log("STOP ◼");
        }

        public void CalibrateNoiseNow(int millis = 500)
        {
            int frames = Math.Max(3, millis / Math.Max(1, FrameMs));
            Interlocked.Exchange(ref _pendingCalibFrames, frames);
            _log.Log("Recalibrage demandé (" + frames + " frames) — restez silencieux");
        }
        private volatile int _pendingCalibFrames = 0;

        // ===== Latence (décomposée) =====
        public int TransportLatencyMs
        {
            get
            {
                int fs = CurrentSampleRate();
                if (fs <= 0) fs = 48000;

                // backlog playback (frames encore dans la file AudioTrack)
                long playHead = 0;
                try { playHead = _track?.PlaybackHeadPosition ?? 0; } catch { }
                long pendingOut = Math.Max(0, _framesWritten - playHead);

                // input: pas d'API head position côté AudioRecord; approx = buffer + 1 frame
                int recFrames = (_recBufBytes / 2); // bytes -> samples
                int recMs = (int)Math.Round(1000.0 * recFrames / Math.Max(1, fs));

                // sortie en ms
                int outMs = (int)Math.Round(1000.0 * pendingOut / Math.Max(1, fs));

                // pipeline (in->process->out): ~2 frames
                int pipeMs = 2 * FrameMs;

                return outMs + recMs + pipeMs;
            }
        }

        public int AlgoLatencyMs
        {
            get
            {
                int fs = Math.Max(8000, CurrentSampleRate());
                int s = 0;
                if (DspNoiseSuppressEnabled && _fftN > 0) s += _fftN / 2;
                return (int)Math.Round(1000.0 * s / fs);
            }
        }

        public int EstimatedLatencyMs => TransportLatencyMs + AlgoLatencyMs;

        // ==================== Thread I/O ====================
        private void IOThreadMain()
        {
            Android.OS.Process.SetThreadPriority(Android.OS.ThreadPriority.Audio);

            try
            {
                _rec.StartRecording();
                if (_rec.RecordingState != RecordState.Recording) { _log.Log("Record KO"); return; }

                _track.Play();
                if (_track.PlayState != PlayState.Playing) { _log.Log("Play KO"); return; }
                try { _track.SetVolume(1.0f); } catch { }

                CalibrateNoiseFramesInternal(Math.Max(3, 500 / Math.Max(1, FrameMs)));

                var inBuf = new byte[_bytesPerFrame];
                var sFrame = new short[_samplesPerFrame];
                var outS = new short[_samplesPerFrame];
                var outBuf = new byte[_bytesPerFrame];

                while (_run)
                {
                    int rq = Interlocked.Exchange(ref _pendingCalibFrames, 0);
                    if (rq > 0) CalibrateNoiseFramesInternal(rq);

                    int n = _rec.Read(inBuf, 0, inBuf.Length);
                    if (n <= 0) { if (n == (int)TrackStatus.ErrorInvalidOperation) break; continue; }
                    Buffer.BlockCopy(inBuf, 0, sFrame, 0, n);
                    _framesRead += _samplesPerFrame;

                    if (HighPassEnabled)
                    {
                        for (int i = 0; i < _samplesPerFrame; i++)
                        {
                            short x = sFrame[i];
                            float y = _hpAlpha * (_hpYprev + x - _hpXprev);
                            _hpXprev = x; _hpYprev = y;
                            int clamped = (int)y;
                            if (clamped > short.MaxValue) clamped = short.MaxValue;
                            if (clamped < short.MinValue) clamped = short.MinValue;
                            sFrame[i] = (short)clamped;
                        }
                    }

                    if (_humEnabled)
                    {
                        for (int i = 0; i < _samplesPerFrame; i++)
                        {
                            float v = sFrame[i];
                            v = _hum1.Process(v);
                            v = _hum2.Process(v);
                            v = _hum3.Process(v);
                            if (v > short.MaxValue) v = short.MaxValue;
                            if (v < short.MinValue) v = short.MinValue;
                            sFrame[i] = (short)v;
                        }
                    }

                    if (EqEnabled && (_bassDb != 0 || _trebleDb != 0))
                    {
                        for (int i = 0; i < _samplesPerFrame; i++)
                        {
                            float v = sFrame[i];
                            if (_bassDb != 0) v = _ls.Process(v);
                            if (_trebleDb != 0) v = _hs.Process(v);
                            if (v > short.MaxValue) v = short.MaxValue;
                            if (v < short.MinValue) v = short.MinValue;
                            sFrame[i] = (short)v;
                        }
                    }
                    if (_presenceEnabled && _presenceDb != 0)
                    {
                        for (int i = 0; i < _samplesPerFrame; i++)
                        {
                            float v = sFrame[i];
                            v = _presence.Process(v);
                            if (v > short.MaxValue) v = short.MaxValue;
                            if (v < short.MinValue) v = short.MinValue;
                            sFrame[i] = (short)v;
                        }
                    }

                    // VAD / énergie
                    bool isVoice = true;
                    double sum = 0; int zc = 0; short prev = 0;
                    for (int i = 0; i < _samplesPerFrame; i++)
                    {
                        short v = sFrame[i];
                        sum += v * (double)v;
                        if ((v ^ prev) < 0) zc++;
                        prev = v;
                    }
                    double rms = Math.Sqrt(sum / _samplesPerFrame);
                    double thr = Math.Max(100, _noiseRmsRef * 1.8);
                    isVoice = (rms > thr) && (zc > _samplesPerFrame * 0.02 && zc < _samplesPerFrame * 0.30);

                    // De-esser
                    if (_deEsserEnabled)
                    {
                        double sumHF = 0;
                        for (int i = 0; i < _samplesPerFrame; i++)
                        {
                            float x = sFrame[i];
                            float y = _hpDeessAlpha * (_hpDeessY + x - _hpDeessXprev);
                            _hpDeessXprev = x; _hpDeessY = y;
                            sumHF += y * (double)y;
                        }
                        double rmsHF = Math.Sqrt(sumHF / _samplesPerFrame);
                        double diffDb = 20.0 * Math.Log10((rmsHF + 1e-6) / Math.Max(1e-6, rms));
                        double want = diffDb - 6.0;
                        int atten = (want > 0) ? (int)Math.Min(_deEsserMaxDb, Math.Round(want)) : 0;

                        int fs = CurrentSampleRate();
                        _deessShelf.MakeHighShelf(fs, 6500, -atten);
                        if (atten > 0)
                        {
                            for (int i = 0; i < _samplesPerFrame; i++)
                            {
                                float v = _deessShelf.Process(sFrame[i]);
                                if (v > short.MaxValue) v = short.MaxValue;
                                if (v < short.MinValue) v = short.MinValue;
                                sFrame[i] = (short)v;
                            }
                        }
                    }

                    if (DspNoiseSuppressEnabled && _haveNoiseProfile)
                        SpectralDenoiseInplace(sFrame);

                    if (AmbientExpanderEnabled)
                    {
                        float ambCutLin = (float)Math.Pow(10.0, -_ambientReductionDb / 20.0f);

                        float targetAmbGain = 1.0f;
                        if (isVoice) { _hangFramesLeft = Math.Max(_hangFramesLeft, AMB_HANG_MS / Math.Max(1, FrameMs)); targetAmbGain = 1.0f; }
                        else if (_hangFramesLeft > 0) { _hangFramesLeft--; targetAmbGain = 1.0f; }
                        else { targetAmbGain = ambCutLin; }

                        float alphaAttack = FrameAlpha(_ambAttackMs);
                        float alphaRelease = FrameAlpha(_ambReleaseMs);
                        float a = (targetAmbGain < _currentAmbGain) ? alphaAttack : alphaRelease;
                        _currentAmbGain += a * (targetAmbGain - _currentAmbGain);
                        ApplyGainInPlace(sFrame, _currentAmbGain);

                        if (!isVoice) _noiseRmsRef = 0.98 * _noiseRmsRef + 0.02 * rms;
                    }

                    if (ClarityEnabled)
                    {
                        for (int i = 0; i < _samplesPerFrame; i++)
                        {
                            int v = (int)(sFrame[i] * 1.05f);
                            if (v > short.MaxValue) v = short.MaxValue;
                            else if (v < short.MinValue) v = short.MinValue;
                            sFrame[i] = (short)v;
                        }
                    }

                    // Gain + compresseur/limiteur
                    float limitGR;
                    ApplyGainAndDynamics(sFrame, outS, out limitGR);
                    if (!PassThroughEnabled) Array.Clear(outS, 0, _samplesPerFrame);

                    // Meters
                    if (_metersCb != null)
                    {
                        double sum2 = 0; int pk = 0;
                        for (int i = 0; i < _samplesPerFrame; i++)
                        {
                            int v = outS[i]; int av = v >= 0 ? v : -v;
                            if (av > pk) pk = av;
                            sum2 += v * (double)v;
                        }
                        double rmsOut = Math.Sqrt(sum2 / _samplesPerFrame) / short.MaxValue;
                        double pkOut = pk / (double)short.MaxValue;

                        float rmsDb = (float)(20.0 * Math.Log10(Math.Max(1e-8, rmsOut)));
                        float pkDb = (float)(20.0 * Math.Log10(Math.Max(1e-8, pkOut)));
                        try { _metersCb(pkDb, rmsDb, limitGR); } catch { }
                    }

                    Buffer.BlockCopy(outS, 0, outBuf, 0, _bytesPerFrame);
                    int wrote = _track.Write(outBuf, 0, _bytesPerFrame);
                    _framesWritten += _samplesPerFrame;
                    if (wrote < 0) _log.Log("Track.Write err: " + wrote);

                    // Adaptation bruit lente
                    if (DspNoiseSuppressEnabled && !isVoice)
                    {
                        float[] mag = new float[_fftN / 2];
                        SpectralMag(sFrame, mag);
                        for (int k = 0; k < mag.Length; k++)
                        {
                            _magPrevMin[k] = Math.Min(_magPrevMin[k] * NR_ADAPT_DECAY + 1e-3f, mag[k]);
                            _noiseMag[k] = 0.98f * _noiseMag[k] + 0.02f * _magPrevMin[k];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Log("IOThread EX: " + ex.Message);
            }
            finally
            {
                try { if (_track?.PlayState == PlayState.Playing) _track.Stop(); } catch { }
                try { if (_rec?.RecordingState == RecordState.Recording) _rec.Stop(); } catch { }
                _loopExited.Set();
            }
        }

        private float FrameAlpha(int tcMs)
        {
            double tc = Math.Max(1.0, tcMs); // Intentional bug fixed below will be corrected later if needed
            double dt = Math.Max(1.0, FrameMs);
            double a = 1.0 - Math.Exp(-dt / tc);
            return (float)a;
        }
        private static void ApplyGainInPlace(short[] buf, float g)
        {
            if (Math.Abs(g - 1.0f) < 1e-4f) return;
            for (int i = 0; i < buf.Length; i++)
            {
                float v = buf[i] * g;
                if (v > short.MaxValue) v = short.MaxValue;
                if (v < short.MinValue) v = short.MinValue;
                buf[i] = (short)v;
            }
        }

        private void CalibrateNoiseFramesInternal(int frames)
        {
            if (frames <= 0) return;
            var inBuf = new byte[_bytesPerFrame];
            var s = new short[_samplesPerFrame];
            var mag = new float[_fftN / 2];

            _haveNoiseProfile = false;
            double rmsAvg = 0;

            int got = 0;
            while (_run && got < frames)
            {
                int n = _rec.Read(inBuf, 0, inBuf.Length);
                if (n <= 0) continue;
                Buffer.BlockCopy(inBuf, 0, s, 0, n);

                double sum = 0;
                for (int i = 0; i < _samplesPerFrame; i++) { double v = s[i]; sum += v * v; }
                double rms = Math.Sqrt(sum / _samplesPerFrame);
                rmsAvg = (got == 0) ? rms : 0.9 * rmsAvg + 0.1 * rms;

                if (DspNoiseSuppressEnabled)
                {
                    SpectralMag(s, mag);
                    for (int k = 0; k < mag.Length; k++)
                        _noiseMag[k] = (got == 0) ? mag[k] : (0.9f * _noiseMag[k] + 0.1f * mag[k]);
                }
                got++;
            }
            if (DspNoiseSuppressEnabled)
            {
                Array.Copy(_noiseMag, _magPrevMin, _noiseMag.Length);
                _haveNoiseProfile = true;
            }
            _noiseRmsRef = Math.Max(60, rmsAvg);
            _log.Log($"NR: profil bruit calibré ({got} frames)");
        }

        private void ApplyGainAndDynamics(short[] input, short[] output, out float limitGrDb)
        {
            float ceil = LIMIT_CEIL * short.MaxValue;
            float grAccum = 0f;

            for (int i = 0; i < _samplesPerFrame; i++)
            {
                float s = input[i] * _preGainLinear;

                double norm = Math.Max(1e-8, Math.Abs((double)s) / short.MaxValue);
                float db = 20f * (float)Math.Log10(norm);

                float over = db - COMP_THRESHOLD_DB;
                if (over > -COMP_KNEE_DB)
                {
                    float knee = (over + COMP_KNEE_DB) / (2f * COMP_KNEE_DB);
                    if (knee < 0f) knee = 0f; else if (knee > 1f) knee = 1f;

                    float gainDb = -(1f - 1f / COMP_RATIO) * over * knee;
                    float lin = (float)Math.Pow(10.0, gainDb / 20.0);
                    s *= lin;
                    grAccum += Math.Abs(gainDb);
                }

                if (s > ceil) { grAccum += 0.5f; s = ceil; }
                if (s < -ceil) { grAccum += 0.5f; s = -ceil; }

                output[i] = (short)s;
            }
            limitGrDb = grAccum / Math.Max(1, _samplesPerFrame);
        }

        // ===== Spectral NR ===== (same as previous implementation)
        private void SpectralDenoiseInplace(short[] samples)
        {
            if (_fftRe == null) return;

            for (int i = 0; i < _fftN; i++)
            {
                float v = (i < samples.Length) ? samples[i] : 0f;
                _fftRe[i] = v * _win[i];
                _fftIm[i] = 0f;
            }
            FFT(_fftRe, _fftIm, _fftBits, false);

            int bins = _fftN / 2;
            for (int k = 0; k < bins; k++)
            {
                float re = _fftRe[k], im = _fftIm[k];
                float mag = (float)Math.Sqrt(re * re + im * im) + 1e-9f;
                float noise = _noiseMag[k];
                float clean = Math.Max(mag - NR_ALPHA * noise, NR_FLOOR * mag);
                float g = clean / mag;

                _fftRe[k] *= g; _fftIm[k] *= g;
                if (k != 0) { int k2 = _fftN - k; _fftRe[k2] *= g; _fftIm[k2] *= g; }
            }

            FFT(_fftRe, _fftIm, _fftBits, true);

            for (int i = 0; i < samples.Length; i++)
            {
                float y = _fftRe[i] * _win[i];
                if (y > short.MaxValue) y = short.MaxValue;
                if (y < short.MinValue) y = short.MinValue;
                samples[i] = (short)y;
            }
        }

        private void SpectralMag(short[] samples, float[] magOut)
        {
            for (int i = 0; i < _fftN; i++)
            {
                float v = (i < samples.Length) ? samples[i] : 0f;
                _fftRe[i] = v * _win[i]; _fftIm[i] = 0f;
            }
            FFT(_fftRe, _fftIm, _fftBits, false);
            int bins = magOut.Length;
            for (int k = 0; k < bins; k++)
            {
                float re = _fftRe[k], im = _fftIm[k];
                magOut[k] = (float)Math.Sqrt(re * re + im * im) + 1e-6f;
            }
        }

        private void InitFftState(int minN)
        {
            int n = 1, bits = 0; while (n < minN) { n <<= 1; bits++; }
            _fftN = n; _fftBits = bits;
            _fftRe = new float[n]; _fftIm = new float[n]; _win = new float[n];
            for (int i = 0; i < n; i++) _win[i] = 0.5f * (1f - (float)Math.Cos(2 * Math.PI * i / (n - 1)));
        }

        private static void FFT(float[] re, float[] im, int bits, bool inverse)
        {
            int n = re.Length;
            for (int i = 0, j = 0; i < n; i++)
            {
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
                int m = n >> 1; while (j >= m && m >= 1) { j -= m; m >>= 1; }
                j += m;
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
                float wlr = (float)Math.Cos(ang), wli = (float)Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float wr = 1, wi = 0;
                    for (int j = 0; j < len / 2; j++)
                    {
                        int u = i + j, v = u + len / 2;
                        float vr = re[v] * wr - im[v] * wi;
                        float vi = re[v] * wi + im[v] * wr;
                        re[v] = re[u] - vr; im[v] = im[u] - vi;
                        re[u] += vr; im[u] += vi;
                        float nwr = wr * wlr - wi * wli;
                        wi = wr * wli + wi * wlr; wr = nwr;
                    }
                }
            }
            if (inverse) { for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; } }
        }

        private static int SelectSampleRate(int requested)
        {
            if (requested == 44100 || requested == 48000) return requested;
            int ok48 = AudioRecord.GetMinBufferSize(48000, ChannelIn.Mono, Encoding.Pcm16bit);
            return (ok48 > 0) ? 48000 : 44100;
        }

        private static int AlignFrames(int bytes, int bytesPerSample)
        {
            int frame = bytesPerSample * 1;
            int rem = bytes % frame;
            return rem == 0 ? bytes : bytes + (frame - rem);
        }

        private int CurrentSampleRate()
        {
            if (_rec != null && _rec.State == Android.Media.State.Initialized) return _rec.SampleRate;
            return SelectSampleRate(SampleRate);
        }

        private void RecomputeHpAlpha()
        {
            int fs = CurrentSampleRate();
            double rc = 1.0 / (2.0 * Math.PI * Math.Max(40, _hpCutHz));
            double dt = 1.0 / Math.Max(8000, fs);
            _hpAlpha = (float)(rc / (rc + dt));
        }

        private void RecomputeHpDeessAlpha()
        {
            int fs = CurrentSampleRate();
            double rc = 1.0 / (2.0 * Math.PI * DEESS_START_HZ);
            double dt = 1.0 / Math.Max(8000, fs);
            _hpDeessAlpha = (float)(rc / (rc + dt));
        }

        public void SetHighPassCutoffHz(int hz) { _hpCutHz = Clamp(hz, 40, 400); RecomputeHpAlpha(); }

        private void MakeHumNotches(int fs)
        {
            if (fs <= 0) return;
            int f1 = _humBaseHz;
            int f2 = f1 * 2;
            int f3 = f1 * 3;
            const float Q = 35f;
            _hum1.MakeNotch(fs, f1, Q);
            _hum2.MakeNotch(fs, f2, Q);
            _hum3.MakeNotch(fs, f3, Q);
        }

        private void ReleasePlatformFx()
        {
            try { _ns?.Release(); _ns?.Dispose(); } catch { } finally { _ns = null; }
            try { _agc?.Release(); _agc?.Dispose(); } catch { } finally { _agc = null; }
            try { _aec?.Release(); _aec?.Dispose(); } catch { } finally { _aec = null; }
        }

        private void ReleaseRecord() { try { _rec?.Release(); _rec?.Dispose(); } catch { } finally { _rec = null; } }
        private void ReleaseTrack() { try { _track?.Release(); _track?.Dispose(); } catch { } finally { _track = null; } }

        private sealed class NullSink : ILogSink { public void Log(string msg) { } }

        // ===== Biquad RBJ =====
        private sealed class Biquad
        {
            private float b0, b1, b2, a1, a2;
            private float z1, z2;

            public void Reset() { z1 = z2 = 0f; }

            public void MakeLowShelf(int fs, int fc, int gainDb, float S = 1.0f)
            {
                if (gainDb == 0) { b0 = 1; b1 = 0; b2 = 0; a1 = 0; a2 = 0; Reset(); return; }
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

                b0 = (float)(b0d / a0d); b1 = (float)(b1d / a0d); b2 = (float)(b2d / a0d);
                a1 = (float)(a1d / a0d); a2 = (float)(a2d / a0d);
                Reset();
            }

            public void MakeHighShelf(int fs, int fc, int gainDb, float S = 1.0f)
            {
                if (gainDb == 0) { b0 = 1; b1 = 0; b2 = 0; a1 = 0; a2 = 0; Reset(); return; }
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

                b0 = (float)(b0d / a0d); b1 = (float)(b1d / a0d); b2 = (float)(b2d / a0d);
                a1 = (float)(a1d / a0d); a2 = (float)(a2d / a0d);
                Reset();
            }

            public void MakePeaking(int fs, int fc, float q, int gainDb)
            {
                if (gainDb == 0) { b0 = 1; b1 = 0; b2 = 0; a1 = 0; a2 = 0; Reset(); return; }
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

                b0 = (float)(b0d / a0d); b1 = (float)(b1d / a0d); b2 = (float)(b2d / a0d);
                a1 = (float)(a1d / a0d); a2 = (float)(a2d / a0d);
                Reset();
            }

            public void MakeNotch(int fs, int fc, float q)
            {
                double w0 = 2.0 * Math.PI * fc / fs;
                double cosw = Math.Cos(w0), sinw = Math.Sin(w0);
                double alpha = sinw / (2.0 * q);

                double b0d = 1;
                double b1d = -2 * cosw;
                double b2d = 1;
                double a0d = 1 + alpha;
                double a1d = -2 * cosw;
                double a2d = 1 - alpha;

                b0 = (float)(b0d / a0d); b1 = (float)(b1d / a0d); b2 = (float)(b2d / a0d);
                a1 = (float)(a1d / a0d); a2 = (float)(a2d / a0d);
                Reset();
            }

            public float Process(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }
    }
}
