using Android.Content;
using Android.Media;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Buds3ProAideAuditiveIA.v2


{
    public enum AudioTransport { A2DP, SCO, LE_LC3_AUTO }

    public interface ILogSink { void Log(string msg); }

    // Nouvelles métriques de santé audio avec prédiction
    public struct AudioHealthMetrics
    {
        public int BufferUnderrunsCount;
        public int LatencyMs;
        public int CpuUsagePercent;
        public double AudioThreadCpuTime;
        public int MemoryPressureMB;
        public DateTime LastHealthCheck;
        public readonly bool IsStable => BufferUnderrunsCount < 5 && LatencyMs < 80 && CpuUsagePercent < 70;
        public readonly bool IsHealthy => BufferUnderrunsCount < 10 && LatencyMs < 100 && CpuUsagePercent < 80;

        // Nouvelles métriques prédictives
        public double StabilityScore; // 0.0 = instable, 1.0 = parfait
        public int PredictedDropoutRisk; // 0-100%
        public double PerformanceTrend; // -1.0 = dégradation, +1.0 = amélioration
    }

    // Watchdog amélioré avec prédiction de pannes
    public class AudioWatchdog
    {
        private DateTime _lastHeartbeat = DateTime.Now;
        private readonly object _lock = new object();
        private const int TimeoutMs = 3000; // Réduit à 3 secondes pour réactivité
        private int _missedHeartbeats = 0;
        private const int MaxMissedHeartbeats = 3;

        // Nouveaux: prédiction de panne
        private readonly Queue<TimeSpan> _recentIntervals = new Queue<TimeSpan>(10);
        private DateTime _previousHeartbeat = DateTime.Now;

        public void Heartbeat()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var interval = now - _previousHeartbeat;

                // Enregistrer l'intervalle pour analyse de tendance
                _recentIntervals.Enqueue(interval);
                if (_recentIntervals.Count > 10) _recentIntervals.Dequeue();

                _lastHeartbeat = now;
                _previousHeartbeat = now;
                _missedHeartbeats = 0;
            }
        }

        public bool IsAlive()
        {
            lock (_lock)
            {
                var timeSince = DateTime.Now - _lastHeartbeat;
                if (timeSince.TotalMilliseconds > TimeoutMs)
                {
                    _missedHeartbeats++;
                    return _missedHeartbeats < MaxMissedHeartbeats;
                }
                return true;
            }
        }

        public TimeSpan TimeSinceLastHeartbeat()
        {
            lock (_lock)
            {
                return DateTime.Now - _lastHeartbeat;
            }
        }

        // Nouveau: prédiction de risque de panne
        public double PredictFailureRisk()
        {
            lock (_lock)
            {
                if (_recentIntervals.Count < 3) return 0.0;

                var intervals = _recentIntervals.ToArray();
                double avgInterval = intervals.Sum(t => t.TotalMilliseconds) / intervals.Length;
                double variance = intervals.Sum(t => Math.Pow(t.TotalMilliseconds - avgInterval, 2)) / intervals.Length;

                // Plus la variance est élevée, plus le risque est grand
                double riskScore = Math.Min(1.0, variance / (50.0 * 50.0)); // 50ms = variance critique
                return riskScore;
            }
        }
    }

    public sealed class AudioEngine : Java.Lang.Object, IDisposable
    {
        // ===== infra
        private readonly Context _ctx;
        private readonly ILogSink _log;

        private AudioRecord _rec;
        private AudioTrack _trk;
        private Thread _worker;
        private volatile bool _running;
        private volatile bool _emergencyStop = false; // Nouveau: arrêt d'urgence

        // Watchdog et métriques améliorés
        private readonly AudioWatchdog _watchdog = new AudioWatchdog();
        private readonly Stopwatch _cpuTimer = new Stopwatch();
        private double _totalCpuTime = 0;
        private int _processedFrames = 0;
        private DateTime _lastMetricsUpdate = DateTime.Now;

        // Nouvelles métriques de performance
        private readonly Queue<double> _latencyHistory = new Queue<double>(100);
        private readonly Queue<int> _cpuHistory = new Queue<int>(100);
        private readonly double _performanceTrend = 0.0;
        private int _totalErrors = 0;
        private int _automaticRecoveries = 0;

        // ===== format / buffers
        private int _requestedHpfHz = 120;
        private int _fs = 48000;
        private int _frameMs = 10;
        private int _n;
        private int _recBufBytes;
        private ChannelOut _outChannel = ChannelOut.Mono;
        private bool _isStereoOut = false;
        private int _trkBufBytes;

        // ===== modules améliorés
        private readonly AudioEffects_EnableNative _platFx = new AudioEffects_EnableNative();
        private DspChain _dsp;
        private SpectralNoiseReducer _snr;
        private Equalizer3Band _eq;
        private readonly TiltEqClarity _clarity = new TiltEqClarity();

        // ===== flags avec nouveaux contrôles de sécurité
        private volatile bool _passThrough = true;
        private volatile bool _useSnr = true;
        private volatile bool _usePlatformFx = false;
        private volatile bool _useHpf = true;
        private volatile bool _useClarity = true;
        private volatile bool _useEq = false;
        private volatile bool _safetyLimiterEnabled = true; // Nouveau: limiteur de sécurité

        // transport
        private volatile AudioTransport _transport = AudioTransport.A2DP;

        // EQ/presence
        private volatile bool _presenceOn = true;

        // Ambient / gate map -> DspChain NR strength
        private int _ambientDb = 12;
        private int _ambAtkMs = 200, _ambRelMs = 150;

        // De-esser & Hum
        private volatile bool _deEsserEnabled = false;
        private volatile bool _humEnabled = false;
        private volatile int _humBaseHz = 50;

        // gain global avec protection
        private int _gainDb = 0;
        private double _gainMultiplier = 1.0;
        private const int MAX_SAFE_GAIN_DB = 24; // Limite de sécurité

        // calib NR spectral
        private volatile int _calibFrames = 0;

        // meters callback
        private Action<float, float, float> _meters;

        // latences
        public int TransportLatencyMs { get; private set; }
        public int AlgoLatencyMs => _frameMs;
        public int EstimatedLatencyMs => TransportLatencyMs + AlgoLatencyMs;

        public bool IsRunning => _running && !_emergencyStop;

        // Protection auditive améliorée
        private const float MAX_SAFE_LEVEL_DB = -6.0f;
        private const float EMERGENCY_LEVEL_DB = -3.0f; // Nouveau: niveau d'urgence
        private const float FADE_TIME_MS = 50.0f;
        private float _fadeGain = 0.0f;
        private bool _safeMode = false;
        private int _dangerousLevelCount = 0;
        private const int MAX_DANGEROUS_LEVELS = 5;

        // Auto-recovery amélioré
        private int _consecutiveErrors = 0;
        private const int MAX_CONSECUTIVE_ERRORS = 3; // Réduit pour plus de réactivité
        private DateTime _lastAutoRestart = DateTime.MinValue;
        private const int AUTO_RESTART_COOLDOWN_MS = 15000; // Réduit à 15 secondes
        private int _autoRestartCount = 0;
        private const int MAX_AUTO_RESTARTS = 3;

        // Disposition flag
        private bool _disposed = false;

        // Nouveau: Emergency Stop
        public void EmergencyStop()
        {
            try
            {
                _emergencyStop = true;
                _running = false;

                // Fade out rapide
                if (_trk != null)
                {
                    _trk.Pause();
                    _trk.Flush();
                }

                SafeLog("EMERGENCY STOP activated");
                LogUtilities.Log(_ctx, "EMERGENCY", "Emergency stop activated by user");
            }
            catch (Exception ex)
            {
                SafeLog($"Error in emergency stop: {ex.Message}");
            }
        }

        // Nouveau: Reset Emergency
        public void ResetEmergency()
        {
            _emergencyStop = false;
            _safeMode = false;
            _dangerousLevelCount = 0;
            _fadeGain = 0.0f;
            SafeLog("Emergency mode reset");
        }

        public AudioHealthMetrics GetHealthMetrics()
        {
            var metrics = new AudioHealthMetrics();

            try
            {
                metrics.BufferUnderrunsCount = _trk?.UnderrunCount ?? -1;
                metrics.LatencyMs = EstimatedLatencyMs;

                // Calcul CPU usage amélioré
                if (_processedFrames > 0)
                {
                    double avgCpuTimePerFrame = _totalCpuTime / _processedFrames;
                    double frameInterval = _frameMs;
                    metrics.CpuUsagePercent = (int)Math.Min(100, (avgCpuTimePerFrame / frameInterval) * 100);

                    // Enregistrer pour analyse de tendance
                    _cpuHistory.Enqueue(metrics.CpuUsagePercent);
                    if (_cpuHistory.Count > 100) _cpuHistory.Dequeue();
                }
                else
                {
                    metrics.CpuUsagePercent = -1;
                }

                metrics.AudioThreadCpuTime = _totalCpuTime;

                // Estimation memory pressure améliorée
                long totalMemory = Java.Lang.Runtime.GetRuntime().TotalMemory();
                long freeMemory = Java.Lang.Runtime.GetRuntime().FreeMemory();
                metrics.MemoryPressureMB = (int)((totalMemory - freeMemory) / (1024 * 1024));

                metrics.LastHealthCheck = DateTime.Now;

                // Nouvelles métriques prédictives
                metrics.StabilityScore = CalculateStabilityScore();
                metrics.PredictedDropoutRisk = (int)(_watchdog.PredictFailureRisk() * 100);
                metrics.PerformanceTrend = CalculatePerformanceTrend();

                // Enregistrer latence pour tendance
                _latencyHistory.Enqueue(metrics.LatencyMs);
                if (_latencyHistory.Count > 100) _latencyHistory.Dequeue();

            }
            catch (Exception ex)
            {
                SafeLog($"Error getting health metrics: {ex.Message}");
                metrics.CpuUsagePercent = -1;
                metrics.LatencyMs = -1;
                metrics.BufferUnderrunsCount = -1;
            }

            return metrics;
        }

        // Nouveau: Calcul de score de stabilité
        private double CalculateStabilityScore()
        {
            double score = 1.0;

            // Pénalités pour problèmes
            if (_consecutiveErrors > 0) score -= _consecutiveErrors * 0.2;
            if (_autoRestartCount > 0) score -= _autoRestartCount * 0.1;
            if (_dangerousLevelCount > 0) score -= _dangerousLevelCount * 0.15;
            if (_totalErrors > 10) score -= 0.3;

            // Bonus pour stabilité
            if (_processedFrames > 48000) score += 0.1; // Plus de 10 secondes stables
            if (_watchdog.IsAlive()) score += 0.1;

            return Math.Max(0.0, Math.Min(1.0, score));
        }

        // Nouveau: Calcul de tendance de performance
        private double CalculatePerformanceTrend()
        {
            if (_cpuHistory.Count < 10) return 0.0;

            var cpuArray = _cpuHistory.ToArray();
            var recent = cpuArray.Length >= 5 ? cpuArray.Skip(Math.Max(0, cpuArray.Length - 5)).Average() : 0;
            var older = cpuArray.Length >= 5 ? cpuArray.Take(5).Average() : 0;

            // Tendance: -1.0 = dégradation, +1.0 = amélioration
            double diff = older - recent; // Si recent < older, c'est une amélioration
            return Math.Max(-1.0, Math.Min(1.0, diff / 50.0)); // Normaliser sur 50% CPU
        }

        public enum AudioPreset { Default, Restaurant, Office, Outdoor, Speech, Music, Gaming }

        public void ApplyPreset(AudioPreset preset)
        {
            try
            {
                switch (preset)
                {
                    case AudioPreset.Restaurant:
                        SetAmbientReductionDb(18);
                        SetPresenceDb(6);
                        SetEqEnabled(true);
                        SetBassDb(-3);
                        SetTrebleDb(2);
                        break;
                    case AudioPreset.Office:
                        SetAmbientReductionDb(8);
                        SetPresenceDb(2);
                        SetEqEnabled(false);
                        break;
                    case AudioPreset.Outdoor:
                        SetAmbientReductionDb(22);
                        SetPresenceDb(4);
                        SetEqEnabled(true);
                        SetBassDb(-2);
                        SetTrebleDb(3);
                        break;
                    case AudioPreset.Speech:
                        SetAmbientReductionDb(15);
                        SetPresenceDb(8);
                        SetEqEnabled(true);
                        SetBassDb(-6);
                        SetPresenceDb(6);
                        SetTrebleDb(0);
                        break;
                    case AudioPreset.Music:
                        SetAmbientReductionDb(5);
                        SetPresenceDb(0);
                        SetEqEnabled(true);
                        SetBassDb(3);
                        SetTrebleDb(2);
                        break;
                    case AudioPreset.Gaming:
                        SetAmbientReductionDb(12);
                        SetPresenceDb(4);
                        SetEqEnabled(true);
                        SetBassDb(0);
                        SetTrebleDb(4);
                        break;
                    case AudioPreset.Default:
                    default:
                        SetAmbientReductionDb(12);
                        SetPresenceDb(0);
                        SetEqEnabled(false);
                        break;
                }
                SafeLog($"Applied preset: {preset}");
            }
            catch (Exception ex)
            {
                SafeLog($"Error applying preset {preset}: {ex.Message}");
            }
        }

        public AudioEngine(Context ctx, ILogSink log = null)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _log = log ?? new NullLog();
        }

        public void SetTransport(AudioTransport t)
        {
            try
            {
                _transport = t;
                SafeLog($"[Route] Transport set to {t}");
                LogUtilities.Log(_ctx, "ROUTE", $"Transport set to {t}");
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting transport: {ex.Message}");
            }
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
            if (_running)
            {
                SafeLog("Cannot configure while running");
                return;
            }

            try
            {
                // Validation des paramètres avec limites de sécurité
                _fs = ValidateSampleRate(sampleRate);
                _frameMs = ValidateFrameMs(frameMs);
                _n = _fs * _frameMs / 1000;

                _passThrough = pass;
                _useSnr = dspNs;
                _usePlatformFx = platformFx;
                _useHpf = hp;
                _useClarity = clarity;
                _useEq = eq;

                // DSP Chain avec gestion d'erreur améliorée
                try
                {
                    _dsp?.Dispose();
                    _dsp = new DspChain(_fs);
                    _dsp.SetNrStrength(DbToNrStrength(_ambientDb));
                    _dsp.SetVoiceBoostDb(0);
                }
                catch (Exception ex)
                {
                    SafeLog($"Error initializing DSP chain: {ex.Message}");
                    _totalErrors++;
                    throw;
                }

                // EQ 3 bandes avec validation
                try
                {
                    _eq?.Dispose();
                    _eq = new Equalizer3Band(_fs);
                    _eq.SetEnabled(_useEq);
                }
                catch (Exception ex)
                {
                    SafeLog($"Error initializing EQ: {ex.Message}");
                    _totalErrors++;
                    // EQ non-critique, on continue
                }

                // SNR spectral avec optimisations
                try
                {
                    _snr?.Dispose();
                    int N = NextPow2(_n);
                    N = Math.Max(256, Math.Min(N, 2048)); // Limites de sécurité
                    _snr = new SpectralNoiseReducer(N, _fs);
                }
                catch (Exception ex)
                {
                    SafeLog($"Error initializing spectral noise reducer: {ex.Message}");
                    _useSnr = false;
                    _totalErrors++;
                }

                SafeLog($"Configuration successful: fs={_fs}, frame={_frameMs}ms, n={_n}");
            }
            catch (Exception ex)
            {
                SafeLog($"Configuration failed: {ex.Message}");
                _totalErrors++;
                throw;
            }
        }

        // Validation des paramètres avec ranges sûrs améliorés
        private int ValidateSampleRate(int sr)
        {
            if (sr < 8000) return 8000;
            if (sr > 96000) return 48000;
            // Préférer des sample rates standards
            if (sr <= 16000) return 16000;
            if (sr <= 44100) return 44100;
            return 48000;
        }

        private int ValidateFrameMs(int frameMs)
        {
            if (frameMs < 5) return 5;
            if (frameMs > 20) return 20;
            return frameMs;
        }

        // ================== flags & réglages (UI) avec validation ==================
        public void SetFlags(bool? pass = null, bool? dspNs = null, bool? platformFx = null,
                             bool? hp = null, bool? clarity = null, bool? ambientExpander = null)
        {
            try
            {
                if (pass.HasValue) _passThrough = pass.Value;
                if (dspNs.HasValue) _useSnr = dspNs.Value;
                if (platformFx.HasValue) _usePlatformFx = platformFx.Value;
                if (hp.HasValue) _useHpf = hp.Value;
                if (clarity.HasValue) _useClarity = clarity.Value;
                if (ambientExpander.HasValue)
                    _dsp?.SetNrStrength(DbToNrStrength(_ambientDb));
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting flags: {ex.Message}");
                _totalErrors++;
            }
        }

        public void SetMetersCallback(Action<float, float, float> cb) => _meters = cb;

        public void CalibrateNoiseNow(int ms = 500)
        {
            try
            {
                ms = Math.Max(100, Math.Min(ms, 3000)); // Limites de sécurité
                _calibFrames = Math.Max(1, ms / Math.Max(1, _frameMs));
                SafeLog($"[SNR] calibrate ~{ms}ms ({_calibFrames} frames)");
            }
            catch (Exception ex)
            {
                SafeLog($"Error in calibration: {ex.Message}");
                _totalErrors++;
            }
        }

        // Safe parameter setters avec validation renforcée
        public void SetAmbientReductionDb(int db)
        {
            try
            {
                _ambientDb = Clamp(db, 0, 24);
                _dsp?.SetNrStrength(DbToNrStrength(_ambientDb));
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting ambient reduction: {ex.Message}");
                _totalErrors++;
            }
        }

        public void SetAmbientAttackMs(int ms)
        {
            try { _ambAtkMs = Clamp(ms, 50, 400); }
            catch (Exception ex) { SafeLog($"Error setting attack: {ex.Message}"); _totalErrors++; }
        }

        public void SetAmbientReleaseMs(int ms)
        {
            try { _ambRelMs = Clamp(ms, 50, 300); }
            catch (Exception ex) { SafeLog($"Error setting release: {ex.Message}"); _totalErrors++; }
        }

        public void SetGainDb(int db)
        {
            try
            {
                // Protection auditive: limiter le gain maximum
                int maxGain = _safetyLimiterEnabled ? MAX_SAFE_GAIN_DB : 36;
                _gainDb = Clamp(db, 0, maxGain);
                _gainMultiplier = Math.Pow(10.0, _gainDb / 20.0);

                if (db > maxGain)
                {
                    SafeLog($"Gain limited to {maxGain}dB for safety (requested: {db}dB)");
                }
            }
            catch (Exception ex) { SafeLog($"Error setting gain: {ex.Message}"); _totalErrors++; }
        }

        // Protection auditive intégrée dans les setters
        public void SetEqEnabled(bool on)
        {
            try { _useEq = on; _eq?.SetEnabled(on); }
            catch (Exception ex) { SafeLog($"Error setting EQ: {ex.Message}"); _totalErrors++; }
        }

        public void SetBassDb(int db)
        {
            try { _eq?.SetBassDb(Clamp(db, -12, 12)); }
            catch (Exception ex) { SafeLog($"Error setting bass: {ex.Message}"); _totalErrors++; }
        }

        public void SetBassFreqHz(int hz)
        {
            try { _eq?.SetBassFreqHz(Clamp(hz, 40, 400)); }
            catch (Exception ex) { SafeLog($"Error setting bass freq: {ex.Message}"); _totalErrors++; }
        }

        public void SetPresenceEnabled(bool on)
        {
            try { _presenceOn = on; _dsp?.SetVoiceBoostDb(on ? 4 : 0); }
            catch (Exception ex) { SafeLog($"Error setting presence: {ex.Message}"); _totalErrors++; }
        }

        public void SetPresenceDb(int db)
        {
            try
            {
                _presenceOn = db != 0;
                _dsp?.SetVoiceBoostDb(Clamp(db, -8, 8));
                _eq?.SetPresenceDb(Clamp(db, -8, 8));
            }
            catch (Exception ex) { SafeLog($"Error setting presence dB: {ex.Message}"); _totalErrors++; }
        }

        public void SetPresenceHz(int hz)
        {
            try { _eq?.SetPresenceHz(Clamp(hz, 1000, 3000)); }
            catch (Exception ex) { SafeLog($"Error setting presence freq: {ex.Message}"); _totalErrors++; }
        }

        public void SetTrebleDb(int db)
        {
            try { _eq?.SetTrebleDb(Clamp(db, -12, 12)); }
            catch (Exception ex) { SafeLog($"Error setting treble: {ex.Message}"); _totalErrors++; }
        }

        public void SetTrebleFreqHz(int hz)
        {
            try { _eq?.SetTrebleFreqHz(Clamp(hz, 2000, 10000)); }
            catch (Exception ex) { SafeLog($"Error setting treble freq: {ex.Message}"); _totalErrors++; }
        }

        public void SetClarity(bool on)
        {
            try { _useClarity = on; }
            catch (Exception ex) { SafeLog($"Error setting clarity: {ex.Message}"); _totalErrors++; }
        }

        public void SetHighPassCutoffHz(int hz)
        {
            try
            {
                _requestedHpfHz = Clamp(hz, 40, 400);
                _useHpf = true;
            }
            catch (Exception ex) { SafeLog($"Error setting HPF: {ex.Message}"); _totalErrors++; }
        }

        public void SetDeEsserEnabled(bool on)
        {
            try { _deEsserEnabled = on; }
            catch (Exception ex) { SafeLog($"Error setting de-esser: {ex.Message}"); _totalErrors++; }
        }

        public void SetHumEnabled(bool on)
        {
            try { _humEnabled = on; }
            catch (Exception ex) { SafeLog($"Error setting hum removal: {ex.Message}"); _totalErrors++; }
        }

        public void SetHumBaseHz(int hz)
        {
            try { _humBaseHz = (hz == 60) ? 60 : 50; }
            catch (Exception ex) { SafeLog($"Error setting hum freq: {ex.Message}"); _totalErrors++; }
        }

        // Nouveau: Contrôle du limiteur de sécurité
        public void SetSafetyLimiterEnabled(bool enabled)
        {
            _safetyLimiterEnabled = enabled;
            SafeLog($"Safety limiter {(enabled ? "enabled" : "disabled")}");

            // Réajuster le gain si nécessaire
            if (enabled && _gainDb > MAX_SAFE_GAIN_DB)
            {
                SetGainDb(MAX_SAFE_GAIN_DB);
            }
        }

        // ================== Start / Stop avec recovery amélioré ==================
        public bool Start()
        {
            if (_running && !_emergencyStop) return true;

            // Reset emergency si nécessaire
            if (_emergencyStop)
            {
                ResetEmergency();
            }

            try
            {
                // Vérification des conditions préalables
                if (_autoRestartCount >= MAX_AUTO_RESTARTS)
                {
                    SafeLog("Maximum auto-restarts reached, manual intervention required");
                    return false;
                }

                // Reset des métriques
                _consecutiveErrors = 0;
                _totalCpuTime = 0;
                _processedFrames = 0;
                _fadeGain = 0.0f;
                _safeMode = false;
                _dangerousLevelCount = 0;

                // Configure routing avec auto-fallback amélioré
                if (!ConfigureAudioRouting())
                {
                    SafeLog("Failed to configure audio routing");
                    return false;
                }

                // Initialize audio components avec validation renforcée
                if (!InitializeAudioRecord())
                {
                    SafeLog("Failed to initialize AudioRecord");
                    Stop();
                    return false;
                }

                if (!InitializeAudioTrack())
                {
                    SafeLog("Failed to initialize AudioTrack");
                    Stop();
                    return false;
                }

                // Apply platform effects avec retry
                try
                {
                    _platFx.AttachOrUpdate(_rec.AudioSessionId, _usePlatformFx, _usePlatformFx, _usePlatformFx);
                }
                catch (Exception ex)
                {
                    SafeLog($"Warning: Platform effects failed: {ex.Message}");
                    _totalErrors++;
                    // Non-critique, on continue
                }

                // Start audio streams avec vérification
                try
                {
                    _rec.StartRecording();
                    if (_rec.RecordingState != RecordState.Recording)
                    {
                        throw new Exception("AudioRecord failed to start recording");
                    }

                    _trk.Play();
                    if (_trk.PlayState != PlayState.Playing)
                    {
                        throw new Exception("AudioTrack failed to start playing");
                    }
                }
                catch (Exception ex)
                {
                    SafeLog($"Failed to start audio streams: {ex.Message}");
                    Stop();
                    return false;
                }

                TransportLatencyMs = (_trkBufBytes / 2) * 1000 / _fs;

                _running = true;
                _worker = new Thread(Loop) { IsBackground = true, Name = "AudioEngineLoop" };
                _worker.Start();

                SafeLog($"[Audio] RUN ▶ fs={_fs} frame={_n} samples recBuf={_recBufBytes} trkBuf={_trkBufBytes} transport={_transport}");

                try
                {
                    LogUtilities.LogLatency(_ctx, TransportLatencyMs, AlgoLatencyMs);
                    LogUtilities.LogRoute(_ctx, BluetoothRouting_Utilities.GetActiveRouteInfo(_ctx));
                }
                catch (Exception ex)
                {
                    SafeLog($"Warning: Logging failed: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                SafeLog($"[Audio] start failed: {ex.Message}");
                _totalErrors++;
                Stop();
                return false;
            }
        }

        // Méthodes d'initialisation séparées pour meilleure gestion d'erreurs
        private bool ConfigureAudioRouting()
        {
            try
            {
                switch (_transport)
                {
                    case AudioTransport.SCO:
                        _fs = 16000; // Force 16kHz pour SCO
                        _n = _fs * _frameMs / 1000;

                        BluetoothRouting_Utilities.EnterCommunicationMode(_ctx);
                        if (!BluetoothRouting_Utilities.EnsureSco(_ctx, 5000))
                        {
                            SafeLog("[Route] SCO start failed; falling back to A2DP");
                            _transport = AudioTransport.A2DP;
                            BluetoothRouting_Utilities.LeaveCommunicationMode(_ctx);
                            return true; // Continue avec A2DP
                        }
                        else
                        {
                            try { BluetoothRouting_Utilities.ForceCommunicationDeviceSco(_ctx); }
                            catch (Exception ex) { SafeLog($"Warning: SCO device forcing failed: {ex.Message}"); }
                        }
                        break;

                    case AudioTransport.LE_LC3_AUTO:
                    case AudioTransport.A2DP:
                    default:
                        BluetoothRouting_Utilities.LeaveCommunicationMode(_ctx);
                        break;
                }
                return true;
            }
            catch (Exception ex)
            {
                SafeLog($"Audio routing configuration failed: {ex.Message}");
                _totalErrors++;
                return false;
            }
        }

        private bool InitializeAudioRecord()
        {
            try
            {
                int minRec = AudioRecord.GetMinBufferSize(_fs, ChannelIn.Mono, Encoding.Pcm16bit);
                if (minRec <= 0)
                {
                    SafeLog("Invalid minimum buffer size for AudioRecord");
                    return false;
                }

                _recBufBytes = Math.Max(minRec, _n * 4);
                var recSource = (_transport == AudioTransport.SCO) ? AudioSource.VoiceCommunication : AudioSource.Mic;

                _rec = new AudioRecord(recSource, _fs, ChannelIn.Mono, Encoding.Pcm16bit, _recBufBytes);

                if (_rec.State != State.Initialized)
                {
                    SafeLog("[Audio] AudioRecord init failed");
                    return false;
                }

                // Bind to Bluetooth if needed
                try { BluetoothRouting_Utilities.BindRecordToBtSco(_rec, _ctx); }
                catch (Exception ex) { SafeLog($"Warning: BT binding failed: {ex.Message}"); }

                return true;
            }
            catch (Exception ex)
            {
                SafeLog($"AudioRecord initialization failed: {ex.Message}");
                _totalErrors++;
                return false;
            }
        }

        private bool InitializeAudioTrack()
        {
            try
            {
                _outChannel = (_transport == AudioTransport.A2DP) ? ChannelOut.Stereo : ChannelOut.Mono;
                _isStereoOut = _outChannel == ChannelOut.Stereo;

                int minTrk = AudioTrack.GetMinBufferSize(_fs, _outChannel, Encoding.Pcm16bit);
                if (minTrk <= 0)
                {
                    SafeLog("Invalid minimum buffer size for AudioTrack");
                    return false;
                }

                _trkBufBytes = Math.Max(minTrk, _n * 4);

                // Use modern AudioTrack builder for SCO with proper attributes
                if (_transport == AudioTransport.SCO && Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Lollipop)
                {
                    var attrs = new AudioAttributes.Builder()
                        .SetUsage(AudioUsageKind.VoiceCommunication)
                        .SetContentType(AudioContentType.Speech)
                        .Build();

                    var fmt = new AudioFormat.Builder()
                        .SetSampleRate(_fs)
                        .SetEncoding(Encoding.Pcm16bit)
                        .SetChannelMask(_outChannel)
                        .Build();

                    _trk = new AudioTrack.Builder()
                        .SetAudioAttributes(attrs)
                        .SetAudioFormat(fmt)
                        .SetBufferSizeInBytes(_trkBufBytes)
                        .SetTransferMode(AudioTrackMode.Stream)
                        .Build();
                }
                else
                {
                    // Fallback to legacy API
#pragma warning disable CS0618
                    var stream = (_transport == AudioTransport.SCO) ? Stream.VoiceCall : Stream.Music;
                    _trk = new AudioTrack(stream, _fs, _outChannel, Encoding.Pcm16bit, _trkBufBytes, AudioTrackMode.Stream);
#pragma warning restore CS0618
                }

                if (_trk.State != AudioTrackState.Initialized)
                {
                    SafeLog("[Audio] AudioTrack init failed");
                    return false;
                }

                // Bind to Bluetooth if needed
                try { BluetoothRouting_Utilities.BindTrackToBtSco(_trk, _ctx); }
                catch (Exception ex) { SafeLog($"Warning: BT track binding failed: {ex.Message}"); }

                return true;
            }
            catch (Exception ex)
            {
                SafeLog($"AudioTrack initialization failed: {ex.Message}");
                _totalErrors++;
                return false;
            }
        }

        public void Stop()
        {
            _running = false;

            try
            {
                if (_worker != null && _worker.IsAlive)
                {
                    if (!_worker.Join(400))
                    {
                        SafeLog("Warning: Audio thread did not stop gracefully");
                        try { _worker.Abort(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error joining worker thread: {ex.Message}");
            }

            // Stop audio streams safely
            try { _trk?.Pause(); _trk?.Flush(); }
            catch (Exception ex) { SafeLog($"Error stopping AudioTrack: {ex.Message}"); }

            try { _rec?.Stop(); }
            catch (Exception ex) { SafeLog($"Error stopping AudioRecord: {ex.Message}"); }

            // Release resources
            try { _trk?.Release(); }
            catch (Exception ex) { SafeLog($"Error releasing AudioTrack: {ex.Message}"); }
            _trk = null;

            try { _rec?.Release(); }
            catch (Exception ex) { SafeLog($"Error releasing AudioRecord: {ex.Message}"); }
            _rec = null;

            try { _platFx.Detach(); }
            catch (Exception ex) { SafeLog($"Error detaching platform effects: {ex.Message}"); }

            // Return to normal mode safely
            try
            {
                BluetoothRouting_Utilities.LeaveCommunicationMode(_ctx);
            }
            catch (Exception ex)
            {
                SafeLog($"Error leaving communication mode: {ex.Message}");
            }

            SafeLog("[Audio] STOP ◼");
            try { LogUtilities.Log(_ctx, "AUDIO", "STOP"); }
            catch (Exception ex) { SafeLog($"Error logging stop: {ex.Message}"); }
        }

        // ================== boucle DSP avec monitoring avancé ==================
        private void Loop()
        {
            try
            {
                Android.OS.Process.SetThreadPriority(Android.OS.ThreadPriority.UrgentAudio);
            }
            catch (Exception ex)
            {
                SafeLog($"Warning: Could not set thread priority: {ex.Message}");
            }

            var frame = new short[_n];
            var pad = new short[_snr?.FrameSamples ?? _n];
            int Nsnr = pad.Length;

            int consecutiveReadErrors = 0;
            const int MAX_READ_ERRORS = 5; // Réduit pour plus de réactivité

            while (_running && !_emergencyStop)
            {
                try
                {
                    _watchdog.Heartbeat(); // Signal que le thread est vivant
                    _cpuTimer.Restart();

                    // Vérification d'urgence
                    if (_emergencyStop)
                    {
                        SafeLog("Emergency stop detected in loop, breaking");
                        break;
                    }

                    // Lecture avec gestion d'erreur améliorée
                    int r;
                    try
                    {
                        r = _rec.Read(frame, 0, _n);
                        consecutiveReadErrors = 0; // Reset sur succès
                    }
                    catch (Exception ex)
                    {
                        consecutiveReadErrors++;
                        SafeLog($"AudioRecord read error #{consecutiveReadErrors}: {ex.Message}");

                        if (consecutiveReadErrors >= MAX_READ_ERRORS)
                        {
                            SafeLog("Too many consecutive read errors, requesting auto-restart");
                            RequestAutoRestart();
                            break;
                        }

                        Thread.Sleep(5); // Petit délai avant retry
                        continue;
                    }

                    if (r <= 0)
                    {
                        consecutiveReadErrors++;
                        if (consecutiveReadErrors >= MAX_READ_ERRORS)
                        {
                            RequestAutoRestart();
                            break;
                        }
                        continue;
                    }

                    // Validation du buffer avec détection de corruption
                    if (!ValidateAudioBuffer(frame, r))
                    {
                        SafeLog("Invalid audio buffer detected, skipping frame");
                        continue;
                    }

                    // Protection auditive renforcée - détection de niveaux dangereux
                    if (DetectUnsafeLevel(frame, r))
                    {
                        _dangerousLevelCount++;
                        if (!_safeMode)
                        {
                            _safeMode = true;
                            SafeLog($"SAFE MODE: Dangerous audio level detected ({_dangerousLevelCount} times)");
                        }

                        // Protection d'urgence
                        if (_dangerousLevelCount >= MAX_DANGEROUS_LEVELS)
                        {
                            SafeLog("EMERGENCY: Too many dangerous levels, activating emergency stop");
                            EmergencyStop();
                            break;
                        }
                    }

                    // Fade-in progressif pour éviter les chocs auditifs
                    ApplyFadeIn(frame, r);

                    if (_passThrough)
                    {
                        ApplyGain(frame, r, _gainDb);
                        WriteToOutput(frame, r);
                        PushMeters(frame, r, 0f);
                        UpdateMetrics();
                        continue;
                    }

                    // === Pipeline DSP avec gestion d'erreurs granulaire ===

                    // 1) SNR spectral (optionnel)
                    if (_useSnr && _snr != null)
                    {
                        try
                        {
                            Array.Clear(pad, 0, Nsnr);
                            Array.Copy(frame, 0, pad, 0, r);

                            if (_calibFrames > 0)
                            {
                                _snr.UpdateNoiseProfile(pad);
                                _calibFrames--;
                            }
                            else
                            {
                                _snr.Process(pad, adapt: false);
                            }

                            Array.Copy(pad, 0, frame, 0, r);
                        }
                        catch (Exception ex)
                        {
                            SafeLog($"SNR processing error: {ex.Message}");
                            _useSnr = false; // Désactiver temporairement
                            _totalErrors++;
                        }
                    }

                    // 2) DspChain (HPF + Présence + Comp + gate)
                    float grDb = 0f;
                    if (_dsp != null)
                    {
                        try
                        {
                            _dsp.Process(frame, r);
                            grDb = _dsp.LastGainReductionDb;
                        }
                        catch (Exception ex)
                        {
                            SafeLog($"DSP chain processing error: {ex.Message}");
                            _totalErrors++;
                            // Continue sans DSP si erreur
                        }
                    }

                    // 3) EQ 3 bandes (si activée)
                    if (_useEq && _eq != null)
                    {
                        try
                        {
                            _eq.ProcessBuffer(frame, r);
                        }
                        catch (Exception ex)
                        {
                            SafeLog($"EQ processing error: {ex.Message}");
                            _useEq = false; // Désactiver temporairement
                            _totalErrors++;
                        }
                    }

                    // 4) Clarity
                    if (_useClarity)
                    {
                        try
                        {
                            _clarity.ProcessBuffer(frame, r);
                        }
                        catch (Exception ex)
                        {
                            SafeLog($"Clarity processing error: {ex.Message}");
                            _useClarity = false; // Désactiver temporairement
                            _totalErrors++;
                        }
                    }

                    // 5) Gain final avec protection renforcée
                    ApplyGainWithProtection(frame, r, _gainDb);

                    // 6) Output avec gestion d'erreur et retry
                    if (!WriteToOutput(frame, r))
                    {
                        _consecutiveErrors++;
                        if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                        {
                            SafeLog("Too many output errors, attempting restart");
                            RequestAutoRestart();
                            break;
                        }
                    }
                    else
                    {
                        _consecutiveErrors = 0; // Reset sur succès
                    }

                    PushMeters(frame, r, grDb);
                    UpdateMetrics();
                }
                catch (Exception ex)
                {
                    SafeLog($"Critical error in audio loop: {ex.Message}");
                    _consecutiveErrors++;
                    _totalErrors++;

                    if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                    {
                        SafeLog("Too many consecutive errors in audio loop, requesting restart");
                        RequestAutoRestart();
                        break;
                    }

                    Thread.Sleep(2); // Pause courte avant retry
                }
            }

            SafeLog("Audio processing loop ended");
        }

        // Validation de la cohérence des buffers audio améliorée
        private bool ValidateAudioBuffer(short[] buffer, int length)
        {
            if (buffer == null || length <= 0 || length > buffer.Length)
                return false;

            // Détection de corruption évidente
            bool allZero = true;
            bool allSaturated = true;
            int zeroCount = 0;
            int saturationCount = 0;

            for (int i = 0; i < length; i++)
            {
                if (buffer[i] == 0) zeroCount++;
                else allZero = false;

                if (Math.Abs(buffer[i]) >= short.MaxValue - 100) saturationCount++;
                else allSaturated = false;

                if (!allZero && !allSaturated) break; // Optimisation
            }

            // Critères plus nuancés
            float zeroRatio = (float)zeroCount / length;
            float saturationRatio = (float)saturationCount / length;

            if (zeroRatio > 0.95f) // Plus de 95% de zéros
            {
                SafeLog($"Suspicious audio buffer: {zeroRatio:P1} zeros");
                return false;
            }

            if (saturationRatio > 0.90f) // Plus de 90% de saturation
            {
                SafeLog($"Suspicious audio buffer: {saturationRatio:P1} saturated");
                return false;
            }

            return true;
        }

        // Détection de niveaux audio dangereux améliorée
        private bool DetectUnsafeLevel(short[] buffer, int length)
        {
            double sum = 0;
            int peak = 0;
            int dangerousSamples = 0;

            for (int i = 0; i < length; i++)
            {
                int abs = Math.Abs(buffer[i]);
                if (abs > peak) peak = abs;
                sum += abs * (double)abs;

                // Compter les échantillons dangereux
                if (abs > short.MaxValue * 0.95) dangerousSamples++;
            }

            double rms = Math.Sqrt(sum / length);
            double rmsDb = 20.0 * Math.Log10((rms + 1e-9) / short.MaxValue);
            double peakDb = 20.0 * Math.Log10((peak + 1e-9) / (double)short.MaxValue);

            // Critères multiples pour danger
            bool rmsUnsafe = rmsDb > MAX_SAFE_LEVEL_DB;
            bool peakUnsafe = peakDb > EMERGENCY_LEVEL_DB;
            bool saturationUnsafe = (double)dangerousSamples / length > 0.1; // Plus de 10% saturés

            if (rmsUnsafe || peakUnsafe || saturationUnsafe)
            {
                SafeLog($"Unsafe audio: RMS={rmsDb:F1}dB, Peak={peakDb:F1}dB, Sat={dangerousSamples}/{length}");
                return true;
            }

            return false;
        }

        // Fade-in progressif pour éviter les chocs auditifs
        private void ApplyFadeIn(short[] buffer, int length)
        {
            if (_fadeGain >= 1.0f) return; // Fade terminé

            float fadeStep = 1.0f / (FADE_TIME_MS / _frameMs); // Progression par frame

            for (int i = 0; i < length; i++)
            {
                buffer[i] = (short)(buffer[i] * _fadeGain);
                _fadeGain = Math.Min(1.0f, _fadeGain + fadeStep / length);
            }
        }

        // Écriture vers la sortie avec gestion d'erreur et retry
        private bool WriteToOutput(short[] frame, int length)
        {
            const int MAX_WRITE_RETRIES = 2;

            for (int retry = 0; retry < MAX_WRITE_RETRIES; retry++)
            {
                try
                {
                    if (_isStereoOut)
                    {
                        var stereo = new short[length * 2];
                        for (int i = 0, j = 0; i < length; i++)
                        {
                            short s = frame[i];
                            stereo[j++] = s;
                            stereo[j++] = s;
                        }
                        int written = _trk.Write(stereo, 0, stereo.Length);
                        return written > 0;
                    }
                    else
                    {
                        int written = _trk.Write(frame, 0, length);
                        return written > 0;
                    }
                }
                catch (Exception ex)
                {
                    SafeLog($"Output write error (retry {retry}): {ex.Message}");
                    if (retry < MAX_WRITE_RETRIES - 1)
                    {
                        Thread.Sleep(1); // Petit délai avant retry
                    }
                }
            }

            return false;
        }

        // Application du gain avec protection intégrée renforcée
        private void ApplyGainWithProtection(short[] buf, int len, int gainDb)
        {
            if (gainDb == 0 && !_safeMode)
            {
                ApplyGain(buf, len, gainDb);
                return;
            }

            // Protection adaptative selon le mode
            int effectiveGain = gainDb;
            if (_safeMode)
            {
                effectiveGain = Math.Min(gainDb, 3); // Max +3dB en safe mode
            }
            if (_safetyLimiterEnabled)
            {
                effectiveGain = Math.Min(effectiveGain, MAX_SAFE_GAIN_DB);
            }

            double g = Math.Pow(10.0, effectiveGain / 20.0);

            for (int i = 0; i < len; i++)
            {
                double v = buf[i] * g;

                // Limiteur doux progressif pour éviter la saturation
                if (Math.Abs(v) > short.MaxValue * 0.90) // -1dB environ
                {
                    double sign = Math.Sign(v);
                    double abs = Math.Abs(v);
                    double limit = short.MaxValue * 0.90;

                    // Compression douce au-dessus du seuil
                    double excess = abs - limit;
                    double compressed = limit + excess * 0.2; // 80% de réduction de l'excès
                    v = sign * Math.Min(compressed, short.MaxValue * 0.95);
                }

                buf[i] = (short)Math.Round(v);
            }
        }

        private void ApplyGain(short[] buf, int len, int gainDb)
        {
            if (gainDb == 0) return;
            double g = _gainMultiplier;
            for (int i = 0; i < len; i++)
            {
                long v = (long)Math.Round(buf[i] * g);
                if (v > short.MaxValue) v = short.MaxValue;
                else if (v < short.MinValue) v = short.MinValue;
                buf[i] = (short)v;
            }
        }

        // Mise à jour des métriques de performance avec prédiction
        private void UpdateMetrics()
        {
            _cpuTimer.Stop();
            _totalCpuTime += _cpuTimer.Elapsed.TotalMilliseconds;
            _processedFrames++;

            // Log périodique des métriques avec plus de détails
            if ((DateTime.Now - _lastMetricsUpdate).TotalSeconds >= 10) // Toutes les 10 secondes
            {
                var metrics = GetHealthMetrics();
                SafeLog($"Health: CPU={metrics.CpuUsagePercent}%, Underruns={metrics.BufferUnderrunsCount}, " +
                       $"Latency={metrics.LatencyMs}ms, Memory={metrics.MemoryPressureMB}MB, " +
                       $"Stability={metrics.StabilityScore:F2}, Trend={metrics.PerformanceTrend:F2}");

                try
                {
                    LogUtilities.Log(_ctx, "PERF",
                        $"CPU={metrics.CpuUsagePercent}% Frames={_processedFrames} " +
                        $"Errors={_totalErrors} Restarts={_autoRestartCount}");
                }
                catch { }

                _lastMetricsUpdate = DateTime.Now;

                // Auto-désactivation du safe mode si stable
                if (_safeMode && _processedFrames % 1000 == 0 && metrics.IsStable) // Check toutes les ~10 secondes
                {
                    _safeMode = false;
                    _dangerousLevelCount = Math.Max(0, _dangerousLevelCount - 1);
                    SafeLog("Safe mode auto-disabled due to stability");
                }

                // Alerte si tendance négative
                if (metrics.PerformanceTrend < -0.5)
                {
                    SafeLog("Warning: Performance degradation detected");
                }
            }
        }

        // Auto-restart amélioré avec limitation
        private void RequestAutoRestart()
        {
            if ((DateTime.Now - _lastAutoRestart).TotalMilliseconds < AUTO_RESTART_COOLDOWN_MS)
            {
                SafeLog("Auto-restart cooldown active, skipping");
                return;
            }

            if (_autoRestartCount >= MAX_AUTO_RESTARTS)
            {
                SafeLog("Maximum auto-restarts reached, manual intervention required");
                EmergencyStop();
                return;
            }

            _lastAutoRestart = DateTime.Now;
            _autoRestartCount++;
            _automaticRecoveries++;
            SafeLog($"Requesting auto-restart #{_autoRestartCount}...");

            // Restart asynchrone pour ne pas bloquer la boucle audio
            Task.Run(() =>
            {
                try
                {
                    Thread.Sleep(100); // Petit délai
                    Stop();
                    Thread.Sleep(500); // Pause avant redémarrage

                    if (Start())
                    {
                        SafeLog($"Auto-restart #{_autoRestartCount} successful");
                    }
                    else
                    {
                        SafeLog($"Auto-restart #{_autoRestartCount} failed");
                        if (_autoRestartCount >= MAX_AUTO_RESTARTS)
                        {
                            EmergencyStop();
                        }
                    }
                }
                catch (Exception ex)
                {
                    SafeLog($"Auto-restart #{_autoRestartCount} error: {ex.Message}");
                    _totalErrors++;
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Abs16(short s) => s == short.MinValue ? 32768 : Math.Abs((int)s);

        private void PushMeters(short[] buf, int len, float grDb)
        {
            if (_meters == null) return;

            try
            {
                double sum = 0;
                int pk = 0;
                for (int i = 0; i < len; i++)
                {
                    int si = buf[i];
                    sum += si * (double)si;
                    int a = Abs16((short)si);
                    if (a > pk) pk = a;
                }
                double rms = Math.Sqrt(sum / Math.Max(1, len));
                double pkRef = Math.Min(pk, 32767);
                float rmsDb = (float)(20.0 * Math.Log10((rms + 1e-9) / short.MaxValue));
                float pkDb = (float)(20.0 * Math.Log10((pkRef + 1e-9) / (double)short.MaxValue));

                _meters?.Invoke(rmsDb, pkDb, grDb);
            }
            catch (Exception ex)
            {
                SafeLog($"Meters error: {ex.Message}");
            }
        }

        // Safe logging qui ne throw jamais
        private void SafeLog(string message)
        {
            try
            {
                _log?.Log(message);
            }
            catch
            {
                // Ignore les erreurs de log pour ne jamais planter le pipeline audio
            }
        }

        // Watchdog public pour monitoring externe
        public bool IsAudioThreadAlive() => _watchdog.IsAlive();
        public TimeSpan TimeSinceLastHeartbeat() => _watchdog.TimeSinceLastHeartbeat();
        public double GetFailureRisk() => _watchdog.PredictFailureRisk();

        // Nouvelles métriques publiques
        public int TotalErrors => _totalErrors;
        public int AutomaticRecoveries => _automaticRecoveries;
        public bool IsSafetyLimiterEnabled => _safetyLimiterEnabled;
        public bool IsInSafeMode => _safeMode;

        // ================== utils ==================
        private static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        private static int NextPow2(int n) { int p = 1; while (p < n) p <<= 1; return p; }
        private static int DbToNrStrength(int db)
        {
            if (db <= 0) return 0;
            if (db >= 24) return 100;
            return (int)Math.Round(db * (100.0 / 24.0));
        }

        private sealed class NullLog : Java.Lang.Object, ILogSink
        {
            public void Log(string msg) { /* no-op */ }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    Stop();
                    _dsp?.Dispose();
                    _eq?.Dispose();
                    _snr?.Dispose();
                    _platFx?.Dispose();
                    SafeLog("AudioEngine disposed");
                }
                catch (Exception ex)
                {
                    SafeLog($"Error during dispose: {ex.Message}");
                }
            }

            _disposed = true;
            base.Dispose(disposing);
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}