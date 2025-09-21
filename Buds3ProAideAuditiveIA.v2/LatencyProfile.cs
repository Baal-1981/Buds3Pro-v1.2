using System;
using System.Collections.Generic;
using System.Linq;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Profils de latence pour v1.3 - optimisation intelligente des buffers audio
    /// </summary>
    public enum LatencyProfile
    {
        Ultra,    // 5ms, buffer minimal - pour gaming/VoIP
        Fast,     // 10ms, équilibré - usage général optimisé
        Balanced, // 20ms, plus stable - défaut recommandé
        Safe      // 40ms, maximum stabilité - environnements difficiles
    }

    /// <summary>
    /// Configuration détaillée pour chaque profil de latence
    /// </summary>
    public struct LatencyProfileConfig
    {
        public int FrameMs;           // Taille de frame en ms
        public int BufferMultiplier;  // Multiplicateur de buffer (1-4x)
        public bool EnableLookAhead;  // Look-ahead pour compresseur
        public double LookAheadMs;    // Durée du look-ahead
        public bool AdaptiveRelease;  // Release adaptative
        public int MaxCpuPercent;     // Limite CPU recommandée
        public string Description;    // Description du profil
        public bool AllowAutoRestart; // Auto-restart en cas de problème
        public int MaxErrors;         // Seuil d'erreurs avant fallback

        // Métriques de qualité attendues
        public int ExpectedLatencyMs;
        public int ExpectedUnderruns;
        public double StabilityScore; // 0.0-1.0
    }

    /// <summary>
    /// Gestionnaire de profils de latence avec adaptation intelligente
    /// </summary>
    public class LatencyProfileManager
    {
        private static readonly Dictionary<LatencyProfile, LatencyProfileConfig> _profiles =
            new Dictionary<LatencyProfile, LatencyProfileConfig>
            {
                [LatencyProfile.Ultra] = new LatencyProfileConfig
                {
                    FrameMs = 5,
                    BufferMultiplier = 1,
                    EnableLookAhead = false, // Minimum latence
                    LookAheadMs = 0,
                    AdaptiveRelease = false, // Simplify processing
                    MaxCpuPercent = 90,
                    Description = "Ultra-faible latence (5ms) - Gaming/VoIP",
                    AllowAutoRestart = true,
                    MaxErrors = 2, // Tolérance faible
                    ExpectedLatencyMs = 8,
                    ExpectedUnderruns = 5,
                    StabilityScore = 0.6
                },

                [LatencyProfile.Fast] = new LatencyProfileConfig
                {
                    FrameMs = 10,
                    BufferMultiplier = 2,
                    EnableLookAhead = true,
                    LookAheadMs = 3.0,
                    AdaptiveRelease = true,
                    MaxCpuPercent = 75,
                    Description = "Latence rapide (10ms) - Usage général optimisé",
                    AllowAutoRestart = true,
                    MaxErrors = 3,
                    ExpectedLatencyMs = 15,
                    ExpectedUnderruns = 2,
                    StabilityScore = 0.8
                },

                [LatencyProfile.Balanced] = new LatencyProfileConfig
                {
                    FrameMs = 20,
                    BufferMultiplier = 3,
                    EnableLookAhead = true,
                    LookAheadMs = 5.0,
                    AdaptiveRelease = true,
                    MaxCpuPercent = 60,
                    Description = "Équilibré (20ms) - Défaut recommandé",
                    AllowAutoRestart = true,
                    MaxErrors = 5,
                    ExpectedLatencyMs = 30,
                    ExpectedUnderruns = 1,
                    StabilityScore = 0.9
                },

                [LatencyProfile.Safe] = new LatencyProfileConfig
                {
                    FrameMs = 40,
                    BufferMultiplier = 4,
                    EnableLookAhead = true,
                    LookAheadMs = 10.0,
                    AdaptiveRelease = true,
                    MaxCpuPercent = 45,
                    Description = "Maximum stabilité (40ms) - Environnements difficiles",
                    AllowAutoRestart = false, // Priorité à la stabilité
                    MaxErrors = 10,
                    ExpectedLatencyMs = 60,
                    ExpectedUnderruns = 0,
                    StabilityScore = 0.95
                }
            };

        private LatencyProfile _currentProfile = LatencyProfile.Balanced;
        private LatencyProfile _requestedProfile = LatencyProfile.Balanced;
        private bool _autoAdaptationEnabled = true;
        private DateTime _lastAdaptation = DateTime.MinValue;
        private readonly Queue<double> _performanceHistory = new Queue<double>(20);

        // Métriques pour auto-adaptation
        private int _underrunCount = 0;
        private int _errorCount = 0;
        private double _avgCpuUsage = 0.0;
        private double _stabilityScore = 1.0;
        private bool _adaptationInProgress = false;

        public LatencyProfile CurrentProfile => _currentProfile;
        public LatencyProfileConfig CurrentConfig => GetConfig(_currentProfile);
        public bool AutoAdaptationEnabled
        {
            get => _autoAdaptationEnabled;
            set => _autoAdaptationEnabled = value;
        }

        public event Action<LatencyProfile, LatencyProfile> ProfileChanged;
        public event Action<string> AdaptationMessage;

        /// <summary>
        /// Obtient la configuration d'un profil
        /// </summary>
        public static LatencyProfileConfig GetConfig(LatencyProfile profile)
        {
            return _profiles.TryGetValue(profile, out var config) ? config : _profiles[LatencyProfile.Balanced];
        }

        /// <summary>
        /// Obtient tous les profils disponibles
        /// </summary>
        public static LatencyProfile[] GetAllProfiles()
        {
            return new[] { LatencyProfile.Ultra, LatencyProfile.Fast, LatencyProfile.Balanced, LatencyProfile.Safe };
        }

        /// <summary>
        /// Demande un changement de profil (sera appliqué au prochain restart audio)
        /// </summary>
        public void RequestProfile(LatencyProfile profile)
        {
            _requestedProfile = profile;
            LogMessage($"Profile change requested: {_currentProfile} → {profile}");
        }

        /// <summary>
        /// Applique le profil demandé (appelé lors du restart audio)
        /// </summary>
        public bool ApplyRequestedProfile()
        {
            if (_requestedProfile != _currentProfile)
            {
                var oldProfile = _currentProfile;
                _currentProfile = _requestedProfile;

                // Reset des métriques pour le nouveau profil
                _underrunCount = 0;
                _errorCount = 0;
                _performanceHistory.Clear();

                ProfileChanged?.Invoke(oldProfile, _currentProfile);
                LogMessage($"Profile applied: {oldProfile} → {_currentProfile}");

                return true;
            }
            return false;
        }

        /// <summary>
        /// Met à jour les métriques de performance pour auto-adaptation
        /// </summary>
        public void UpdateMetrics(AudioHealthMetrics metrics)
        {
            if (_adaptationInProgress) return;

            try
            {
                // Mise à jour des compteurs
                if (metrics.BufferUnderrunsCount > _underrunCount)
                {
                    _underrunCount = metrics.BufferUnderrunsCount;
                }

                _avgCpuUsage = metrics.CpuUsagePercent;
                _stabilityScore = metrics.StabilityScore;

                // Historique de performance
                double performanceScore = CalculatePerformanceScore(metrics);
                _performanceHistory.Enqueue(performanceScore);
                if (_performanceHistory.Count > 20) _performanceHistory.Dequeue();

                // Auto-adaptation si activée
                if (_autoAdaptationEnabled && ShouldAdapt())
                {
                    PerformAdaptation(metrics);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error updating metrics: {ex.Message}");
            }
        }

        /// <summary>
        /// Calcule un score de performance global (0.0-1.0)
        /// </summary>
        private double CalculatePerformanceScore(AudioHealthMetrics metrics)
        {
            var config = CurrentConfig;
            double score = 1.0;

            // Pénalités pour problèmes
            if (metrics.BufferUnderrunsCount > config.ExpectedUnderruns)
                score -= 0.2 * (metrics.BufferUnderrunsCount - config.ExpectedUnderruns);

            if (metrics.CpuUsagePercent > config.MaxCpuPercent)
                score -= 0.3 * ((metrics.CpuUsagePercent - config.MaxCpuPercent) / 100.0);

            if (metrics.LatencyMs > config.ExpectedLatencyMs * 1.5)
                score -= 0.2;

            // Bonus pour stabilité
            if (metrics.IsStable) score += 0.1;
            if (metrics.IsHealthy) score += 0.1;

            return Math.Max(0.0, Math.Min(1.0, score));
        }

        /// <summary>
        /// Détermine si une adaptation est nécessaire
        /// </summary>
        private bool ShouldAdapt()
        {
            // Cooldown entre adaptations
            if ((DateTime.Now - _lastAdaptation).TotalSeconds < 30) return false;

            // Besoin de données suffisantes
            if (_performanceHistory.Count < 10) return false;

            // Score de performance moyen récent
            double avgScore = 0;
            foreach (var score in _performanceHistory)
                avgScore += score;
            avgScore /= _performanceHistory.Count;

            // Adaptation si performance dégradée ou excellente
            return avgScore < 0.6 || avgScore > 0.9;
        }

        /// <summary>
        /// Effectue une adaptation intelligente du profil
        /// </summary>
        private void PerformAdaptation(AudioHealthMetrics metrics)
        {
            _adaptationInProgress = true;
            _lastAdaptation = DateTime.Now;

            try
            {
                var avgScore = _performanceHistory.Average();
                var currentProfile = _currentProfile;
                LatencyProfile? newProfile = null;

                // Logique d'adaptation
                if (avgScore < 0.6) // Performance dégradée
                {
                    // Passer à un profil plus conservateur
                    switch (currentProfile)
                    {
                        case LatencyProfile.Ultra:
                            newProfile = LatencyProfile.Fast;
                            break;
                        case LatencyProfile.Fast:
                            newProfile = LatencyProfile.Balanced;
                            break;
                        case LatencyProfile.Balanced:
                            newProfile = LatencyProfile.Safe;
                            break;
                            // Safe reste Safe
                    }

                    if (newProfile.HasValue)
                    {
                        LogMessage($"Auto-adaptation: Performance faible ({avgScore:F2}) → {newProfile}");
                    }
                }
                else if (avgScore > 0.9 && _currentProfile != LatencyProfile.Ultra) // Excellente performance
                {
                    // Tenter un profil plus agressif
                    switch (currentProfile)
                    {
                        case LatencyProfile.Safe:
                            newProfile = LatencyProfile.Balanced;
                            break;
                        case LatencyProfile.Balanced:
                            newProfile = LatencyProfile.Fast;
                            break;
                        case LatencyProfile.Fast:
                            newProfile = LatencyProfile.Ultra;
                            break;
                    }

                    if (newProfile.HasValue)
                    {
                        LogMessage($"Auto-adaptation: Excellente performance ({avgScore:F2}) → {newProfile}");
                    }
                }

                if (newProfile.HasValue)
                {
                    RequestProfile(newProfile.Value);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error during adaptation: {ex.Message}");
            }
            finally
            {
                _adaptationInProgress = false;
            }
        }

        /// <summary>
        /// Recommande le meilleur profil pour l'appareil actuel
        /// </summary>
        public LatencyProfile RecommendProfile()
        {
            try
            {
                // Facteurs à considérer : CPU, mémoire, historique de performance
                long totalMemory = Java.Lang.Runtime.GetRuntime().TotalMemory();
                long freeMemory = Java.Lang.Runtime.GetRuntime().FreeMemory();
                double memoryPressure = (double)(totalMemory - freeMemory) / totalMemory;

                // Recommandation basée sur les ressources système
                if (memoryPressure > 0.8)
                {
                    return LatencyProfile.Safe; // Système sous pression
                }
                else if (memoryPressure > 0.6)
                {
                    return LatencyProfile.Balanced; // Ressources modérées
                }
                else if (_performanceHistory.Count > 5 && _performanceHistory.Average() > 0.8)
                {
                    return LatencyProfile.Fast; // Bon historique
                }
                else
                {
                    return LatencyProfile.Balanced; // Défaut sûr
                }
            }
            catch
            {
                return LatencyProfile.Balanced; // Fallback sûr
            }
        }

        /// <summary>
        /// Obtient un rapport d'état détaillé
        /// </summary>
        public string GetStatusReport()
        {
            var config = CurrentConfig;
            var avgPerf = _performanceHistory.Count > 0 ? _performanceHistory.Average() : 0.0;

            return $"LatencyProfile Status: Current={_currentProfile}, Requested={_requestedProfile}, " +
                   $"Frame={config.FrameMs}ms, Buffer={config.BufferMultiplier}x, " +
                   $"AutoAdapt={_autoAdaptationEnabled}, Underruns={_underrunCount}, " +
                   $"AvgPerf={avgPerf:F2}, CPU={_avgCpuUsage:F1}%, Stability={_stabilityScore:F2}";
        }

        /// <summary>
        /// Reset complet du gestionnaire
        /// </summary>
        public void Reset()
        {
            _underrunCount = 0;
            _errorCount = 0;
            _avgCpuUsage = 0.0;
            _stabilityScore = 1.0;
            _performanceHistory.Clear();
            _adaptationInProgress = false;

            LogMessage("LatencyProfileManager reset");
        }

        private void LogMessage(string message)
        {
            try
            {
                AdaptationMessage?.Invoke(message);
                System.Diagnostics.Debug.WriteLine($"[LatencyProfile] {message}");
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }

    /// <summary>
    /// Configuration factory pour les profils de latence
    /// </summary>
    public static class LatencyProfileFactory
    {
        /// <summary>
        /// Crée une configuration audio optimisée pour le profil donné
        /// </summary>
        public static (int sampleRate, int frameMs, int bufferSize) CreateAudioConfig(
            LatencyProfile profile,
            int preferredSampleRate = 48000)
        {
            var config = LatencyProfileManager.GetConfig(profile);

            // Optimisation du sample rate selon le profil
            int sampleRate = profile switch
            {
                LatencyProfile.Ultra => Math.Min(preferredSampleRate, 44100), // Limiter pour ultra-low latency
                LatencyProfile.Safe => preferredSampleRate, // Full quality pour stabilité
                _ => preferredSampleRate
            };

            // Calcul de la taille de buffer optimale
            int frameSize = sampleRate * config.FrameMs / 1000;
            int bufferSize = frameSize * config.BufferMultiplier;

            return (sampleRate, config.FrameMs, bufferSize);
        }

        /// <summary>
        /// Crée une configuration DSP optimisée pour le profil
        /// </summary>
        public static (bool enableLookAhead, double lookAheadMs, bool adaptiveRelease) CreateDspConfig(LatencyProfile profile)
        {
            var config = LatencyProfileManager.GetConfig(profile);
            return (config.EnableLookAhead, config.LookAheadMs, config.AdaptiveRelease);
        }
    }
}