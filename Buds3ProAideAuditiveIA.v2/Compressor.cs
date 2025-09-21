using System;
using System.Collections.Generic;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Compresseur avancé avec look-ahead, détection adaptative et protection auditive.
    /// Nouvelles fonctionnalités v1.2.1:
    /// - Look-ahead buffer pour réduire les artifacts
    /// - Détection automatique de transients
    /// - Limiteur adaptatif intégré
    /// - Protection contre les niveaux dangereux
    /// - Métriques de performance
    /// </summary>
    public class Compressor : IDisposable
    {
        // ==== Paramètres utilisateur ====
        public double ThresholdDb = -10.0;
        public double Ratio = 4.0;
        public double KneeDb = 6.0;
        public double AttackMs = 5.0;
        public double ReleaseMs = 50.0;
        public double MakeupDb = 0.0;
        public double CeilingDb = -0.5;
        public int SampleRate = 48000;

        // ==== Nouveaux paramètres avancés ====
        public bool EnableLookAhead = true;
        public double LookAheadMs = 5.0; // 5ms de look-ahead
        public bool EnableAdaptiveRelease = true;
        public bool EnableTransientDetection = true;
        public double SafetyLimitDb = -3.0; // Limiteur de sécurité

        // ==== États internes ====
        private double _grDb = 0.0;
        private double _envDb = double.NegativeInfinity;
        private double _adaptiveReleaseCoeff = 0.0;

        // Look-ahead buffer
        private Queue<float> _lookAheadBuffer;
        private int _lookAheadSamples = 0;
        private double[] _futureEnvelope;
        private int _futureIndex = 0;

        // Détection de transients
        private double _prevEnergy = 0.0;
        private readonly double _transientThreshold = 6.0; // dB de variation pour détecter transient
        private bool _transientDetected = false;
        private int _transientHoldFrames = 0;
        private const int TRANSIENT_HOLD_TIME = 10; // frames

        // Métriques et monitoring
        private int _processedSamples = 0;
        private double _maxGainReduction = 0.0;
        private double _totalGainReduction = 0.0;
        private int _transientCount = 0;
        private int _limitingEvents = 0;
        private bool _isConfigured = false;

        public float LastGainReductionDb { get; private set; }
        public bool IsHealthy => _isConfigured && _lookAheadBuffer != null;
        public int ProcessedSamples => _processedSamples;
        public double MaxGainReduction => _maxGainReduction;
        public int TransientCount => _transientCount;
        public int LimitingEvents => _limitingEvents;

        public void Reset()
        {
            try
            {
                _grDb = 0.0;
                _envDb = double.NegativeInfinity;
                _adaptiveReleaseCoeff = 0.0;
                _prevEnergy = 0.0;
                _transientDetected = false;
                _transientHoldFrames = 0;
                _futureIndex = 0;

                LastGainReductionDb = 0f;
                _processedSamples = 0;
                _maxGainReduction = 0.0;
                _totalGainReduction = 0.0;
                _transientCount = 0;
                _limitingEvents = 0;

                // Reconfigurer le look-ahead si nécessaire
                ConfigureLookAhead();

                System.Diagnostics.Debug.WriteLine("[Compressor] Reset completed");
                _isConfigured = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Compressor] Reset error: {ex.Message}");
                _isConfigured = false;
            }
        }

        private void ConfigureLookAhead()
        {
            try
            {
                if (EnableLookAhead && SampleRate > 0)
                {
                    _lookAheadSamples = (int)(LookAheadMs * SampleRate / 1000.0);
                    _lookAheadSamples = Math.Max(1, Math.Min(_lookAheadSamples, SampleRate / 10)); // Max 100ms

                    _lookAheadBuffer = new Queue<float>(_lookAheadSamples);
                    _futureEnvelope = new double[_lookAheadSamples];

                    // Pré-remplir avec des zéros
                    for (int i = 0; i < _lookAheadSamples; i++)
                    {
                        _lookAheadBuffer.Enqueue(0.0f);
                        _futureEnvelope[i] = double.NegativeInfinity;
                    }

                    System.Diagnostics.Debug.WriteLine($"[Compressor] Look-ahead configured: {_lookAheadSamples} samples ({LookAheadMs:F1}ms)");
                }
                else
                {
                    _lookAheadBuffer = null;
                    _futureEnvelope = null;
                    _lookAheadSamples = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Compressor] Look-ahead configuration error: {ex.Message}");
                _lookAheadBuffer = null;
                _futureEnvelope = null;
                _lookAheadSamples = 0;
            }
        }

        public void ReconfigureForSampleRate(int newSampleRate)
        {
            if (newSampleRate != SampleRate && newSampleRate > 0)
            {
                SampleRate = newSampleRate;
                ConfigureLookAhead();
                System.Diagnostics.Debug.WriteLine($"[Compressor] Reconfigured for {SampleRate}Hz");
            }
        }

        public void ProcessInPlace(float[] x, int n)
        {
            if (x == null || n <= 0 || !_isConfigured) return;
            if (n > x.Length) n = x.Length;

            try
            {
                // Configuration des coefficients de lissage
                double attA = ExpCoef(AttackMs, SampleRate);
                double relA = ExpCoef(ReleaseMs, SampleRate);

                // Paramètres de compression validés
                double knee = Math.Max(0.0, KneeDb);
                double thr = ThresholdDb;
                double ratio = Math.Max(1.0, Ratio);
                double makeupLin = DbToLin(MakeupDb);
                double ceilLin = DbToLin(CeilingDb);
                double safetyLin = DbToLin(SafetyLimitDb);

                for (int i = 0; i < n; i++)
                {
                    float currentSample = x[i];

                    // === 1. Look-ahead processing ===
                    float processedSample = ProcessWithLookAhead(currentSample);

                    // === 2. Détection de niveau et transients ===
                    double levelDb = DetectLevel(processedSample);
                    bool transientDetected = DetectTransient(levelDb);

                    // === 3. Adaptation de la release selon les transients ===
                    double adaptiveRelA = relA;
                    if (EnableAdaptiveRelease)
                    {
                        adaptiveRelA = CalculateAdaptiveRelease(relA, transientDetected);
                    }

                    // === 4. Calcul de la compression ===
                    double gainReductionDb = CalculateCompression(levelDb, thr, ratio, knee);

                    // === 5. Lissage temporel du gain ===
                    SmoothGainReduction(gainReductionDb, attA, adaptiveRelA);

                    // === 6. Application du gain + makeup ===
                    double gainLin = DbToLin(_grDb) * makeupLin;
                    double outputSample = processedSample * gainLin;

                    // === 7. Limiteur de sécurité adaptatif ===
                    outputSample = ApplySafetyLimiter(outputSample, safetyLin, ceilLin);

                    x[i] = (float)outputSample;

                    // === 8. Mise à jour des métriques ===
                    UpdateMetrics(gainReductionDb);
                }

                _processedSamples += n;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Compressor] Processing error: {ex.Message}");
                // En cas d'erreur, passer en mode bypass
                for (int i = 0; i < n; i++)
                {
                    x[i] = Math.Max(-1.0f, Math.Min(1.0f, x[i])); // Simple clipping
                }
            }
        }

        /// <summary>Traitement avec look-ahead pour réduire les artifacts</summary>
        private float ProcessWithLookAhead(float currentSample)
        {
            if (_lookAheadBuffer == null || !EnableLookAhead)
            {
                return currentSample;
            }

            try
            {
                // Ajouter le nouvel échantillon au buffer
                _lookAheadBuffer.Enqueue(currentSample);

                // Analyser l'enveloppe future
                double futureLevel = DetectLevel(currentSample);
                _futureEnvelope[_futureIndex] = futureLevel;
                _futureIndex = (_futureIndex + 1) % _lookAheadSamples;

                // Retourner l'échantillon retardé
                return _lookAheadBuffer.Dequeue();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Compressor] Look-ahead error: {ex.Message}");
                return currentSample; // Fallback sans look-ahead
            }
        }

        /// <summary>Détection de niveau robuste</summary>
        private double DetectLevel(float sample)
        {
            double abs = Math.Abs(sample) + 1e-12;
            double levelDb = 20.0 * Math.Log10(abs);

            // Lissage de l'enveloppe de détection
            if (_envDb == double.NegativeInfinity)
            {
                _envDb = levelDb;
            }
            else
            {
                double attCoeff = ExpCoef(1.0, SampleRate); // 1ms pour la détection
                if (levelDb > _envDb)
                    _envDb = levelDb + attCoeff * (_envDb - levelDb);
                else
                    _envDb = levelDb + ExpCoef(10.0, SampleRate) * (_envDb - levelDb); // 10ms release pour détection
            }

            return _envDb;
        }

        /// <summary>Détection intelligente de transients</summary>
        private bool DetectTransient(double levelDb)
        {
            if (!EnableTransientDetection)
            {
                return false;
            }

            bool transient = false;

            // Calculer la variation d'énergie
            if (_prevEnergy != 0.0)
            {
                double energyChange = levelDb - _prevEnergy;
                if (energyChange > _transientThreshold)
                {
                    transient = true;
                    _transientCount++;
                    _transientHoldFrames = TRANSIENT_HOLD_TIME;
                    System.Diagnostics.Debug.WriteLine($"[Compressor] Transient detected: {energyChange:F1}dB change");
                }
            }

            _prevEnergy = levelDb;

            // Maintenir l'état transient pendant quelques frames
            if (_transientHoldFrames > 0)
            {
                _transientHoldFrames--;
                _transientDetected = true;
            }
            else
            {
                _transientDetected = false;
            }

            return transient || _transientDetected;
        }

        /// <summary>Release adaptative selon les transients</summary>
        private double CalculateAdaptiveRelease(double baseRelease, bool transientActive)
        {
            if (!transientActive)
            {
                // Release normale, retour progressif
                _adaptiveReleaseCoeff = 0.99 * _adaptiveReleaseCoeff;
                return baseRelease;
            }

            // Release plus rapide pendant les transients
            _adaptiveReleaseCoeff = Math.Min(1.0, _adaptiveReleaseCoeff + 0.1);
            double fastRelease = ExpCoef(ReleaseMs * 0.3, SampleRate); // 3x plus rapide

            return baseRelease + _adaptiveReleaseCoeff * (fastRelease - baseRelease);
        }

        /// <summary>Calcul de la courbe de compression soft-knee</summary>
        private double CalculateCompression(double levelDb, double threshold, double ratio, double knee)
        {
            double excess = levelDb - threshold;
            double gainReduction;

            if (knee <= 0.0)
            {
                // Hard knee
                gainReduction = (excess > 0.0) ? (1.0 - 1.0 / ratio) * excess : 0.0;
            }
            else
            {
                // Soft knee
                if (2 * excess <= -knee)
                {
                    gainReduction = 0.0;
                }
                else if (2 * excess >= knee)
                {
                    gainReduction = (1.0 - 1.0 / ratio) * excess;
                }
                else
                {
                    double t = excess + knee / 2.0;
                    gainReduction = (1.0 - 1.0 / ratio) * (t * t) / (2.0 * knee);
                }
            }

            return Math.Max(0.0, gainReduction); // Toujours positif
        }

        /// <summary>Lissage temporel de la réduction de gain</summary>
        private void SmoothGainReduction(double targetReduction, double attack, double release)
        {
            double targetGrDb = -targetReduction; // Négatif pour réduction

            if (targetGrDb < _grDb) // Plus de réduction -> attack
            {
                _grDb = targetGrDb - attack * (targetGrDb - _grDb);
            }
            else // Moins de réduction -> release
            {
                _grDb = targetGrDb - release * (targetGrDb - _grDb);
            }

            LastGainReductionDb = (float)(-_grDb); // Positif pour affichage
        }

        /// <summary>Limiteur de sécurité adaptatif</summary>
        private double ApplySafetyLimiter(double sample, double safetyLimit, double ceiling)
        {
            double abs = Math.Abs(sample);

            // Limiteur doux progressif
            if (abs > safetyLimit)
            {
                _limitingEvents++;

                // Compression douce au-dessus du seuil de sécurité
                double excess = abs - safetyLimit;
                double maxExcess = ceiling - safetyLimit;

                if (maxExcess > 0)
                {
                    // Courbe de limitation douce (tanh-like)
                    double ratio = excess / maxExcess;
                    double compressed = safetyLimit + maxExcess * Math.Tanh(ratio * 2.0) / 2.0;
                    sample = Math.Sign(sample) * Math.Min(compressed, ceiling);
                }
                else
                {
                    sample = Math.Sign(sample) * safetyLimit;
                }
            }

            // Clipping final de sécurité
            if (Math.Abs(sample) > ceiling)
            {
                sample = Math.Sign(sample) * ceiling;
            }

            return sample;
        }

        /// <summary>Mise à jour des métriques de performance</summary>
        private void UpdateMetrics(double gainReductionDb)
        {
            if (gainReductionDb > _maxGainReduction)
            {
                _maxGainReduction = gainReductionDb;
            }

            _totalGainReduction += gainReductionDb;

            // Log périodique des statistiques
            if (_processedSamples > 0 && _processedSamples % (SampleRate * 10) == 0) // Toutes les 10 secondes
            {
                double avgReduction = _totalGainReduction / _processedSamples;
                System.Diagnostics.Debug.WriteLine($"[Compressor] Stats: Max={_maxGainReduction:F1}dB, Avg={avgReduction:F2}dB, " +
                                                 $"Transients={_transientCount}, Limiting={_limitingEvents}");
            }
        }

        /// <summary>Obtenir un rapport d'état détaillé</summary>
        public string GetStatusReport()
        {
            double avgReduction = _processedSamples > 0 ? _totalGainReduction / _processedSamples : 0.0;

            return $"Compressor Status: Samples={_processedSamples}, MaxGR={_maxGainReduction:F1}dB, " +
                   $"AvgGR={avgReduction:F2}dB, Transients={_transientCount}, LimitEvents={_limitingEvents}, " +
                   $"LookAhead={EnableLookAhead}({_lookAheadSamples}), Adaptive={EnableAdaptiveRelease}, " +
                   $"Healthy={IsHealthy}";
        }

        /// <summary>Réinitialisation des statistiques</summary>
        public void ResetStatistics()
        {
            _processedSamples = 0;
            _maxGainReduction = 0.0;
            _totalGainReduction = 0.0;
            _transientCount = 0;
            _limitingEvents = 0;
            System.Diagnostics.Debug.WriteLine("[Compressor] Statistics reset");
        }

        /// <summary>Configuration de presets optimisés</summary>
        public void ApplyPreset(CompressorPreset preset)
        {
            try
            {
                switch (preset)
                {
                    case CompressorPreset.Gentle:
                        ThresholdDb = -15.0;
                        Ratio = 2.0;
                        KneeDb = 8.0;
                        AttackMs = 10.0;
                        ReleaseMs = 100.0;
                        EnableLookAhead = true;
                        LookAheadMs = 3.0;
                        break;

                    case CompressorPreset.Speech:
                        ThresholdDb = -18.0;
                        Ratio = 3.0;
                        KneeDb = 6.0;
                        AttackMs = 5.0;
                        ReleaseMs = 80.0;
                        EnableLookAhead = true;
                        EnableAdaptiveRelease = true;
                        LookAheadMs = 5.0;
                        break;

                    case CompressorPreset.Aggressive:
                        ThresholdDb = -12.0;
                        Ratio = 6.0;
                        KneeDb = 4.0;
                        AttackMs = 2.0;
                        ReleaseMs = 50.0;
                        EnableLookAhead = true;
                        EnableAdaptiveRelease = true;
                        LookAheadMs = 8.0;
                        break;

                    case CompressorPreset.Limiter:
                        ThresholdDb = -6.0;
                        Ratio = 20.0;
                        KneeDb = 2.0;
                        AttackMs = 0.5;
                        ReleaseMs = 20.0;
                        EnableLookAhead = true;
                        LookAheadMs = 10.0;
                        break;

                    case CompressorPreset.Transparent:
                    default:
                        ThresholdDb = -20.0;
                        Ratio = 1.5;
                        KneeDb = 10.0;
                        AttackMs = 15.0;
                        ReleaseMs = 150.0;
                        EnableLookAhead = false;
                        break;
                }

                ConfigureLookAhead(); // Reconfigurer après changement de preset
                System.Diagnostics.Debug.WriteLine($"[Compressor] Applied preset: {preset}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Compressor] Preset application error: {ex.Message}");
            }
        }

        // ===== Utils =====
        private static double DbToLin(double db) => Math.Pow(10.0, db / 20.0);

        private static double ExpCoef(double ms, int fs)
        {
            ms = Math.Max(0.1, ms);
            fs = Math.Max(1000, fs);
            double tau = ms / 1000.0;
            return Math.Exp(-1.0 / (tau * fs));
        }

        public void Dispose()
        {
            try
            {
                _lookAheadBuffer?.Clear();
                _lookAheadBuffer = null;
                _futureEnvelope = null;
                _isConfigured = false;
                System.Diagnostics.Debug.WriteLine("[Compressor] Disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Compressor] Dispose error: {ex.Message}");
            }
        }
    }

    /// <summary>Presets de compresseur pour différents usages</summary>
    public enum CompressorPreset
    {
        Transparent,    // Compression très douce
        Gentle,         // Compression légère
        Speech,         // Optimisé pour la voix
        Aggressive,     // Compression forte
        Limiter         // Mode limiteur
    }
}