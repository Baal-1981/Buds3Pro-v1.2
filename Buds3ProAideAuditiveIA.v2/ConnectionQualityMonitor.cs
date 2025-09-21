using System;
using System.Collections.Generic;
using System.Linq;
using Android.Media;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Moniteur de qualité de connexion Bluetooth avec métriques avancées
    /// </summary>
    public sealed class ConnectionQualityMonitor : IDisposable
    {
        private readonly AudioManager _audioManager;
        private readonly Queue<QualityMetrics> _metricsHistory;
        private readonly object _lockObject = new object();

        private ConnectionQuality _currentQuality = ConnectionQuality.Unknown;
        private DateTime _lastUpdate = DateTime.Now;
        private int _consecutivePoorReadings = 0;
        private bool _isDisposed = false;

        // Métriques de qualité
        private double _averageLatency = 0;
        private double _packetLossRate = 0;
        private int _signalStrength = 0;
        private int _audioDropouts = 0;

        // Configuration
        private const int HISTORY_SIZE = 50;
        private const int POOR_QUALITY_THRESHOLD = 3; // Consecutive poor readings

        public ConnectionQuality CurrentQuality => _currentQuality;
        public double AverageLatency => _averageLatency;
        public double PacketLossRate => _packetLossRate;
        public int SignalStrength => _signalStrength;
        public int AudioDropouts => _audioDropouts;

        public event Action<ConnectionQuality> QualityChanged;

        public ConnectionQualityMonitor(AudioManager audioManager)
        {
            _audioManager = audioManager ?? throw new ArgumentNullException(nameof(audioManager));
            _metricsHistory = new Queue<QualityMetrics>(HISTORY_SIZE);
        }

        public void Start()
        {
            try
            {
                // Initial quality assessment
                UpdateQuality();
                System.Diagnostics.Debug.WriteLine("[QualityMonitor] Started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QualityMonitor] Start error: {ex.Message}");
            }
        }

        /// <summary>
        /// Met à jour les métriques de qualité de connexion
        /// </summary>
        public ConnectionQuality UpdateQuality()
        {
            if (_isDisposed) return _currentQuality;

            try
            {
                lock (_lockObject)
                {
                    var metrics = CollectCurrentMetrics();

                    // Add to history
                    _metricsHistory.Enqueue(metrics);
                    if (_metricsHistory.Count > HISTORY_SIZE)
                    {
                        _metricsHistory.Dequeue();
                    }

                    // Calculate quality based on metrics
                    var newQuality = CalculateQuality(metrics);

                    // Update quality with hysteresis to avoid flapping
                    UpdateQualityWithHysteresis(newQuality);

                    _lastUpdate = DateTime.Now;
                    return _currentQuality;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QualityMonitor] Update error: {ex.Message}");
                return _currentQuality;
            }
        }

        /// <summary>
        /// Collecte les métriques actuelles du système audio
        /// </summary>
        private QualityMetrics CollectCurrentMetrics()
        {
            var metrics = new QualityMetrics
            {
                Timestamp = DateTime.Now
            };

            try
            {
                // Collect audio device information
                var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
                var bluetoothDevice = devices.FirstOrDefault(d =>
                    d.Type == AudioDeviceType.BluetoothA2dp ||
                    d.Type == AudioDeviceType.BluetoothSco ||
                    d.Type == AudioDeviceType.BleHeadset ||
                    d.Type == AudioDeviceType.BleSpeaker);

                if (bluetoothDevice != null)
                {
                    // Estimate latency based on device type
                    metrics.EstimatedLatency = EstimateLatencyForDevice(bluetoothDevice);

                    // Check for audio properties (API dependent)
                    metrics.IsConnected = true;

                    // Mock signal strength (not directly available via AudioManager)
                    // Mock signal strength (not directly available via AudioManager)

                    // Audio dropout detection (estimated from system behavior)
                    metrics.AudioDropouts = DetectAudioDropouts();

                    // Packet loss estimation (heuristic based on audio performance)
                    metrics.PacketLossRate = EstimatePacketLoss();
                }
                else
                {
                    metrics.IsConnected = false;
                    metrics.EstimatedLatency = 0;
                    metrics.SignalStrength = 0;
                    metrics.AudioDropouts = 0;
                    metrics.PacketLossRate = 1.0; // 100% loss if no device
                }

                // System audio state
                metrics.AudioMode = _audioManager.Mode;
                metrics.IsBluetoothScoOn = _audioManager.BluetoothScoOn;

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QualityMonitor] Metrics collection error: {ex.Message}");
                metrics.HasError = true;
            }

            return metrics;
        }

        /// <summary>
        /// Estime la latence pour un type d'appareil donné
        /// </summary>
        private double EstimateLatencyForDevice(AudioDeviceInfo device)
        {
            return device.Type switch
            {
                AudioDeviceType.BluetoothA2dp => 150.0, // A2DP typical latency
                AudioDeviceType.BluetoothSco => 80.0,   // SCO lower latency
                AudioDeviceType.BleHeadset => 40.0,     // LE Audio low latency
                AudioDeviceType.BleSpeaker => 45.0,     // LE Audio slightly higher
                _ => 200.0 // Conservative estimate for unknown types
            };
        }

        /// <summary>
        /// Estime la force du signal (heuristique)
        /// </summary>
        private int EstimateSignalStrength(AudioDeviceInfo device)
        {
            try
            {
                // Heuristic based on device responsiveness and audio state
                if (_audioManager.BluetoothScoOn || device.Type == AudioDeviceType.BluetoothA2dp)
                {
                    // If we can establish connection, assume reasonable signal
                    var timeSinceLastUpdate = DateTime.Now - _lastUpdate;
                    if (timeSinceLastUpdate.TotalSeconds < 5)
                    {
                        return 75; // Good signal strength
                    }
                    else
                    {
                        return 50; // Moderate signal strength
                    }
                }

                return 25; // Weak signal strength
            }
            catch
            {
                return 0; // No signal
            }
        }

        /// <summary>
        /// Détecte les coupures audio (estimation heuristique)
        /// </summary>
        private int DetectAudioDropouts()
        {
            try
            {
                // Simple heuristic: check if audio mode changes frequently
                // In a real implementation, this would integrate with AudioEngine metrics
                var recentMetrics = _metricsHistory.Where(m =>
                    (DateTime.Now - m.Timestamp).TotalSeconds < 10).ToList();

                if (recentMetrics.Count > 5)
                {
                    var modeChanges = recentMetrics
                        .Zip(recentMetrics.Skip(1), (a, b) => a.AudioMode != b.AudioMode)
                        .Count(changed => changed);

                    return modeChanges; // More mode changes = more potential dropouts
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Estime le taux de perte de paquets (heuristique)
        /// </summary>
        private double EstimatePacketLoss()
        {
            try
            {
                // Heuristic based on audio dropouts and connection stability
                var recentDropouts = _audioDropouts;
                var connectionStability = _metricsHistory.Count > 10 ?
                    _metricsHistory.Count(m => m.IsConnected) / (double)_metricsHistory.Count : 1.0;

                // Calculate estimated packet loss
                double baseLoss = Math.Max(0, recentDropouts * 0.01); // 1% per dropout
                double stabilityLoss = Math.Max(0, (1.0 - connectionStability) * 0.1); // Up to 10% for instability

                return Math.Min(1.0, baseLoss + stabilityLoss);
            }
            catch
            {
                return 0.05; // Default 5% estimated loss
            }
        }

        /// <summary>
        /// Calcule la qualité basée sur les métriques collectées
        /// </summary>
        private ConnectionQuality CalculateQuality(QualityMetrics metrics)
        {
            if (!metrics.IsConnected || metrics.HasError)
            {
                return ConnectionQuality.Poor;
            }

            // Scoring algorithm
            int score = 100;

            // Latency impact
            if (metrics.EstimatedLatency > 200) score -= 40;
            else if (metrics.EstimatedLatency > 100) score -= 25;
            else if (metrics.EstimatedLatency > 50) score -= 10;

            // Packet loss impact
            if (metrics.PacketLossRate > 0.1) score -= 30; // >10% loss
            else if (metrics.PacketLossRate > 0.05) score -= 20; // >5% loss
            else if (metrics.PacketLossRate > 0.02) score -= 10; // >2% loss

            // Signal strength impact
            if (metrics.SignalStrength < 30) score -= 25;
            else if (metrics.SignalStrength < 50) score -= 15;
            else if (metrics.SignalStrength < 70) score -= 5;

            // Audio dropouts impact
            score -= metrics.AudioDropouts * 5; // 5 points per dropout

            // Determine quality level
            if (score >= 85) return ConnectionQuality.Excellent;
            if (score >= 70) return ConnectionQuality.Good;
            if (score >= 50) return ConnectionQuality.Fair;
            return ConnectionQuality.Poor;
        }

        /// <summary>
        /// Met à jour la qualité avec hystérésis pour éviter les oscillations
        /// </summary>
        private void UpdateQualityWithHysteresis(ConnectionQuality newQuality)
        {
            if (newQuality == _currentQuality)
            {
                _consecutivePoorReadings = 0;
                return;
            }

            // For quality degradation, require multiple consecutive poor readings
            if (newQuality < _currentQuality)
            {
                _consecutivePoorReadings++;
                if (_consecutivePoorReadings >= POOR_QUALITY_THRESHOLD)
                {
                    _currentQuality = newQuality;
                    _consecutivePoorReadings = 0;
                    QualityChanged?.Invoke(_currentQuality);
                    System.Diagnostics.Debug.WriteLine($"[QualityMonitor] Quality degraded to: {_currentQuality}");
                }
            }
            else
            {
                // For quality improvement, update immediately
                _currentQuality = newQuality;
                _consecutivePoorReadings = 0;
                QualityChanged?.Invoke(_currentQuality);
                System.Diagnostics.Debug.WriteLine($"[QualityMonitor] Quality improved to: {_currentQuality}");
            }

            // Update running averages
            _averageLatency = _metricsHistory.Count > 0 ?
                _metricsHistory.Average(m => m.EstimatedLatency) : 0;
            _packetLossRate = _metricsHistory.Count > 0 ?
                _metricsHistory.Average(m => m.PacketLossRate) : 0;
            _signalStrength = _metricsHistory.Count > 0 ?
                (int)_metricsHistory.Average(m => m.SignalStrength) : 0;
            _audioDropouts = _metricsHistory.Count > 0 ?
                (int)_metricsHistory.Sum(m => m.AudioDropouts) : 0;
        }

        /// <summary>
        /// Obtient un rapport détaillé de la qualité
        /// </summary>
        public string GetQualityReport()
        {
            lock (_lockObject)
            {
                return $"Quality={_currentQuality}, " +
                       $"Latency={_averageLatency:F1}ms, " +
                       $"PacketLoss={_packetLossRate:P1}, " +
                       $"Signal={_signalStrength}%, " +
                       $"Dropouts={_audioDropouts}, " +
                       $"Samples={_metricsHistory.Count}";
            }
        }

        /// <summary>
        /// Obtient l'historique des métriques pour analyse
        /// </summary>
        public QualityMetrics[] GetMetricsHistory()
        {
            lock (_lockObject)
            {
                return _metricsHistory.ToArray();
            }
        }

        /// <summary>
        /// Réinitialise les métriques et l'historique
        /// </summary>
        public void Reset()
        {
            lock (_lockObject)
            {
                _metricsHistory.Clear();
                _currentQuality = ConnectionQuality.Unknown;
                _consecutivePoorReadings = 0;
                _averageLatency = 0;
                _packetLossRate = 0;
                _signalStrength = 0;
                _audioDropouts = 0;

                System.Diagnostics.Debug.WriteLine("[QualityMonitor] Reset completed");
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                lock (_lockObject)
                {
                    _metricsHistory.Clear();
                    _isDisposed = true;
                }

                System.Diagnostics.Debug.WriteLine("[QualityMonitor] Disposed");
            }
        }
    }

    /// <summary>
    /// Structure des métriques de qualité de connexion
    /// </summary>
    public struct QualityMetrics
    {
        public DateTime Timestamp { get; set; }
        public bool IsConnected { get; set; }
        public double EstimatedLatency { get; set; } // milliseconds
        public double PacketLossRate { get; set; } // 0.0 - 1.0
        public int SignalStrength { get; set; } // 0 - 100
        public int AudioDropouts { get; set; }
        public Mode AudioMode { get; set; }
        public bool IsBluetoothScoOn { get; set; }
        public bool HasError { get; set; }

        public readonly bool IsGoodQuality =>
            IsConnected && !HasError && EstimatedLatency < 100 &&
            PacketLossRate < 0.05 && SignalStrength > 50;
    }
}