using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Android.Bluetooth;
using Android.Content;
using Newtonsoft.Json;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Gestionnaire de profils d'appareils Bluetooth avec mémorisation des préférences
    /// </summary>
    public sealed class DeviceProfileManager : IDisposable
    {
        private readonly Context _context;
        private readonly string _profilesDirectory;
        private readonly Dictionary<string, DeviceProfile> _profiles;
        private readonly object _lockObject = new object();

        private readonly JsonSerializerSettings _jsonSettings;
        private bool _isDisposed = false;

        public DeviceProfileManager(Context context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));

            // Setup profiles directory
            _profilesDirectory = Path.Combine(
                context.GetExternalFilesDir("bluetooth_profiles")?.AbsolutePath ??
                context.FilesDir.AbsolutePath,
                "device_profiles");

            Directory.CreateDirectory(_profilesDirectory);

            _profiles = new Dictionary<string, DeviceProfile>();

            // Configure JSON serialization
            _jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                NullValueHandling = NullValueHandling.Ignore
            };

            LoadAllProfiles();

            System.Diagnostics.Debug.WriteLine($"[DeviceProfileManager] Initialized with {_profiles.Count} profiles");
        }

        /// <summary>
        /// Obtient ou crée un profil pour l'appareil spécifié
        /// </summary>
        public DeviceProfile GetDeviceProfile(BluetoothDevice device)
        {
            if (device == null) return null;

            lock (_lockObject)
            {
                if (_profiles.TryGetValue(device.Address, out var profile))
                {
                    return profile;
                }

                return null;
            }
        }

        /// <summary>
        /// Assure qu'un profil existe pour l'appareil donné
        /// </summary>
        public DeviceProfile EnsureDeviceProfile(BluetoothDevice device)
        {
            if (device == null) return null;

            lock (_lockObject)
            {
                if (_profiles.TryGetValue(device.Address, out var existingProfile))
                {
                    return existingProfile;
                }

                // Create new profile
                var newProfile = new DeviceProfile
                {
                    DeviceAddress = device.Address,
                    DeviceName = device.Name ?? "Unknown Device",
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow,
                    PreferredTransports = new List<AudioTransport> { AudioTransport.A2DP }, // Default
                    QualityHistory = new List<QualityRecord>(),
                    ConnectionCount = 0,
                    TotalConnectionTime = TimeSpan.Zero
                };

                _profiles[device.Address] = newProfile;
                SaveProfile(newProfile);

                System.Diagnostics.Debug.WriteLine($"[DeviceProfileManager] Created profile for: {newProfile.DeviceName}");
                return newProfile;
            }
        }

        /// <summary>
        /// Met à jour la préférence de transport pour un appareil
        /// </summary>
        public void UpdateDevicePreference(BluetoothDevice device, AudioTransport transport)
        {
            if (device == null) return;

            lock (_lockObject)
            {
                var profile = EnsureDeviceProfile(device);

                // Update transport preference with usage-based learning
                if (!profile.PreferredTransports.Contains(transport))
                {
                    profile.PreferredTransports.Insert(0, transport);
                }
                else
                {
                    // Move to front (most recently used)
                    profile.PreferredTransports.Remove(transport);
                    profile.PreferredTransports.Insert(0, transport);
                }

                // Keep only top 3 preferred transports
                if (profile.PreferredTransports.Count > 3)
                {
                    profile.PreferredTransports = profile.PreferredTransports.Take(3).ToList();
                }

                profile.LastSeen = DateTime.UtcNow;
                profile.LastUsedTransport = transport;

                SaveProfile(profile);

                System.Diagnostics.Debug.WriteLine(
                    $"[DeviceProfileManager] Updated {profile.DeviceName} preference: {transport}");
            }
        }

        /// <summary>
        /// Met à jour les statistiques de connexion
        /// </summary>
        public void UpdateConnectionStats(BluetoothDevice device, TimeSpan connectionDuration, bool successful)
        {
            if (device == null) return;

            lock (_lockObject)
            {
                var profile = EnsureDeviceProfile(device);

                profile.ConnectionCount++;
                if (successful)
                {
                    profile.SuccessfulConnections++;
                    profile.TotalConnectionTime = profile.TotalConnectionTime.Add(connectionDuration);
                }
                else
                {
                    profile.FailedConnections++;
                }

                profile.LastSeen = DateTime.UtcNow;
                profile.ConnectionSuccessRate = profile.ConnectionCount > 0 ?
                    (double)profile.SuccessfulConnections / profile.ConnectionCount : 0.0;

                SaveProfile(profile);
            }
        }

        /// <summary>
        /// Enregistre les métriques de qualité pour analyse
        /// </summary>
        public void RecordQualityMetrics(BluetoothDevice device, ConnectionQuality quality,
            AudioTransport transport, double latency, double packetLoss)
        {
            if (device == null) return;

            lock (_lockObject)
            {
                var profile = EnsureDeviceProfile(device);

                var qualityRecord = new QualityRecord
                {
                    Timestamp = DateTime.UtcNow,
                    Quality = quality,
                    Transport = transport,
                    Latency = latency,
                    PacketLossRate = packetLoss
                };

                profile.QualityHistory.Add(qualityRecord);

                // Maintain rolling window of quality records
                const int maxQualityRecords = 100;
                if (profile.QualityHistory.Count > maxQualityRecords)
                {
                    profile.QualityHistory = profile.QualityHistory
                        .OrderByDescending(r => r.Timestamp)
                        .Take(maxQualityRecords)
                        .ToList();
                }

                // Update average quality metrics
                UpdateAverageQualityMetrics(profile);

                SaveProfile(profile);
            }
        }

        /// <summary>
        /// Met à jour les métriques moyennes de qualité
        /// </summary>
        private void UpdateAverageQualityMetrics(DeviceProfile profile)
        {
            if (!profile.QualityHistory.Any()) return;

            var recentRecords = profile.QualityHistory
                .Where(r => (DateTime.UtcNow - r.Timestamp).TotalDays <= 7) // Last 7 days
                .ToList();

            if (recentRecords.Any())
            {
                profile.AverageLatency = recentRecords.Average(r => r.Latency);
                profile.AveragePacketLoss = recentRecords.Average(r => r.PacketLossRate);

                // Calculate quality distribution
                var qualityCounts = recentRecords.GroupBy(r => r.Quality)
                    .ToDictionary(g => g.Key, g => g.Count());

                profile.QualityDistribution = qualityCounts;

                // Determine most reliable transport
                var transportQuality = recentRecords
                    .GroupBy(r => r.Transport)
                    .ToDictionary(g => g.Key, g => g.Average(r => (int)r.Quality));

                if (transportQuality.Any())
                {
                    profile.MostReliableTransport = transportQuality
                        .OrderByDescending(kv => kv.Value)
                        .First().Key;
                }
            }
        }

        /// <summary>
        /// Obtient les recommandations pour un appareil
        /// </summary>
        public DeviceRecommendations GetRecommendations(BluetoothDevice device)
        {
            if (device == null) return null;

            lock (_lockObject)
            {
                var profile = GetDeviceProfile(device);
                if (profile == null) return null;

                var recommendations = new DeviceRecommendations
                {
                    DeviceAddress = device.Address,
                    RecommendedTransport = profile.PreferredTransports.FirstOrDefault(),
                    AlternativeTransports = profile.PreferredTransports.Skip(1).ToList(),
                    ExpectedLatency = profile.AverageLatency,
                    ExpectedPacketLoss = profile.AveragePacketLoss,
                    ReliabilityScore = profile.ConnectionSuccessRate,
                    Confidence = CalculateConfidence(profile)
                };

                // Add transport-specific recommendations
                foreach (var transport in profile.PreferredTransports.Take(3))
                {
                    var transportRecords = profile.QualityHistory
                        .Where(r => r.Transport == transport &&
                               (DateTime.UtcNow - r.Timestamp).TotalDays <= 7)
                        .ToList();

                    if (transportRecords.Any())
                    {
                        var transportRec = new TransportRecommendation
                        {
                            Transport = transport,
                            AverageLatency = transportRecords.Average(r => r.Latency),
                            AveragePacketLoss = transportRecords.Average(r => r.PacketLossRate),
                            SuccessRate = transportRecords.Count(r => r.Quality >= ConnectionQuality.Good) /
                                         (double)transportRecords.Count,
                            UsageCount = transportRecords.Count
                        };

                        recommendations.TransportRecommendations.Add(transportRec);
                    }
                }

                return recommendations;
            }
        }

        /// <summary>
        /// Calcule le niveau de confiance des recommandations
        /// </summary>
        private double CalculateConfidence(DeviceProfile profile)
        {
            double confidence = 0.0;

            // More connections = higher confidence
            if (profile.ConnectionCount >= 10) confidence += 0.3;
            else if (profile.ConnectionCount >= 5) confidence += 0.2;
            else if (profile.ConnectionCount >= 2) confidence += 0.1;

            // Recent usage = higher confidence
            var daysSinceLastSeen = (DateTime.UtcNow - profile.LastSeen).TotalDays;
            if (daysSinceLastSeen <= 1) confidence += 0.3;
            else if (daysSinceLastSeen <= 7) confidence += 0.2;
            else if (daysSinceLastSeen <= 30) confidence += 0.1;

            // Quality data = higher confidence
            if (profile.QualityHistory.Count >= 20) confidence += 0.3;
            else if (profile.QualityHistory.Count >= 10) confidence += 0.2;
            else if (profile.QualityHistory.Count >= 5) confidence += 0.1;

            // Success rate = confidence modifier
            confidence *= profile.ConnectionSuccessRate;

            return Math.Min(1.0, confidence);
        }

        /// <summary>
        /// Sauvegarde un profil d'appareil
        /// </summary>
        private void SaveProfile(DeviceProfile profile)
        {
            try
            {
                var filePath = Path.Combine(_profilesDirectory, $"{profile.DeviceAddress}.json");
                var json = JsonConvert.SerializeObject(profile, _jsonSettings);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceProfileManager] Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Charge tous les profils depuis le stockage
        /// </summary>
        private void LoadAllProfiles()
        {
            try
            {
                if (!Directory.Exists(_profilesDirectory)) return;

                var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");

                foreach (var filePath in profileFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var profile = JsonConvert.DeserializeObject<DeviceProfile>(json, _jsonSettings);

                        if (profile != null && !string.IsNullOrEmpty(profile.DeviceAddress))
                        {
                            _profiles[profile.DeviceAddress] = profile;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[DeviceProfileManager] Failed to load profile {filePath}: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[DeviceProfileManager] Loaded {_profiles.Count} device profiles");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceProfileManager] Load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtient le nombre de profils gérés
        /// </summary>
        public int GetProfileCount()
        {
            lock (_lockObject)
            {
                return _profiles.Count;
            }
        }

        /// <summary>
        /// Obtient tous les profils d'appareils
        /// </summary>
        public DeviceProfile[] GetAllProfiles()
        {
            lock (_lockObject)
            {
                return _profiles.Values.ToArray();
            }
        }

        /// <summary>
        /// Supprime les anciens profils inutilisés
        /// </summary>
        public int CleanupOldProfiles(int maxAgeDays = 90)
        {
            lock (_lockObject)
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-maxAgeDays);
                var oldProfiles = _profiles.Values
                    .Where(p => p.LastSeen < cutoffDate)
                    .ToList();

                foreach (var profile in oldProfiles)
                {
                    _profiles.Remove(profile.DeviceAddress);

                    try
                    {
                        var filePath = Path.Combine(_profilesDirectory, $"{profile.DeviceAddress}.json");
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[DeviceProfileManager] Failed to delete profile file: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[DeviceProfileManager] Cleaned up {oldProfiles.Count} old profiles");

                return oldProfiles.Count;
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                lock (_lockObject)
                {
                    // Save all profiles before disposing
                    foreach (var profile in _profiles.Values)
                    {
                        SaveProfile(profile);
                    }

                    _profiles.Clear();
                    _isDisposed = true;
                }

                System.Diagnostics.Debug.WriteLine("[DeviceProfileManager] Disposed");
            }
        }
    }

    // ===== Supporting Data Structures =====

    /// <summary>
    /// Profil d'un appareil Bluetooth avec historique et préférences
    /// </summary>
    public class DeviceProfile
    {
        public string DeviceAddress { get; set; }
        public string DeviceName { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }

        // Connection statistics
        public int ConnectionCount { get; set; }
        public int SuccessfulConnections { get; set; }
        public int FailedConnections { get; set; }
        public double ConnectionSuccessRate { get; set; }
        public TimeSpan TotalConnectionTime { get; set; }

        // Transport preferences (ordered by preference)
        public List<AudioTransport> PreferredTransports { get; set; } = new List<AudioTransport>();
        public AudioTransport? LastUsedTransport { get; set; }
        public AudioTransport? MostReliableTransport { get; set; }

        // Quality metrics
        public List<QualityRecord> QualityHistory { get; set; } = new List<QualityRecord>();
        public double AverageLatency { get; set; }
        public double AveragePacketLoss { get; set; }
        public Dictionary<ConnectionQuality, int> QualityDistribution { get; set; } =
            new Dictionary<ConnectionQuality, int>();

        // Device-specific settings
        public Dictionary<string, object> CustomSettings { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Met à jour l'historique de qualité avec une nouvelle mesure
        /// </summary>
        public void UpdateQualityHistory(ConnectionQuality quality)
        {
            var record = new QualityRecord
            {
                Timestamp = DateTime.UtcNow,
                Quality = quality,
                Transport = LastUsedTransport ?? AudioTransport.A2DP
            };

            QualityHistory.Add(record);

            // Maintain rolling window
            const int maxRecords = 50;
            if (QualityHistory.Count > maxRecords)
            {
                QualityHistory = QualityHistory
                    .OrderByDescending(r => r.Timestamp)
                    .Take(maxRecords)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Enregistrement de qualité horodaté
    /// </summary>
    public class QualityRecord
    {
        public DateTime Timestamp { get; set; }
        public ConnectionQuality Quality { get; set; }
        public AudioTransport Transport { get; set; }
        public double Latency { get; set; }
        public double PacketLossRate { get; set; }
    }

    /// <summary>
    /// Recommandations pour un appareil spécifique
    /// </summary>
    public class DeviceRecommendations
    {
        public string DeviceAddress { get; set; }
        public AudioTransport? RecommendedTransport { get; set; }
        public List<AudioTransport> AlternativeTransports { get; set; } = new List<AudioTransport>();
        public double ExpectedLatency { get; set; }
        public double ExpectedPacketLoss { get; set; }
        public double ReliabilityScore { get; set; }
        public double Confidence { get; set; }
        public List<TransportRecommendation> TransportRecommendations { get; set; } =
            new List<TransportRecommendation>();
    }

    /// <summary>
    /// Recommandation spécifique à un transport
    /// </summary>
    public class TransportRecommendation
    {
        public AudioTransport Transport { get; set; }
        public double AverageLatency { get; set; }
        public double AveragePacketLoss { get; set; }
        public double SuccessRate { get; set; }
        public int UsageCount { get; set; }
    }
}