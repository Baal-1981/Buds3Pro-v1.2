using System;
using System.Linq;

namespace Buds3ProAideAuditiveIA.v2
{
    public class DspChain : IDisposable
    {
        // Étages optimisés
        private readonly Biquad _hpf = new Biquad();
        private readonly Biquad _peak = new Biquad();
        private readonly Compressor _comp = new Compressor();

        // État avec monitoring avancé
        private int _sr = 48000;
        private int _nrStrength = 6;
        private float _noiseFloor = 0.02f;

        // Cache des coefficients pour éviter les recalculs
        private bool _hpfDirty = true;
        private bool _peakDirty = true;
        private bool _compDirty = true;

        // Paramètres cachés avec validation
        private double _cachedHpfFreq = 120.0;
        private readonly double _cachedPeakFreq = 2800.0;
        private double _cachedPeakGain = 4.0;
        private readonly int _cachedSampleRate = 48000;

        // Protection et monitoring améliorés
        private bool _isConfigured = false;
        private int _processedSamples = 0;
        private int _errorCount = 0;
        private readonly int _maxErrors = 5;
        private int _totalErrors = 0;
        private DateTime _lastErrorTime = DateTime.MinValue;

        // Buffer temporaire optimisé avec réutilisation
        private float[] _tempBuffer;
        private readonly object _bufferLock = new object();

        // Nouvelles métriques de performance
        private double _totalProcessingTime = 0;
        private int _performanceFrames = 0;
        private double _avgCpuTime = 0;

        // Auto-calibration améliorée
        private bool _autoCalibrationActive = true;
        private int _calibrationFrames = 0;
        private const int AUTO_CALIBRATION_FRAMES = 100;

        // Disposition flag
        private bool _disposed = false;

        public DspChain(int sampleRate = 48000)
        {
            Init(sampleRate);
        }

        public void Init(int sampleRate)
        {
            try
            {
                _sr = Math.Max(8000, Math.Min(192000, sampleRate));

                // Pré-allocation du buffer temporaire avec marge de sécurité
                int maxFrameSize = _sr / 20; // 50ms max
                lock (_bufferLock)
                {
                    _tempBuffer = new float[maxFrameSize + 128]; // Marge pour éviter les réallocations
                }

                UpdateFiltersIfNeeded();
                UpdateCompressorIfNeeded();

                _noiseFloor = 0.02f;
                _isConfigured = true;
                _errorCount = 0;
                _totalErrors = 0;

                System.Diagnostics.Debug.WriteLine($"[DspChain] Initialized: SR={_sr}Hz with enhanced safety");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] Init error: {ex.Message}");
                _isConfigured = false;
                _totalErrors++;
                throw;
            }
        }

        // Mise à jour conditionnelle des filtres avec cache intelligent
        private void UpdateFiltersIfNeeded()
        {
            try
            {
                if (_hpfDirty)
                {
                    _hpf.DesignHighpass(_sr, _cachedHpfFreq, 0.707);
                    _hpfDirty = false;
                    System.Diagnostics.Debug.WriteLine($"[DspChain] HPF updated: {_cachedHpfFreq}Hz");
                }

                if (_peakDirty)
                {
                    _peak.DesignPeaking(_sr, _cachedPeakFreq, 1.0, _cachedPeakGain);
                    _peakDirty = false;
                    System.Diagnostics.Debug.WriteLine($"[DspChain] Peak updated: {_cachedPeakGain}dB @ {_cachedPeakFreq}Hz");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] Filter update error: {ex.Message}");
                _errorCount++;
                _totalErrors++;
                _lastErrorTime = DateTime.Now;
            }
        }

        private void UpdateCompressorIfNeeded()
        {
            try
            {
                if (_compDirty)
                {
                    _comp.SampleRate = _sr;
                    _comp.ThresholdDb = -18.0;
                    _comp.Ratio = 3.0;
                    _comp.KneeDb = 6.0;
                    _comp.AttackMs = 5.0;
                    _comp.ReleaseMs = 80.0;
                    _comp.MakeupDb = 0.0;
                    _comp.CeilingDb = -1.0;

                    // Nouvelles optimisations
                    _comp.EnableLookAhead = true;
                    _comp.LookAheadMs = 5.0;
                    _comp.EnableAdaptiveRelease = true;
                    _comp.EnableTransientDetection = true;

                    _comp.Reset();
                    _compDirty = false;
                    System.Diagnostics.Debug.WriteLine("[DspChain] Compressor updated with advanced features");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] Compressor update error: {ex.Message}");
                _errorCount++;
                _totalErrors++;
                _lastErrorTime = DateTime.Now;
            }
        }

