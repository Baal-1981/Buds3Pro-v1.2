using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;
using Android.Media;
using Android.OS;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Gestionnaire Bluetooth intelligent avec auto-fallback, monitoring qualité et profils d'appareils
    /// Nouvelles fonctionnalités v1.2.1:
    /// - Auto-fallback : A2DP → SCO → LE Audio selon disponibilité
    /// - Quality monitoring : Détection automatique de la qualité de connexion
    /// - Device profiles : Mémorisation des préférences par casque
    /// </summary>
    public sealed class BluetoothManager : IDisposable
    {
        // ===== Core Services =====
        private readonly Context _context;
        private readonly AudioManager _audioManager;
        private readonly BluetoothAdapter _bluetoothAdapter;

        // ===== Device Management =====
        private readonly DeviceProfileManager _profileManager;
        private readonly ConnectionQualityMonitor _qualityMonitor;
        private readonly Dictionary<string, BluetoothDevice> _knownDevices;

        // ===== State Management =====
        private BluetoothDevice _currentDevice;
        private AudioTransport _currentTransport = AudioTransport.A2DP;
        private AudioTransport _requestedTransport = AudioTransport.A2DP;
        private ConnectionState _connectionState = ConnectionState.Disconnected;

        // ===== Auto-fallback Configuration =====
        private readonly AutoFallbackConfig _fallbackConfig;
        private bool _autoFallbackEnabled = true;
        private DateTime _lastFallbackAttempt = DateTime.MinValue;
        private const int FALLBACK_COOLDOWN_MS = 5000; // 5 seconds between attempts

        // ===== Monitoring & Events =====
        private readonly System.Timers.Timer _monitoringTimer;
        private AudioDeviceCallback _deviceCallback;

        public event Action<BluetoothDevice, AudioTransport> DeviceConnected;
        public event Action<BluetoothDevice, string> DeviceDisconnected;
        public event Action<AudioTransport, AudioTransport> TransportChanged;
        public event Action<ConnectionQuality> QualityChanged;
        public event Action<string> StatusMessage;

        // ===== Properties =====
        public BluetoothDevice CurrentDevice => _currentDevice;
        public AudioTransport CurrentTransport => _currentTransport;
        public ConnectionState ConnectionState => _connectionState;
        public ConnectionQuality CurrentQuality => _qualityMonitor?.CurrentQuality ?? ConnectionQuality.Unknown;
        public bool AutoFallbackEnabled
        {
            get => _autoFallbackEnabled;
            set => _autoFallbackEnabled = value;
        }

        public BluetoothManager(Context context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _audioManager = (AudioManager)context.GetSystemService(Context.AudioService);
            _bluetoothAdapter = ((Android.Bluetooth.BluetoothManager)_context.GetSystemService(Context.BluetoothService))?.Adapter;

            _knownDevices = new Dictionary<string, BluetoothDevice>();
            _profileManager = new DeviceProfileManager(context);
            _qualityMonitor = new ConnectionQualityMonitor(_audioManager);
            _fallbackConfig = new AutoFallbackConfig();

            // Setup monitoring timer
            _monitoringTimer = new System.Timers.Timer(2000); // 2 seconds
            _monitoringTimer.Elapsed += OnMonitoringTick;
            _monitoringTimer.AutoReset = true;

            InitializeDeviceCallback();
            LoadKnownDevices();

            SafeLog("BluetoothManager initialized with advanced features");
        }

        /// <summary>
        /// Démarre le gestionnaire Bluetooth intelligent
        /// </summary>
        public async Task<bool> StartAsync()
        {
            try
            {
                if (_bluetoothAdapter?.IsEnabled != true)
                {
                    SafeLog("Bluetooth adapter not available or disabled");
                    return false;
                }

                // Register device callback
                _audioManager.RegisterAudioDeviceCallback(_deviceCallback, null);

                // Start quality monitoring
                _qualityMonitor.Start();

                // Start monitoring timer
                _monitoringTimer.Start();

                // Perform initial device scan and connection attempt
                await ScanAndConnectAsync();

                SafeLog("BluetoothManager started successfully");
                return true;
            }
            catch (Exception ex)
            {
                SafeLog($"Error starting BluetoothManager: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Auto-fallback intelligent avec priorités configurables
        /// </summary>
        public async Task<AudioTransport> PerformAutoFallbackAsync(AudioTransport requested = AudioTransport.A2DP)
        {
            if (!_autoFallbackEnabled)
            {
                SafeLog("Auto-fallback disabled, using requested transport");
                return requested;
            }

            // Cooldown check
            if ((DateTime.Now - _lastFallbackAttempt).TotalMilliseconds < FALLBACK_COOLDOWN_MS)
            {
                SafeLog("Auto-fallback cooldown active");
                return _currentTransport;
            }

            _lastFallbackAttempt = DateTime.Now;

            try
            {
                SafeLog($"Starting auto-fallback sequence for transport: {requested}");

                // Get fallback sequence based on device profile and preferences
                var fallbackSequence = GetFallbackSequence(requested);

                foreach (var transport in fallbackSequence)
                {
                    SafeLog($"Attempting transport: {transport}");

                    if (await TryConnectTransportAsync(transport))
                    {
                        SafeLog($"Auto-fallback successful: {transport}");
                        await SetTransportAsync(transport);
                        return transport;
                    }

                    // Wait between attempts
                    await Task.Delay(1000);
                }

                SafeLog("Auto-fallback failed for all transports");
                return _currentTransport;
            }
            catch (Exception ex)
            {
                SafeLog($"Error in auto-fallback: {ex.Message}");
                return _currentTransport;
            }
        }

        /// <summary>
        /// Définit le transport audio avec gestion intelligente
        /// </summary>
        public async Task<bool> SetTransportAsync(AudioTransport transport)
        {
            try
            {
                var oldTransport = _currentTransport;
                _requestedTransport = transport;

                SafeLog($"Setting transport: {oldTransport} → {transport}");

                // Apply transport-specific configuration
                switch (transport)
                {
                    case AudioTransport.A2DP:
                        await ConfigureA2dpAsync();
                        break;

                    case AudioTransport.SCO:
                        await ConfigureScoAsync();
                        break;

                    case AudioTransport.LE_LC3_AUTO:
                        await ConfigureLeAudioAsync();
                        break;
                }

                _currentTransport = transport;

                // Update device profile with preference
                if (_currentDevice != null)
                {
                    _profileManager.UpdateDevicePreference(_currentDevice, transport);
                }

                // Notify listeners
                TransportChanged?.Invoke(oldTransport, transport);

                SafeLog($"Transport changed successfully: {oldTransport} → {transport}");
                return true;
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting transport {transport}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Obtient la séquence de fallback optimisée pour l'appareil actuel
        /// </summary>
        private List<AudioTransport> GetFallbackSequence(AudioTransport requested)
        {
            var sequence = new List<AudioTransport> { requested };

            // Get device-specific preferences if available
            if (_currentDevice != null)
            {
                var profile = _profileManager.GetDeviceProfile(_currentDevice);
                if (profile != null)
                {
                    // Add preferred transports from device profile
                    foreach (var transport in profile.PreferredTransports)
                    {
                        if (!sequence.Contains(transport))
                        {
                            sequence.Add(transport);
                        }
                    }
                }
            }

            // Add default fallback sequence
            var defaultSequence = _fallbackConfig.GetDefaultSequence(requested);
            foreach (var transport in defaultSequence)
            {
                if (!sequence.Contains(transport))
                {
                    sequence.Add(transport);
                }
            }

            SafeLog($"Fallback sequence: {string.Join(" → ", sequence)}");
            return sequence;
        }

        /// <summary>
        /// Teste la connectivité d'un transport spécifique
        /// </summary>
        private async Task<bool> TryConnectTransportAsync(AudioTransport transport)
        {
            try
            {
                return await (transport switch
                {
                    AudioTransport.A2DP => TestA2dpConnectivityAsync(),
                    AudioTransport.SCO => TestScoConnectivityAsync(),
                    AudioTransport.LE_LC3_AUTO => TestLeAudioConnectivityAsync(),
                    _ => Task.FromResult(false)
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error testing transport {transport}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Configuration A2DP optimisée
        /// </summary>
        private async Task ConfigureA2dpAsync()
        {
            try
            {
                // Leave communication mode for A2DP
                BluetoothRouting_Utilities.LeaveCommunicationMode(_context);

                // Ensure A2DP profile is connected
                var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
                var a2dpDevice = devices.FirstOrDefault(d => d.Type == AudioDeviceType.BluetoothA2dp);

                if (a2dpDevice != null)
                {
                    SafeLog($"A2DP device found: {a2dpDevice.ProductName}");
                    _connectionState = ConnectionState.Connected;
                }
                else
                {
                    SafeLog("No A2DP device available");
                    _connectionState = ConnectionState.Disconnected;
                }

                await Task.Delay(500); // Allow time for configuration
            }
            catch (Exception ex)
            {
                SafeLog($"Error configuring A2DP: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Configuration SCO optimisée avec retry intelligent
        /// </summary>
        private async Task ConfigureScoAsync()
        {
            try
            {
                // Enter communication mode
                BluetoothRouting_Utilities.EnterCommunicationMode(_context);

                // Attempt SCO connection with retries
                int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    SafeLog($"SCO connection attempt {attempt}/{maxRetries}");

                    if (BluetoothRouting_Utilities.EnsureSco(_context, 4000))
                    {
                        SafeLog("SCO connected successfully");
                        _connectionState = ConnectionState.Connected;

                        // Force communication device
                        BluetoothRouting_Utilities.ForceCommunicationDeviceSco(_context);
                        return;
                    }

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000); // Wait before retry
                    }
                }

                SafeLog("SCO connection failed after all retries");
                _connectionState = ConnectionState.Failed;
                throw new Exception("SCO connection failed");
            }
            catch (Exception ex)
            {
                SafeLog($"Error configuring SCO: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Configuration LE Audio (système géré)
        /// </summary>
        private async Task ConfigureLeAudioAsync()
        {
            try
            {
                // Leave communication mode
                BluetoothRouting_Utilities.LeaveCommunicationMode(_context);

                // Check for LE Audio devices
                var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
                var leDevice = devices.FirstOrDefault(d =>
                    d.Type == AudioDeviceType.BleHeadset ||
                    d.Type == AudioDeviceType.BleSpeaker ||
                    d.Type == AudioDeviceType.BleBroadcast);

                if (leDevice != null)
                {
                    SafeLog($"LE Audio device found: {leDevice.ProductName}");
                    _connectionState = ConnectionState.Connected;
                }
                else
                {
                    SafeLog("No LE Audio device available");
                    _connectionState = ConnectionState.Disconnected;
                }

                await Task.Delay(300); // Allow time for LE Audio negotiation
            }
            catch (Exception ex)
            {
                SafeLog($"Error configuring LE Audio: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tests de connectivité spécifiques
        /// </summary>
        private Task<bool> TestA2dpConnectivityAsync()
        {
            try
            {
                var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
                var a2dpDevice = devices.FirstOrDefault(d => d.Type == AudioDeviceType.BluetoothA2dp);

                if (a2dpDevice != null)
                {
                    // Test with actual audio routing
                    var wasConnected = BluetoothRouting_Utilities.IsA2dpActive(_context);
                    SafeLog($"A2DP test result: {wasConnected}");
                    return Task.FromResult(wasConnected);
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                SafeLog($"A2DP connectivity test failed: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private async Task<bool> TestScoConnectivityAsync()
        {
            try
            {
                // Quick SCO test
                BluetoothRouting_Utilities.EnterCommunicationMode(_context);
                await Task.Delay(500);

                bool scoConnected = BluetoothRouting_Utilities.IsScoOn(_context);

                if (!scoConnected)
                {
                    // Try to establish SCO
                    scoConnected = BluetoothRouting_Utilities.EnsureSco(_context, 2000);
                }

                SafeLog($"SCO test result: {scoConnected}");
                return scoConnected;
            }
            catch (Exception ex)
            {
                SafeLog($"SCO connectivity test failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TestLeAudioConnectivityAsync()
        {
            try
            {
                await Task.Delay(200); // LE Audio detection delay

                bool leActive = BluetoothRouting_Utilities.IsLeActive(_context);
                SafeLog($"LE Audio test result: {leActive}");
                return leActive;
            }
            catch (Exception ex)
            {
                SafeLog($"LE Audio connectivity test failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Scan et connexion automatique des appareils
        /// </summary>
        private async Task ScanAndConnectAsync()
        {
            try
            {
                SafeLog("Scanning for connected Bluetooth devices...");

                // Get currently connected audio devices
                var devices = _audioManager.GetDevices(GetDevicesTargets.All);
                var bluetoothDevices = devices.Where(d =>
                    d.Type == AudioDeviceType.BluetoothA2dp ||
                    d.Type == AudioDeviceType.BluetoothSco ||
                    d.Type == AudioDeviceType.BleHeadset ||
                    d.Type == AudioDeviceType.BleSpeaker).ToList();

                if (bluetoothDevices.Any())
                {
                    var device = bluetoothDevices.First();
                    SafeLog($"Found connected device: {device.ProductName} ({device.Type})");

                    // Update current device info
                    UpdateCurrentDevice(device);

                    // Determine optimal transport for this device
                    var optimalTransport = await DetermineOptimalTransportAsync(device);
                    await SetTransportAsync(optimalTransport);
                }
                else
                {
                    SafeLog("No connected Bluetooth audio devices found");
                    _connectionState = ConnectionState.Disconnected;
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error in scan and connect: {ex.Message}");
            }
        }

        /// <summary>
        /// Détermine le transport optimal pour un appareil donné
        /// </summary>
        private Task<AudioTransport> DetermineOptimalTransportAsync(AudioDeviceInfo device)
        {
            try
            {
                // Check device profile preferences first
                if (_currentDevice != null)
                {
                    var profile = _profileManager.GetDeviceProfile(_currentDevice);
                    if (profile?.PreferredTransports.Any() == true)
                    {
                        var preferredTransport = profile.PreferredTransports.First();
                        SafeLog($"Using device preferred transport: {preferredTransport}");
                        return Task.FromResult(preferredTransport);
                    }
                }

                // Default logic based on device type
                return Task.FromResult(
                    device.Type switch
                    {
                        AudioDeviceType.BluetoothA2dp => AudioTransport.A2DP,
                        AudioDeviceType.BluetoothSco => AudioTransport.SCO,
                        AudioDeviceType.BleHeadset => AudioTransport.LE_LC3_AUTO,
                        AudioDeviceType.BleSpeaker => AudioTransport.LE_LC3_AUTO,
                        _ => AudioTransport.A2DP // Default fallback
                    });
            }
            catch (Exception ex)
            {
                SafeLog($"Error determining optimal transport: {ex.Message}");
                return Task.FromResult(AudioTransport.A2DP);
            }
        }

        /// <summary>
        /// Met à jour les informations de l'appareil actuel
        /// </summary>
        private void UpdateCurrentDevice(AudioDeviceInfo audioDevice)
        {
            try
            {
                // Try to find the corresponding BluetoothDevice
                if (_bluetoothAdapter?.BondedDevices != null)
                {
                    var bondedDevice = _bluetoothAdapter.BondedDevices
                        .FirstOrDefault(d => d.Name == audioDevice.ProductName);

                    if (bondedDevice != null)
                    {
                        _currentDevice = bondedDevice;
                        _knownDevices[bondedDevice.Address] = bondedDevice;

                        SafeLog($"Updated current device: {bondedDevice.Name} ({bondedDevice.Address})");

                        // Load or create device profile
                        _profileManager.EnsureDeviceProfile(bondedDevice);
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error updating current device: {ex.Message}");
            }
        }

        /// <summary>
        /// Monitoring timer tick
        /// </summary>
        private async void OnMonitoringTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // Update connection quality
                var quality = _qualityMonitor.UpdateQuality();

                // Check if auto-fallback is needed
                if (_autoFallbackEnabled && quality == ConnectionQuality.Poor)
                {
                    SafeLog("Poor connection quality detected, attempting auto-fallback");
                    await PerformAutoFallbackAsync(_requestedTransport);
                }

                // Update device profiles with current metrics
                _profileManager.GetDeviceProfile(_currentDevice)?.UpdateQualityHistory(quality);
            }
            catch (Exception ex)
            {
                SafeLog($"Error in monitoring tick: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize device callback
        /// </summary>
        private void InitializeDeviceCallback()
        {
            _deviceCallback = new EnhancedAudioDeviceCallback(
                onDeviceAdded: async (devices) => {
                    SafeLog($"Audio device added: {devices.Length} devices");
                    await ScanAndConnectAsync();
                },
                onDeviceRemoved: (devices) => {
                    SafeLog($"Audio device removed: {devices.Length} devices");
                    _connectionState = ConnectionState.Disconnected;
                    DeviceDisconnected?.Invoke(_currentDevice, "Device removed");
                }
            );
        }

        /// <summary>
        /// Load known devices from storage
        /// </summary>
        private void LoadKnownDevices()
        {
            try
            {
                if (_bluetoothAdapter?.BondedDevices != null)
                {
                    foreach (var device in _bluetoothAdapter.BondedDevices)
                    {
                        _knownDevices[device.Address] = device;
                        _profileManager.EnsureDeviceProfile(device);
                    }

                    SafeLog($"Loaded {_knownDevices.Count} known Bluetooth devices");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error loading known devices: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtient un rapport d'état complet
        /// </summary>
        public string GetStatusReport()
        {
            try
            {
                var currentDeviceName = _currentDevice?.Name ?? "None";
                var qualityInfo = _qualityMonitor?.GetQualityReport() ?? "Unknown";
                var profileCount = _profileManager?.GetProfileCount() ?? 0;

                return $"Bluetooth Status: Device='{currentDeviceName}', " +
                       $"Transport={_currentTransport}, State={_connectionState}, " +
                       $"Quality={CurrentQuality}, AutoFallback={_autoFallbackEnabled}, " +
                       $"KnownDevices={_knownDevices.Count}, Profiles={profileCount}, " +
                       $"QualityDetails=[{qualityInfo}]";
            }
            catch (Exception ex)
            {
                return $"Status error: {ex.Message}";
            }
        }

        private void SafeLog(string message)
        {
            try
            {
                StatusMessage?.Invoke(message);
                System.Diagnostics.Debug.WriteLine($"[BluetoothManager] {message}");
            }
            catch
            {
                // Never throw from logging
            }
        }

        public void Dispose()
        {
            try
            {
                _monitoringTimer?.Stop();
                _monitoringTimer?.Dispose();

                if (_deviceCallback != null)
                {
                    _audioManager?.UnregisterAudioDeviceCallback(_deviceCallback);
                }

                _qualityMonitor?.Dispose();
                _profileManager?.Dispose();

                SafeLog("BluetoothManager disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing BluetoothManager: {ex.Message}");
            }
        }
    }

    // ===== Supporting Types =====

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed,
        Reconnecting
    }

    public enum ConnectionQuality
    {
        Unknown,
        Excellent,  // < 20ms latency, no drops
        Good,       // < 50ms latency, rare drops
        Fair,       // < 100ms latency, occasional drops
        Poor        // > 100ms latency, frequent drops
    }

    /// <summary>
    /// Enhanced audio device callback with async support
    /// </summary>
    public class EnhancedAudioDeviceCallback : AudioDeviceCallback
    {
        private readonly Func<AudioDeviceInfo[], Task> _onDeviceAdded;
        private readonly Action<AudioDeviceInfo[]> _onDeviceRemoved;

        public EnhancedAudioDeviceCallback(
            Func<AudioDeviceInfo[], Task> onDeviceAdded = null,
            Action<AudioDeviceInfo[]> onDeviceRemoved = null)
        {
            _onDeviceAdded = onDeviceAdded;
            _onDeviceRemoved = onDeviceRemoved;
        }

        public override void OnAudioDevicesAdded(AudioDeviceInfo[] addedDevices)
        {
            try
            {
                _onDeviceAdded?.Invoke(addedDevices);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnAudioDevicesAdded: {ex.Message}");
            }
        }

        public override void OnAudioDevicesRemoved(AudioDeviceInfo[] removedDevices)
        {
            try
            {
                _onDeviceRemoved?.Invoke(removedDevices);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnAudioDevicesRemoved: {ex.Message}");
            }
        }
    }
}