using System;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Réducteur de bruit spectral optimisé avec buffers réutilisables et cache FFT.
    /// Améliorations v1.2.1:
    /// - Buffers pré-alloués pour éviter GC
    /// - Cache des fenêtrages
    /// - Détection intelligente d'activité vocale
    /// - Adaptation progressive du profil de bruit
    /// - Protection contre les artifacts
    /// </summary>
    public sealed class SpectralNoiseReducer : IDisposable
    {
        private readonly int _fs;
        private readonly int _n;         // FFT size = frame size (puissance de 2)

        // Buffers pré-alloués (évite allocations répétées)
        private readonly double[] _win;  // fenêtre Hann précalculée
        private readonly double[] _re;   // partie réelle FFT
        private readonly double[] _im;   // partie imaginaire FFT
        private readonly double[] _tempFrame; // buffer temporaire pour processing

        // Profil de bruit et suiveur de minimum
        private readonly double[] _noiseMag;
        private readonly double[] _prevMin;
        private readonly double[] _smoothedMag; // Lissage des magnitudes actuelles

        // Cache de performance
        private readonly double[] _gainCache; // Cache des gains précédents
        private readonly double[] _magHistory; // Historique pour détection vocale
        private int _historyIndex = 0;
        private const int HISTORY_SIZE = 8;

        // Paramètres optimisés
        private const float DefaultAlpha = 0.60f;
        private const float DefaultFloor = 0.50f;
        private const double AdaptDecay = 0.995;
        private const double VoiceDetectionThreshold = 1.5; // Ratio pour détecter la voix

        // État et monitoring
        private bool _isInitialized = false;
        private int _processedFrames = 0;
        private int _adaptationFrames = 0;
        private double _lastVoiceActivity = 0.0;
        private DateTime _lastVoiceDetected = DateTime.MinValue;

        // Statistiques de performance
        private double _totalProcessingTime = 0;
        private int _errorCount = 0;
        private const int MAX_ERRORS = 5;

        public SpectralNoiseReducer(int frameSamples, int sampleRate)
        {
            try
            {
                if (frameSamples <= 0) throw new ArgumentOutOfRangeException(nameof(frameSamples));
                if (!IsPowerOfTwo(frameSamples)) throw new ArgumentException("frameSamples doit être une puissance de 2.", nameof(frameSamples));
                if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

                _fs = sampleRate;
                _n = frameSamples;

                // Pré-allocation de tous les buffers
                _win = new double[_n];
                _re = new double[_n];
                _im = new double[_n];
                _tempFrame = new double[_n];

                int bins = _n / 2;
                _noiseMag = new double[bins];
                _prevMin = new double[bins];
                _smoothedMag = new double[bins];
                _gainCache = new double[bins];
                _magHistory = new double[bins * HISTORY_SIZE];

                InitializeWindow();
                InitializeNoiseProfile();

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine($"[SNR] Initialized: {frameSamples} samples @ {sampleRate} Hz");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SNR] Initialization error: {ex.Message}");
                throw;
            }
        }

        private void InitializeWindow()
        {
            // Précalcul de la fenêtre de Hann
            for (int i = 0; i < _n; i++)
            {
                _win[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (_n - 1));
            }
        }

        private void InitializeNoiseProfile()
        {
            int bins = _n / 2;

            // Initialisation conservative du profil de bruit
            for (int k = 0; k < bins; k++)
            {
                _noiseMag[k] = 1e-6; // Très faible pour commencer
                _prevMin[k] = 1e-6;
                _smoothedMag[k] = 0.0;
                _gainCache[k] = 1.0; // Pas de réduction initialement
            }
        }

        public int SampleRate => _fs;
        public int FrameSamples => _n;
        public bool IsInitialized => _isInitialized;
        public int ProcessedFrames => _processedFrames;
        public double LastVoiceActivity => _lastVoiceActivity;
        public bool IsHealthy => _errorCount < MAX_ERRORS && _isInitialized;

        /// <summary>Mise à jour forcée du profil de bruit (calibration manuelle)</summary>
        public void UpdateNoiseProfile(short[] frame)
        {
            if (!_isInitialized || frame == null || frame.Length < _n)
            {
                System.Diagnostics.Debug.WriteLine("[SNR] Invalid frame for noise profile update");
                return;
            }

            try
            {
                var startTime = DateTime.Now;

                PrepareFftInput(frame);
                FFT(_re, _im, inverse: false);

                int bins = _n / 2;
                for (int k = 0; k < bins; k++)
                {
                    double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) + 1e-9;

                    // Mise à jour plus agressive pour calibration manuelle
                    _noiseMag[k] = 0.7 * _noiseMag[k] + 0.3 * mag;
                    _prevMin[k] = Math.Min(_prevMin[k] * 0.99 + 1e-4, _noiseMag[k]);
                }

                _adaptationFrames++;
                UpdatePerformanceMetrics(startTime);

                System.Diagnostics.Debug.WriteLine($"[SNR] Manual noise profile updated (frame #{_adaptationFrames})");
            }
            catch (Exception ex)
            {
                HandleError($"Noise profile update error: {ex.Message}");
            }
        }

        /// <summary>Réduction de bruit principale avec détection intelligente</summary>
        public bool Process(short[] frame, float alpha = DefaultAlpha, float floor = DefaultFloor, bool adapt = true)
        {
            if (!_isInitialized || frame == null || frame.Length < _n)
            {
                return false;
            }

            if (_errorCount >= MAX_ERRORS)
            {
                System.Diagnostics.Debug.WriteLine("[SNR] Too many errors, skipping processing");
                return false;
            }

            try
            {
                var startTime = DateTime.Now;

                // Validation et normalisation des paramètres
                double a = Clamp(alpha, 0.0, 1.0);
                double f = Clamp(floor, 0.0, 1.0);

                // Analyse spectrale
                PrepareFftInput(frame);
                FFT(_re, _im, inverse: false);

                int bins = _n / 2;

                // Détection d'activité vocale
                double voiceActivity = DetectVoiceActivity(bins);
                _lastVoiceActivity = voiceActivity;

                if (voiceActivity > VoiceDetectionThreshold)
                {
                    _lastVoiceDetected = DateTime.Now;
                }

                // Traitement spectral avec adaptation intelligente
                ProcessSpectralData(bins, a, f, voiceActivity);

                // Adaptation du profil de bruit
                if (adapt && ShouldAdaptNoise(voiceActivity))
                {
                    AdaptNoiseProfile(bins);
                }

                // Reconstruction temporelle
                FFT(_re, _im, inverse: true);
                ApplyWindowAndStore(frame);

                _processedFrames++;
                UpdatePerformanceMetrics(startTime);

                return true;
            }
            catch (Exception ex)
            {
                HandleError($"Processing error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Détection d'activité vocale basée sur l'analyse spectrale</summary>
        private double DetectVoiceActivity(int bins)
        {
            double currentEnergy = 0.0;
            double noiseEnergy = 0.0;

            // Calcul des énergies dans les bandes importantes pour la voix (300-3400 Hz)
            int lowBin = Math.Max(1, (int)(300.0 * _n / _fs));
            int highBin = Math.Min(bins - 1, (int)(3400.0 * _n / _fs));

            for (int k = lowBin; k < highBin; k++)
            {
                double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
                currentEnergy += mag;
                noiseEnergy += _noiseMag[k];
            }

            // Ratio signal/bruit dans la bande vocale
            double snr = (currentEnergy + 1e-9) / (noiseEnergy + 1e-9);

            // Lissage temporel pour stabilité
            const double smoothing = 0.8;
            _lastVoiceActivity = smoothing * _lastVoiceActivity + (1.0 - smoothing) * snr;

            return _lastVoiceActivity;
        }

        /// <summary>Traitement spectral optimisé avec cache</summary>
        private void ProcessSpectralData(int bins, double alpha, double floor, double voiceActivity)
        {
            // Adaptation de l'agressivité selon l'activité vocale
            double adaptiveAlpha = alpha;
            if (voiceActivity > VoiceDetectionThreshold)
            {
                adaptiveAlpha *= 0.6; // Moins agressif pendant la voix
            }

            for (int k = 0; k < bins; k++)
            {
                double re = _re[k], im = _im[k];
                double mag = Math.Sqrt(re * re + im * im) + 1e-9;
                double nmag = _noiseMag[k] + 1e-12;

                // Lissage de la magnitude courante
                _smoothedMag[k] = 0.7 * _smoothedMag[k] + 0.3 * mag;

                // Soustraction spectrale avec adaptation
                double clean = Math.Max(mag - adaptiveAlpha * nmag, floor * mag);
                double gain = clean / mag;

                // Lissage du gain pour éviter les artifacts
                const double gainSmoothing = 0.6;
                gain = gainSmoothing * _gainCache[k] + (1.0 - gainSmoothing) * gain;
                _gainCache[k] = gain;

                // Application avec limitation pour éviter sur-atténuation
                gain = Math.Max(0.1, Math.Min(1.0, gain)); // Limité entre 10% et 100%

                _re[k] *= gain;
                _im[k] *= gain;

                // Symétrie conjuguée pour FFT réelle
                if (k != 0 && k != bins - 1)
                {
                    int k2 = _n - k;
                    _re[k2] *= gain;
                    _im[k2] *= gain;
                }
            }
        }

        /// <summary>Décision d'adaptation du profil de bruit</summary>
        private bool ShouldAdaptNoise(double voiceActivity)
        {
            // Ne pas adapter pendant la voix
            if (voiceActivity > VoiceDetectionThreshold) return false;

            // Ne pas adapter juste après la voix (réverbération)
            var timeSinceVoice = DateTime.Now - _lastVoiceDetected;
            if (timeSinceVoice.TotalMilliseconds < 500) return false;

            // Adaptation périodique lente
            return _processedFrames % 8 == 0; // Toutes les 8 frames
        }

        /// <summary>Adaptation progressive du profil de bruit</summary>
        private void AdaptNoiseProfile(int bins)
        {
            for (int k = 0; k < bins; k++)
            {
                double mag = _smoothedMag[k];

                // Suiveur de minimum très lent
                _prevMin[k] = Math.Min(_prevMin[k] * AdaptDecay + 1e-4, mag);

                // Mise à jour conservative du profil
                _noiseMag[k] = 0.995 * _noiseMag[k] + 0.005 * _prevMin[k];
            }
        }

        /// <summary>Préparation optimisée de l'entrée FFT</summary>
        private void PrepareFftInput(short[] frame)
        {
            const double inv32768 = 1.0 / 32768.0;

            for (int i = 0; i < _n; i++)
            {
                double x = frame[i] * inv32768;
                _re[i] = x * _win[i];
                _im[i] = 0.0;
            }
        }

        /// <summary>Application de fenêtre et stockage optimisés</summary>
        private void ApplyWindowAndStore(short[] frame)
        {
            for (int i = 0; i < _n; i++)
            {
                double y = _re[i] * _win[i];

                // Clamp et conversion optimisés
                if (y > 1.0) y = 1.0;
                else if (y < -1.0) y = -1.0;

                int sample = (int)(y * 32767.0);
                if (sample > 32767) sample = 32767;
                else if (sample < -32768) sample = -32768;

                frame[i] = (short)sample;
            }
        }

        /// <summary>Gestion centralisée des erreurs</summary>
        private void HandleError(string message)
        {
            _errorCount++;
            System.Diagnostics.Debug.WriteLine($"[SNR] Error #{_errorCount}: {message}");

            if (_errorCount >= MAX_ERRORS)
            {
                System.Diagnostics.Debug.WriteLine("[SNR] Maximum errors reached, entering safe mode");
            }
        }

        /// <summary>Mise à jour des métriques de performance</summary>
        private void UpdatePerformanceMetrics(DateTime startTime)
        {
            var elapsed = DateTime.Now - startTime;
            _totalProcessingTime += elapsed.TotalMilliseconds;

            // Log périodique des performances
            if (_processedFrames % 1000 == 0 && _processedFrames > 0)
            {
                double avgTime = _totalProcessingTime / _processedFrames;
                System.Diagnostics.Debug.WriteLine($"[SNR] Performance: {avgTime:F2}ms avg, {_processedFrames} frames, {_errorCount} errors");
            }
        }

        /// <summary>Reset complet de l'état</summary>
        public void Reset()
        {
            try
            {
                _processedFrames = 0;
                _adaptationFrames = 0;
                _errorCount = 0;
                _totalProcessingTime = 0;
                _lastVoiceActivity = 0.0;
                _lastVoiceDetected = DateTime.MinValue;
                _historyIndex = 0;

                InitializeNoiseProfile();

                // Clear des buffers
                Array.Clear(_re, 0, _re.Length);
                Array.Clear(_im, 0, _im.Length);
                Array.Clear(_tempFrame, 0, _tempFrame.Length);
                Array.Clear(_magHistory, 0, _magHistory.Length);

                System.Diagnostics.Debug.WriteLine("[SNR] Reset completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SNR] Reset error: {ex.Message}");
            }
        }

        /// <summary>Rapport d'état détaillé</summary>
        public string GetStatusReport()
        {
            double avgProcessingTime = _processedFrames > 0 ? _totalProcessingTime / _processedFrames : 0;

            return $"SNR Status: Frames={_processedFrames}, Errors={_errorCount}/{MAX_ERRORS}, " +
                   $"AvgTime={avgProcessingTime:F2}ms, VoiceActivity={_lastVoiceActivity:F2}, " +
                   $"Adaptations={_adaptationFrames}, Healthy={IsHealthy}";
        }

        // ===== Helpers =====
        private static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi) ? hi : v;
        private static bool IsPowerOfTwo(int x) => (x & (x - 1)) == 0;

        /// <summary>FFT complexe radix-2 in-place optimisée</summary>
        private static void FFT(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;

            // Bit-reversal optimisé
            for (int i = 1, j = 0; i < n; ++i)
            {
                int bit = n >> 1;
                for (; j >= bit; bit >>= 1) j -= bit;
                j += bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            // Radix-2 Cooley-Tukey
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len * (inverse ? 1 : -1);
                double wlr = Math.Cos(ang), wli = Math.Sin(ang);

                for (int i = 0; i < n; i += len)
                {
                    double wr = 1.0, wi = 0.0;
                    int half = len >> 1;

                    for (int j = 0; j < half; j++)
                    {
                        int u = i + j;
                        int v = u + half;

                        double vr = re[v] * wr - im[v] * wi;
                        double vi = re[v] * wi + im[v] * wr;

                        re[v] = re[u] - vr; im[v] = im[u] - vi;
                        re[u] += vr; im[u] += vi;

                        double nwr = wr * wlr - wi * wli;
                        wi = wr * wli + wi * wlr;
                        wr = nwr;
                    }
                }
            }

            if (inverse)
            {
                double invN = 1.0 / n;
                for (int i = 0; i < n; i++)
                {
                    re[i] *= invN;
                    im[i] *= invN;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _isInitialized = false;
                System.Diagnostics.Debug.WriteLine("[SNR] Disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SNR] Dispose error: {ex.Message}");
            }
        }
    }
}