        // === Réglages exposés avec cache intelligent et validation ===
        public void SetVoiceBoostDb(int db)
        {
            try
            {
                db = Math.Max(-8, Math.Min(+8, db));
                double newGain = db;

                if (Math.Abs(newGain - _cachedPeakGain) > 0.1)
                {
                    _cachedPeakGain = newGain;
                    _peakDirty = true;
                    System.Diagnostics.Debug.WriteLine($"[DspChain] Voice boost set to {db}dB");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] SetVoiceBoostDb error: {ex.Message}");
                _errorCount++;
                _totalErrors++;
            }
        }

        public void SetNrStrength(int s)
        {
            try
            {
                int oldStrength = _nrStrength;
                _nrStrength = Math.Max(0, Math.Min(100, s));

                if (oldStrength != _nrStrength)
                {
                    System.Diagnostics.Debug.WriteLine($"[DspChain] NR strength: {oldStrength}% → {_nrStrength}%");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] SetNrStrength error: {ex.Message}");
                _errorCount++;
            }
        }

        public void SetHighPassFreq(double freq)
        {
            try
            {
                double newFreq = Math.Max(40.0, Math.Min(400.0, freq));
                if (Math.Abs(newFreq - _cachedHpfFreq) > 1.0)
                {
                    _cachedHpfFreq = newFreq;
                    _hpfDirty = true;
                    System.Diagnostics.Debug.WriteLine($"[DspChain] HPF frequency set to {newFreq:F1}Hz");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] SetHighPassFreq error: {ex.Message}");
                _errorCount++;
                _totalErrors++;
            }
        }

        public void Calibrate()
        {
            try
            {
                _noiseFloor = 0.0f;
                _calibrationFrames = 0;
                _autoCalibrationActive = true;
                System.Diagnostics.Debug.WriteLine("[DspChain] Manual calibration started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] Calibrate error: {ex.Message}");
                _errorCount++;
            }
        }

        public float LastGainReductionDb => _comp?.LastGainReductionDb ?? 0f;

        // Propriétés de santé améliorées
        public bool IsHealthy => _isConfigured && _errorCount < _maxErrors && _comp?.IsHealthy == true;
        public int ProcessedSamples => _processedSamples;
        public int ErrorCount => _errorCount;
        public int TotalErrors => _totalErrors;
        public double AverageCpuTime => _avgCpuTime;
        public bool IsCalibrating => _autoCalibrationActive && _calibrationFrames < AUTO_CALIBRATION_FRAMES;

        // === Chaîne de traitement optimisée avec monitoring ===
        public void Process(short[] buf, int n)
        {
            if (!_isConfigured || buf == null || n <= 0)
            {
                return;
            }

            if (n > buf.Length) n = buf.Length;

            // Protection contre trop d'erreurs
            if (_errorCount >= _maxErrors)
            {
                System.Diagnostics.Debug.WriteLine("[DspChain] Too many errors, entering bypass mode");
                return;
            }

            var startTime = DateTime.Now;

            try
            {
                // Mise à jour des filtres si nécessaire
                UpdateFiltersIfNeeded();
                UpdateCompressorIfNeeded();

                // Redimensionner le buffer temporaire si nécessaire (thread-safe)
                lock (_bufferLock)
                {
                    if (_tempBuffer.Length < n)
                    {
                        _tempBuffer = new float[n + 128];
                        System.Diagnostics.Debug.WriteLine($"[DspChain] Buffer resized to {_tempBuffer.Length}");
                    }
                }

                // Conversion short -> float optimisée
                ConvertToFloat(buf, _tempBuffer, n);

                // Estimation RMS rapide pour gate et calibration
                double rmsSquared = CalculateRmsSquared(_tempBuffer, n);
                double rms = Math.Sqrt(rmsSquared);

                // Auto-calibration améliorée
                if (_autoCalibrationActive)
                {
                    PerformAutoCalibration(rms);
                }

                // Gate adaptatif très léger avec amélioration
                ApplyAdaptiveGate(_tempBuffer, n, rms);

                // Filtres avec gestion d'erreur
                try
                {
                    _hpf.ProcessInPlace(_tempBuffer, n);
                    _peak.ProcessInPlace(_tempBuffer, n);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DspChain] Filter processing error: {ex.Message}");
                    _errorCount++;
                    _totalErrors++;
                }

                // Compression avec gestion d'erreur avancée
                try
                {
                    _comp.ProcessInPlace(_tempBuffer, n);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DspChain] Compressor error: {ex.Message}");
                    _errorCount++;
                    _totalErrors++;
                    // Continue sans compression si erreur
                }

