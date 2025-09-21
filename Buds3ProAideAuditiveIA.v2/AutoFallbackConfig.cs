using System;
using System.Collections.Generic;
using System.Linq;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Configuration pour l'auto-fallback intelligent des transports Bluetooth
    /// </summary>
    public sealed class AutoFallbackConfig
    {
        // ===== Default Fallback Sequences =====
        private readonly Dictionary<AudioTransport, List<AudioTransport>> _defaultSequences;
        private readonly Dictionary<string, List<AudioTransport>> _deviceSpecificSequences;

        // ===== Timing Configuration =====
        public int ConnectionTimeoutMs { get; set; } = 4000;
        public int RetryDelayMs { get; set; } = 1000;
        public int MaxRetries { get; set; } = 3;
        public int CooldownPeriodMs { get; set; } = 5000;

        // ===== Quality Thresholds =====
        public ConnectionQuality MinimumQuality { get; set; } = ConnectionQuality.Fair;
        public double MaxAcceptableLatency { get; set; } = 150.0; // milliseconds
        public double MaxAcceptablePacketLoss { get; set; } = 0.1; // 10%

        // ===== Device Type Preferences =====
        private readonly Dictionary<string, AudioTransport> _deviceTypePreferences;

        public AutoFallbackConfig()
        {
            _defaultSequences = BuildDefaultSequences();
            _deviceTypePreferences = BuildDeviceTypePreferences();
            _deviceSpecificSequences = new Dictionary<string, List<AudioTransport>>();
        }

        /// <summary>
        /// Initialise les séquences de fallback par défaut
        /// </summary>
        private Dictionary<AudioTransport, List<AudioTransport>> BuildDefaultSequences()
        {
            var dict = new Dictionary<AudioTransport, List<AudioTransport>>
            {
                // A2DP priority: High quality → Low latency → Compatibility
                [AudioTransport.A2DP] = new List<AudioTransport>
                {
                    AudioTransport.A2DP,
                    AudioTransport.LE_LC3_AUTO,
                    AudioTransport.SCO
                },

                // SCO priority: Low latency → Compatibility → Quality
                [AudioTransport.SCO] = new List<AudioTransport>
                {
                    AudioTransport.SCO,
                    AudioTransport.LE_LC3_AUTO,
                    AudioTransport.A2DP
                },

                // LE Audio priority: Modern → Fallback to legacy
                [AudioTransport.LE_LC3_AUTO] = new List<AudioTransport>
                {
                    AudioTransport.LE_LC3_AUTO,
                    AudioTransport.A2DP,
                    AudioTransport.SCO
                }
            };
            return dict;
        }

        /// <summary>
        /// Initialise les préférences par type d'appareil
        /// </summary>
        private Dictionary<string, AudioTransport> BuildDeviceTypePreferences()
        {
            var dict2 = new Dictionary<string, AudioTransport>(StringComparer.OrdinalIgnoreCase)
            {
                // Gaming headsets - prioritize low latency
                ["gaming"] = AudioTransport.SCO,
                ["game"] = AudioTransport.SCO,
                ["razer"] = AudioTransport.SCO,
                ["steelseries"] = AudioTransport.SCO,

                // Music headphones - prioritize quality
                ["music"] = AudioTransport.A2DP,
                ["audiophile"] = AudioTransport.A2DP,
                ["sony"] = AudioTransport.A2DP,
                ["bose"] = AudioTransport.A2DP,
                ["sennheiser"] = AudioTransport.A2DP,

                // Modern devices - try LE Audio first
                ["buds3"] = AudioTransport.LE_LC3_AUTO,
                ["airpods"] = AudioTransport.LE_LC3_AUTO,
                ["galaxy buds"] = AudioTransport.LE_LC3_AUTO,
                ["pixel buds"] = AudioTransport.LE_LC3_AUTO,

                // Communication devices - prioritize voice quality
                ["headset"] = AudioTransport.SCO,
                ["jabra"] = AudioTransport.SCO,
                ["plantronics"] = AudioTransport.SCO,
                ["poly"] = AudioTransport.SCO
            };
            return dict2;
        }

        /// <summary>
        /// Obtient la séquence de fallback pour un transport donné
        /// </summary>
        public List<AudioTransport> GetDefaultSequence(AudioTransport requested)
        {
            if (_defaultSequences.TryGetValue(requested, out var sequence))
            {
                return new List<AudioTransport>(sequence);
            }

            // Fallback sequence if not found
            return new List<AudioTransport> { requested, AudioTransport.A2DP, AudioTransport.SCO };
        }

        /// <summary>
        /// Obtient la séquence optimisée pour un appareil spécifique
        /// </summary>
        public List<AudioTransport> GetSequenceForDevice(string deviceName, AudioTransport requested)
        {
            // Check device-specific overrides first
            if (!string.IsNullOrEmpty(deviceName) &&
                _deviceSpecificSequences.TryGetValue(deviceName, out var deviceSequence))
            {
                return new List<AudioTransport>(deviceSequence);
            }

            // Check device type preferences
            var preferredTransport = GetPreferredTransportForDevice(deviceName);
            if (preferredTransport.HasValue && preferredTransport != requested)
            {
                var sequence = GetDefaultSequence(preferredTransport.Value);

                // Ensure requested transport is in the sequence
                if (!sequence.Contains(requested))
                {
                    sequence.Insert(1, requested);
                }

                return sequence;
            }

            // Return default sequence
            return GetDefaultSequence(requested);
        }

        /// <summary>
        /// Détermine le transport préféré basé sur le nom de l'appareil
        /// </summary>
        public AudioTransport? GetPreferredTransportForDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return null;

            var lowerDeviceName = deviceName.ToLowerInvariant();

            foreach (var preference in _deviceTypePreferences)
            {
                if (lowerDeviceName.Contains(preference.Key))
                {
                    return preference.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Ajoute une séquence personnalisée pour un appareil
        /// </summary>
        public void SetDeviceSequence(string deviceName, List<AudioTransport> sequence)
        {
            if (string.IsNullOrEmpty(deviceName) || sequence == null || !sequence.Any())
                return;

            _deviceSpecificSequences[deviceName] = new List<AudioTransport>(sequence);
        }

        /// <summary>
        /// Supprime la séquence personnalisée d'un appareil
        /// </summary>
        public void RemoveDeviceSequence(string deviceName)
        {
            if (!string.IsNullOrEmpty(deviceName))
            {
                _deviceSpecificSequences.Remove(deviceName);
            }
        }

        /// <summary>
        /// Vérifie si un transport répond aux critères de qualité minimum
        /// </summary>
        public bool MeetsQualityThreshold(ConnectionQuality quality, double latency, double packetLoss)
        {
            return quality >= MinimumQuality &&
                   latency <= MaxAcceptableLatency &&
                   packetLoss <= MaxAcceptablePacketLoss;
        }

        /// <summary>
        /// Calcule le score de qualité pour prioriser les transports
        /// </summary>
        public double CalculateQualityScore(ConnectionQuality quality, double latency, double packetLoss)
        {
            double score = 0;

            // Quality contribution (0-40 points)
            score += (int)quality * 10;

            // Latency contribution (0-30 points)
            if (latency <= 50) score += 30;
            else if (latency <= 100) score += 20;
            else if (latency <= 150) score += 10;

            // Packet loss contribution (0-30 points)
            if (packetLoss <= 0.01) score += 30; // <1%
            else if (packetLoss <= 0.05) score += 20; // <5%
            else if (packetLoss <= 0.1) score += 10; // <10%

            return score;
        }

        /// <summary>
        /// Crée une configuration optimisée pour un profil d'usage
        /// </summary>
        public static AutoFallbackConfig CreateForProfile(UsageProfile profile)
        {
            var config = new AutoFallbackConfig();

            switch (profile)
            {
                case UsageProfile.Gaming:
                    config.MaxAcceptableLatency = 80.0;
                    config.MinimumQuality = ConnectionQuality.Good;
                    config.ConnectionTimeoutMs = 3000;
                    break;

                case UsageProfile.Music:
                    config.MaxAcceptableLatency = 200.0;
                    config.MaxAcceptablePacketLoss = 0.02; // Very low tolerance
                    config.MinimumQuality = ConnectionQuality.Good;
                    break;

                case UsageProfile.Voice:
                    config.MaxAcceptableLatency = 100.0;
                    config.MinimumQuality = ConnectionQuality.Fair;
                    config.ConnectionTimeoutMs = 5000;
                    break;

                case UsageProfile.General:
                default:
                    // Use default settings
                    break;
            }

            return config;
        }

        /// <summary>
        /// Obtient un rapport de configuration
        /// </summary>
        public string GetConfigurationReport()
        {
            return $"AutoFallback Config: " +
                   $"MinQuality={MinimumQuality}, " +
                   $"MaxLatency={MaxAcceptableLatency}ms, " +
                   $"MaxPacketLoss={MaxAcceptablePacketLoss:P1}, " +
                   $"Timeout={ConnectionTimeoutMs}ms, " +
                   $"Retries={MaxRetries}, " +
                   $"DevicePreferences={_deviceTypePreferences.Count}, " +
                   $"CustomSequences={_deviceSpecificSequences.Count}";
        }
    }

    /// <summary>
    /// Profils d'usage pour optimiser la configuration
    /// </summary>
    public enum UsageProfile
    {
        General,    // Equilibré pour usage général
        Gaming,     // Optimisé latence
        Music,      // Optimisé qualité audio
        Voice       // Optimisé communication vocale
    }
}