                // Limiteur de sécurité et conversion retour
                ConvertToShortWithLimiting(_tempBuffer, buf, n);

                _processedSamples += n;

                // Mise à jour des métriques de performance
                UpdatePerformanceMetrics(startTime);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] Critical processing error: {ex.Message}");
                _errorCount++;
                _totalErrors++;
                _lastErrorTime = DateTime.Now;

                // En cas d'erreur critique, passer en mode bypass
                if (_errorCount >= _maxErrors)
                {
                    System.Diagnostics.Debug.WriteLine("[DspChain] Entering bypass mode due to errors");
                }
            }
        }

        // Auto-calibration améliorée
        private void PerformAutoCalibration(double rms)
        {
            try
            {
                if (_calibrationFrames < AUTO_CALIBRATION_FRAMES)
                {
                    if (_noiseFloor == 0.0f && rms > 0)
                    {
                        _noiseFloor = (float)(rms * 0.8);
                    }
                    else if (rms > 0)
                    {
                        // Lissage progressif
                        _noiseFloor = (float)(_noiseFloor * 0.95 + rms * 0.05);
                    }

                    _calibrationFrames++;

                    if (_calibrationFrames >= AUTO_CALIBRATION_FRAMES)
                    {
                        _autoCalibrationActive = false;
                        System.Diagnostics.Debug.WriteLine($"[DspChain] Auto-calibration completed: noise floor = {_noiseFloor:F4}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] Auto-calibration error: {ex.Message}");
                _autoCalibrationActive = false;
            }
        }

        // Mise à jour des métriques de performance
        private void UpdatePerformanceMetrics(DateTime startTime)
        {
            try
            {
                var elapsed = DateTime.Now - startTime;
                _totalProcessingTime += elapsed.TotalMilliseconds;
                _performanceFrames++;

                if (_performanceFrames > 0)
                {
                    _avgCpuTime = _totalProcessingTime / _performanceFrames;
                }

                // Log périodique des performances (toutes les 1000 frames)
                if (_performanceFrames % 1000 == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[DspChain] Performance: {_avgCpuTime:F3}ms avg, {_errorCount}/{_maxErrors} errors, {_totalErrors} total errors");

                    // Reset du compteur d'erreurs si pas d'erreur récente
                    if (_errorCount > 0 && (DateTime.Now - _lastErrorTime).TotalSeconds > 30)
                    {
                        _errorCount = Math.Max(0, _errorCount - 1);
                        System.Diagnostics.Debug.WriteLine($"[DspChain] Error count reduced to {_errorCount} (recovery)");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] Performance metrics error: {ex.Message}");
            }
        }

        // Conversion optimisée short[] -> float[]
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ConvertToFloat(short[] input, float[] output, int count)
        {
            const float inv32768 = 1.0f / 32768f;
            for (int i = 0; i < count; i++)
            {
                output[i] = input[i] * inv32768;
            }
        }

        // Calcul RMS optimisé
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static double CalculateRmsSquared(float[] buffer, int count)
        {
            double sum = 0.0;
            for (int i = 0; i < count; i++)
            {
                double v = buffer[i];
                sum += v * v;
            }
            return sum / Math.Max(1, count);
        }

        // Gate adaptatif avec amélioration progressive
        private void ApplyAdaptiveGate(float[] buffer, int count, double rms)
        {
            if (_nrStrength <= 0 || _noiseFloor <= 0) return;

            // Seuil adaptatif basé sur le strength et l'historique
            float threshold = _noiseFloor * (1.2f - _nrStrength * 0.01f);
            float gateAmount = Math.Min(0.8f, _nrStrength * 0.008f);

            // Adaptation basée sur le niveau RMS actuel
            if (rms > _noiseFloor * 2.0)
            {
                gateAmount *= 0.5f; // Moins de gate si signal fort
            }

            // Gate doux avec transition progressive
            for (int i = 0; i < count; i++)
            {
                float abs = Math.Abs(buffer[i]);
                if (abs < threshold)
                {
                    float ratio = abs / threshold;
                    float reduction = gateAmount * (1.0f - ratio);
                    buffer[i] *= Math.Max(0.1f, 1.0f - reduction);
                }
            }
        }

        // Conversion retour avec limiteur de sécurité amélioré
        private static void ConvertToShortWithLimiting(float[] input, short[] output, int count)
        {
            const float limit = 0.95f; // Protection contre saturation
            const float scale = 32767f;

            for (int i = 0; i < count; i++)
            {
                float v = input[i];

                // Limiteur doux progressif
                if (v > limit) v = limit + (v - limit) * 0.1f;
                else if (v < -limit) v = -limit + (v + limit) * 0.1f;

                int s = (int)(v * scale);

                // Sécurité finale
                if (s > 32767) s = 32767;
                else if (s < -32768) s = -32768;

                output[i] = (short)s;
            }
        }

        // Reset complet du pipeline avec diagnostic
        public void Reset()
        {
            try
            {
                _hpf?.Reset();
                _peak?.Reset();
                _comp?.Reset();
                _noiseFloor = 0.02f;
                _processedSamples = 0;
                _errorCount = 0;
                _calibrationFrames = 0;
                _autoCalibrationActive = true;
                _totalProcessingTime = 0;
                _performanceFrames = 0;

                System.Diagnostics.Debug.WriteLine("[DspChain] Complete reset with auto-calibration restart");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DspChain] Reset error: {ex.Message}");
                _totalErrors++;
            }
        }

        // Diagnostic et monitoring amélioré
        public string GetStatusReport()
        {
            var compStatus = _comp?.IsHealthy == true ? "OK" : "ERROR";
            var calibStatus = IsCalibrating ? "CALIBRATING" : "READY";

            return $"DspChain Status: Config={_isConfigured}, Errors={_errorCount}/{_maxErrors} (Total={_totalErrors}), " +
                   $"Processed={_processedSamples} samples, NoiseFloor={_noiseFloor:F4}, " +
                   $"SR={_sr}Hz, NR={_nrStrength}%, Comp={compStatus}, Calib={calibStatus}, " +
                   $"AvgCPU={_avgCpuTime:F3}ms";
        }

        // Optimisation: invalidation sélective des caches
        public void InvalidateFilters()
        {
            _hpfDirty = true;
            _peakDirty = true;
            System.Diagnostics.Debug.WriteLine("[DspChain] Filters invalidated");
        }

        public void InvalidateCompressor()
        {
            _compDirty = true;
            System.Diagnostics.Debug.WriteLine("[DspChain] Compressor invalidated");
        }

        // Reconfiguration rapide pour changement de sample rate
        public void ReconfigureForSampleRate(int newSampleRate)
        {
            if (newSampleRate != _sr)
            {
                _sr = Math.Max(8000, Math.Min(192000, newSampleRate));

                // Force la mise à jour de tous les composants
                InvalidateFilters();
                InvalidateCompressor();

                // Reconfigurer le compresseur
                _comp?.ReconfigureForSampleRate(_sr);

                // Redimensionner le buffer si nécessaire
                int maxFrameSize = _sr / 20;
                lock (_bufferLock)
                {
                    if (_tempBuffer.Length < maxFrameSize)
                    {
                        _tempBuffer = new float[maxFrameSize + 128];
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[DspChain] Reconfigured for SR={_sr}Hz");
            }
        }

        // Nouvelle méthode: obtenir les métriques de performance détaillées
        public (double avgCpuTime, int errors, int totalErrors, bool isHealthy, bool isCalibrating) GetPerformanceMetrics()
        {
            return (_avgCpuTime, _errorCount, _totalErrors, IsHealthy, IsCalibrating);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    _isConfigured = false;
                    _hpf?.Reset();
                    _peak?.Reset();
                    _comp?.Dispose();

                    lock (_bufferLock)
                    {
                        _tempBuffer = null;
                    }

                    System.Diagnostics.Debug.WriteLine("[DspChain] Disposed cleanly");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DspChain] Dispose error: {ex.Message}");
                }
            }

            _disposed = true;
        }
    }
}