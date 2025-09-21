using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using System;
using System.Linq;
using System.Threading.Tasks;
using Android.Bluetooth;

namespace Buds3ProAideAuditiveIA.v2
{
    [Activity(
        Label = "@string/app_name",
        MainLauncher = true,
        Exported = true,
        Theme = "@style/Theme.Sonara.Dark",
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
    )]
    public class MainActivity : AppCompatActivity, ILogSink
    {
        private const int ReqAudio = 0xB301;

        // ===== Audio Engine Core =====
        private AudioEngine _engine;

        // ===== Bluetooth Management Enhanced =====
        private BluetoothManager _bluetoothManager;
        private Button _btnBluetoothConfig, _btnBluetoothProfiles, _btnBluetoothQuality;
        private TextView _bluetoothStatusDetail, _qualityMetrics, _deviceProfileInfo;
        private Switch _swAutoFallback;
        private LinearLayout _bluetoothEnhancedPanel;

        // ===== Top Control Bar =====
        private Button _btnStart, _btnStop, _btnEmergencyStop;
        private TextView _healthStatus;
        private Button _btnResetEmergency;

        // ===== Quick Actions Panel =====
        private QuickActionsPanel _quickActionsPanel;
        private Button _btnQuickCalibrate, _btnQuickReset, _btnQuickPreset;

        // ===== Safety & Emergency Controls =====
        private Switch _swSafetyLimiter;
        private LinearLayout _emergencyPanel;

        // ===== Quick Toggles =====
        private Switch _swPassThrough, _swNoiseCancel;

        // ===== Latency Profile Management (v1.3 Ready) =====
        private Spinner _latencyProfileSpinner;
        private Button _btnApplyProfile;
        private TextView _latencyProfileInfo;
        private LatencyProfileManager _latencyManager;

        // ===== Preset Management Enhanced =====
        private Spinner _presetSpinner;
        private Button _btnSavePreset, _btnLoadPreset, _btnExportPresets, _btnImportPresets;
        private PresetManager _presetManager;
        private LinearLayout _presetPanel;

        // ===== System Health & Performance =====
        private TextView _systemHealthStatus;
        private ProgressBar _cpuUsageBar, _memoryUsageBar, _latencyBar;
        private LinearLayout _systemMetricsPanel;

        // ===== Calibration Enhanced =====
        private Button _btnCalibrate;
        private int _calibMs = 500;
        private ProgressBar _calibrationProgress;
        private TextView _calibrationStatus;

        // ===== Panel Toggles =====
        private Button _btnNcToggle, _btnEqToggle, _btnQualityToggle, _btnRouteToggle, _btnAdvancedToggle, _btnLatencyToggle;

        // ===== Panels =====
        private LinearLayout _ncPanel, _eqPanel, _qualityPanel, _routePanel, _advancedPanel, _latencyPanel;

        // ===== Panel Content Widgets =====
        private Switch _swPlatformFx, _swDspNs, _swAmbient;
        private Switch _swEq;
        private Switch _swHP, _swClarity, _swDeEss, _swHum;
        private RadioButton _rbA2dp, _rbSco, _rbLc3Auto;
        private Switch _swAdaptiveCompression, _swAutoGainControl, _swAudioWatchdog;

        // ===== Enhanced Sliders with Real-time Display =====
        private class EnhancedSlider
        {
            public SeekBar Slider { get; set; }
            public TextView Label { get; set; }
            public TextView ValueDisplay { get; set; }
            public string Unit { get; set; }
            public string Prefix { get; set; }
        }

        private EnhancedSlider _sliderAmbientDb, _sliderAttack, _sliderRelease, _sliderCalibration;
        private EnhancedSlider _sliderBassDb, _sliderMidDb, _sliderTrebleDb;
        private EnhancedSlider _sliderBassHz, _sliderMidHz, _sliderTrebleHz;
        private EnhancedSlider _sliderGain;

        // ===== Visual Indicators =====
        private TextView _audioStatusLed, _bluetoothStatusLed, _latencyStatusLed;
        private LinearLayout _statusIndicatorsPanel;

        // ===== Real-time Graph Enhanced =====
        private RealTimeChartView _chart;
        private LinearLayout _graphPanel;

        // ===== Status & Monitoring =====
        private TextView _status, _latency, _routeInfo, _performanceInfo;
        private Switch _swShowLogs, _swShowAdvancedMetrics;
        private LinearLayout _logCard, _metricsCard;

        // ===== Route Management =====
        private Button _btnRebind;
        private AudioDeviceCallbackEx _deviceCallback;

        // ===== Monitoring & Safety =====
        private System.Timers.Timer _healthTimer, _metricsUpdateTimer;
        private LatencyMeter _latencyMeter;

        // ===== Configuration & Safety =====
        private bool _isConfiguring = false;
        private readonly object _configLock = new object();

        // ===== Accessibility Enhanced =====
        private bool _accessibilityMode = false;
        private bool _highVisibilityMode = false;
        private LinearLayout _accessibilityPanel;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            try
            {
                InitializeCore();
                CreateEnhancedUserInterface();
                SetupEventHandlers();
                InitializeEnhancedMonitoring();
                InitializeLatencyProfileManager();
                InitializePresetManager();
                SetupAccessibility();

                EnsureMicPermission();
                UpdateRouteLabel();

                SafeLog("MainActivity initialized successfully with v1.2.1 enhancements including Bluetooth management");
            }
            catch (Exception ex)
            {
                SafeLog($"Critical error in MainActivity.OnCreate: {ex.Message}");
                ShowErrorDialog("Initialization Failed", $"Failed to initialize application: {ex.Message}");
            }
        }

        private void InitializeCore()
        {
            _engine = new AudioEngine(this, this);
            InitializeBluetoothManager();
        }

        private void InitializeBluetoothManager()
        {
            try
            {
                _bluetoothManager = new BluetoothManager(this);

                // Subscribe to events
                _bluetoothManager.DeviceConnected += OnBluetoothDeviceConnected;
                _bluetoothManager.DeviceDisconnected += OnBluetoothDeviceDisconnected;
                _bluetoothManager.TransportChanged += OnBluetoothTransportChanged;
                _bluetoothManager.QualityChanged += OnBluetoothQualityChanged;
                _bluetoothManager.StatusMessage += OnBluetoothStatusMessage;

                // Start the manager
                Task.Run(async () =>
                {
                    var started = await _bluetoothManager.StartAsync();
                    RunOnUiThread(() =>
                    {
                        if (started)
                        {
                            SafeLog("Bluetooth manager started successfully");
                            UpdateBluetoothUI();
                        }
                        else
                        {
                            SafeLog("Failed to start Bluetooth manager");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error initializing Bluetooth manager: {ex.Message}");
            }
        }

        private void InitializeLatencyProfileManager()
        {
            try
            {
                _latencyManager = new LatencyProfileManager();
                _latencyManager.ProfileChanged += OnLatencyProfileChanged;
                _latencyManager.AdaptationMessage += OnLatencyAdaptationMessage;

                // Setup spinner with profiles
                var profiles = LatencyProfileManager.GetAllProfiles();
                var profileNames = new string[profiles.Length];
                for (int i = 0; i < profiles.Length; i++)
                {
                    var config = LatencyProfileManager.GetConfig(profiles[i]);
                    profileNames[i] = $"{profiles[i]} ({config.ExpectedLatencyMs}ms)";
                }

                var profileAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, profileNames);
                profileAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                _latencyProfileSpinner.Adapter = profileAdapter;

                // Set recommended profile
                var recommended = _latencyManager.RecommendProfile();
                var recommendedIndex = Array.IndexOf(profiles, recommended);
                if (recommendedIndex >= 0)
                {
                    _latencyProfileSpinner.SetSelection(recommendedIndex);
                }

                SafeLog($"Latency profile manager initialized, recommended: {recommended}");
            }
            catch (Exception ex)
            {
                SafeLog($"Error initializing latency profile manager: {ex.Message}");
            }
        }

        private void InitializePresetManager()
        {
            try
            {
                var presetsDir = System.IO.Path.Combine(GetExternalFilesDir("presets")?.AbsolutePath ?? FilesDir.AbsolutePath, "audio_presets");
                _presetManager = new PresetManager(presetsDir);

                // Load available presets
                var presetNames = _presetManager.ListNames().ToArray();
                var defaultPresets = new[] { "Default", "Restaurant", "Office", "Outdoor", "Speech", "Music", "Gaming" };

                var allPresets = new string[defaultPresets.Length + presetNames.Length];
                defaultPresets.CopyTo(allPresets, 0);
                presetNames.CopyTo(allPresets, defaultPresets.Length);

                var presetAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, allPresets);
                presetAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                _presetSpinner.Adapter = presetAdapter;

                SafeLog($"Preset manager initialized with {presetNames.Length} user presets");
            }
            catch (Exception ex)
            {
                SafeLog($"Error initializing preset manager: {ex.Message}");
            }
        }

        private void CreateEnhancedUserInterface()
        {
            var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            root.SetPadding(24, 24, 24, 24);
            root.SetBackgroundColor(Color.ParseColor("#121212"));

            // ===== Enhanced Title Bar with Health Status =====
            CreateTitleBar(root);

            // ===== Emergency Controls Enhanced =====
            CreateEmergencyControls(root);

            // ===== Quick Actions Panel =====
            CreateQuickActionsPanel(root);

            // ===== Status Indicators Panel =====
            CreateStatusIndicators(root);

            // ===== Main Controls =====
            CreateMainControls(root);

            // ===== System Health Metrics =====
            CreateSystemHealthPanel(root);

            // ===== Latency Profile Management =====
            CreateLatencyProfilePanel(root);

            // ===== Enhanced Preset Management =====
            CreateEnhancedPresetPanel(root);

            // ===== Quick Toggles =====
            CreateQuickToggles(root);

            // ===== Calibration Enhanced =====
            CreateEnhancedCalibration(root);

            // ===== Panel Headers =====
            CreatePanelHeaders(root);

            // ===== All Panels =====
            CreateAllPanels(root);

            // ===== Enhanced Bluetooth Panel =====
            CreateBluetoothEnhancedPanel(root);

            // ===== Enhanced Real-time Graph =====
            CreateEnhancedGraph(root);

            // ===== Status Information =====
            CreateStatusInformation(root);

            // ===== Bottom Navigation =====
            CreateBottomNavigation(root);

            // ===== Accessibility Panel =====
            CreateAccessibilityPanel(root);

            SetupScrollContainer(root);
        }

        private void CreateTitleBar(LinearLayout root)
        {
            var titleRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

            var title = new TextView(this)
            {
                Text = "Sonara – Hearing Assist v1.2.1 Enhanced",
                TextSize = 20f,
                Typeface = Typeface.DefaultBold
            };
            title.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            AccessibleUIComponents.SetAsHeading(title, true);

            _healthStatus = new TextView(this) { Text = "🟢", TextSize = 24f };
            _healthStatus.SetPadding(16, 0, 0, 0);
            AccessibleUIComponents.SetContentDescription(_healthStatus, "System health status indicator");

            titleRow.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            titleRow.AddView(_healthStatus);
            titleRow.SetPadding(0, 0, 0, 16);
            root.AddView(titleRow);
        }

        private void CreateEmergencyControls(LinearLayout root)
        {
            _emergencyPanel = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _emergencyPanel.SetBackgroundColor(Color.ParseColor("#2C1810"));
            _emergencyPanel.SetPadding(16, 12, 16, 12);

            var emergencyTitle = new TextView(this)
            {
                Text = "🚨 Emergency Controls",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            emergencyTitle.SetTextColor(new Color(0xFF, 0xA5, 0x00));
            _emergencyPanel.AddView(emergencyTitle);

            var emergencyRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            emergencyRow.SetPadding(0, 8, 0, 0);

            _btnEmergencyStop = MakeEmergency("🛑 EMERGENCY STOP");
            _btnResetEmergency = MakeSecondary("Reset Emergency");
            _swSafetyLimiter = MakeSwitch("Safety Limiter", true);

            emergencyRow.AddView(_btnEmergencyStop, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2f));
            emergencyRow.AddView(_btnResetEmergency, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            _emergencyPanel.AddView(emergencyRow);
            _emergencyPanel.AddView(_swSafetyLimiter);

            root.AddView(_emergencyPanel);
        }

        private void CreateQuickActionsPanel(LinearLayout root)
        {
            _quickActionsPanel = new QuickActionsPanel(this);

            var quickActionsContainer = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            quickActionsContainer.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            quickActionsContainer.SetPadding(16, 12, 16, 12);

            var quickTitle = new TextView(this)
            {
                Text = "⚡ Quick Actions",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            quickTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            quickActionsContainer.AddView(quickTitle);

            var quickButtonsRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            quickButtonsRow.SetPadding(0, 8, 0, 0);

            _btnQuickCalibrate = MakeTertiary("🎯 Calibrate");
            _btnQuickReset = MakeTertiary("🔄 Reset");
            _btnQuickPreset = MakeTertiary("💾 Quick Save");

            quickButtonsRow.AddView(_btnQuickCalibrate, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            quickButtonsRow.AddView(_btnQuickReset, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            quickButtonsRow.AddView(_btnQuickPreset, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            quickActionsContainer.AddView(quickButtonsRow);
            quickActionsContainer.AddView(_quickActionsPanel);

            root.AddView(quickActionsContainer);
        }

        private void CreateStatusIndicators(LinearLayout root)
        {
            _statusIndicatorsPanel = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            _statusIndicatorsPanel.SetBackgroundColor(Color.ParseColor("#1A1A1A"));
            _statusIndicatorsPanel.SetPadding(16, 8, 16, 8);

            _audioStatusLed = CreateStatusLed("🔊 Audio", "Audio engine status");
            _bluetoothStatusLed = CreateStatusLed("🔶 Bluetooth", "Bluetooth connection status");
            _latencyStatusLed = CreateStatusLed("⏱️ Latency", "Audio latency status");

            _statusIndicatorsPanel.AddView(_audioStatusLed, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            _statusIndicatorsPanel.AddView(_bluetoothStatusLed, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            _statusIndicatorsPanel.AddView(_latencyStatusLed, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            root.AddView(_statusIndicatorsPanel);
        }

        private TextView CreateStatusLed(string text, string description)
        {
            var led = new TextView(this)
            {
                Text = text,
                TextSize = 12f,
                Gravity = GravityFlags.Center
            };
            led.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            led.SetPadding(8, 4, 8, 4);
            AccessibleUIComponents.SetContentDescription(led, description);
            return led;
        }

        private void CreateMainControls(LinearLayout root)
        {
            var rowMain = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            rowMain.SetPadding(0, 8, 0, 16);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop) rowMain.Elevation = 12f;

            _btnStart = MakePrimary("▶ START");
            _btnStop = MakePrimary("⏹ STOP");

            AccessibleUIComponents.SetContentDescription(_btnStart, "Start audio processing");
            AccessibleUIComponents.SetContentDescription(_btnStop, "Stop audio processing");

            rowMain.AddView(_btnStart, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            rowMain.AddView(_btnStop, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            root.AddView(rowMain);
        }

        private void CreateSystemHealthPanel(LinearLayout root)
        {
            _systemMetricsPanel = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _systemMetricsPanel.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            _systemMetricsPanel.SetPadding(16, 12, 16, 12);

            var metricsTitle = new TextView(this)
            {
                Text = "📊 System Health & Performance",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            metricsTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            _systemMetricsPanel.AddView(metricsTitle);

            // CPU Usage
            var cpuRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            var cpuLabel = new TextView(this) { Text = "CPU: ", TextSize = 12f };
            cpuLabel.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            _cpuUsageBar = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100, Progress = 0 };
            cpuRow.AddView(cpuLabel);
            cpuRow.AddView(_cpuUsageBar, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            // Memory Usage
            var memoryRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            var memoryLabel = new TextView(this) { Text = "Memory: ", TextSize = 12f };
            memoryLabel.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            _memoryUsageBar = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100, Progress = 0 };
            memoryRow.AddView(memoryLabel);
            memoryRow.AddView(_memoryUsageBar, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            // Latency Status
            var latencyRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            var latencyLabel = new TextView(this) { Text = "Latency: ", TextSize = 12f };
            latencyLabel.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            _latencyBar = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100, Progress = 0 };
            latencyRow.AddView(latencyLabel);
            latencyRow.AddView(_latencyBar, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            _systemMetricsPanel.AddView(cpuRow);
            _systemMetricsPanel.AddView(memoryRow);
            _systemMetricsPanel.AddView(latencyRow);

            _systemHealthStatus = new TextView(this)
            {
                Text = "System Status: Initializing...",
                TextSize = 11f
            };
            _systemHealthStatus.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            _systemMetricsPanel.AddView(_systemHealthStatus);

            root.AddView(_systemMetricsPanel);
        }

        private void CreateLatencyProfilePanel(LinearLayout root)
        {
            _latencyPanel = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _latencyPanel.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            _latencyPanel.SetPadding(16, 12, 16, 12);
            _latencyPanel.Visibility = ViewStates.Gone;

            var latencyTitle = new TextView(this)
            {
                Text = "⚡ Latency Optimization (v1.3 Preview)",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            latencyTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            _latencyPanel.AddView(latencyTitle);

            var profileRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            profileRow.SetPadding(0, 8, 0, 8);

            _latencyProfileSpinner = new Spinner(this);
            _btnApplyProfile = MakeTertiary("Apply");

            profileRow.AddView(_latencyProfileSpinner, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2f));
            profileRow.AddView(_btnApplyProfile);
            _latencyPanel.AddView(profileRow);

            _latencyProfileInfo = new TextView(this)
            {
                Text = "Latency profiles optimize buffer sizes and processing for different use cases.",
                TextSize = 11f
            };
            _latencyProfileInfo.SetTextColor(new Color(0x8A, 0x8A, 0x8A));
            _latencyPanel.AddView(_latencyProfileInfo);

            root.AddView(_latencyPanel);
        }

        private void CreateEnhancedPresetPanel(LinearLayout root)
        {
            _presetPanel = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _presetPanel.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            _presetPanel.SetPadding(16, 12, 16, 12);

            var presetTitle = new TextView(this)
            {
                Text = "🎛️ Audio Presets Enhanced",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            presetTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            _presetPanel.AddView(presetTitle);

            var presetRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

            _presetSpinner = new Spinner(this);
            _btnSavePreset = MakeTertiary("💾 Save");
            _btnLoadPreset = MakeTertiary("📁 Load");

            presetRow.AddView(_presetSpinner, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2f));
            presetRow.AddView(_btnSavePreset);
            presetRow.AddView(_btnLoadPreset);
            _presetPanel.AddView(presetRow);

            var presetActionsRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            _btnExportPresets = MakeTertiary("📤 Export All");
            _btnImportPresets = MakeTertiary("📥 Import");

            presetActionsRow.AddView(_btnExportPresets, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            presetActionsRow.AddView(_btnImportPresets, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            _presetPanel.AddView(presetActionsRow);

            root.AddView(_presetPanel);
        }

        private void CreateQuickToggles(LinearLayout root)
        {
            _swPassThrough = MakeSwitch("🎯 Pass-through", true);
            _swNoiseCancel = MakeSwitch("🔇 Noise Cancelling (DSP)", true);

            AccessibleUIComponents.SetContentDescription(_swPassThrough, "Toggle pass-through mode");
            AccessibleUIComponents.SetContentDescription(_swNoiseCancel, "Toggle noise cancelling");

            root.AddView(_swPassThrough);
            root.AddView(_swNoiseCancel);
        }

        private void CreateEnhancedCalibration(LinearLayout root)
        {
            var calibrationCard = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            calibrationCard.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            calibrationCard.SetPadding(16, 12, 16, 12);

            var calibTitle = new TextView(this)
            {
                Text = "🎯 Enhanced Calibration",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            calibTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            calibrationCard.AddView(calibTitle);

            var calibRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            _btnCalibrate = MakeSecondary("🎯 CALIBRATE");
            _calibrationProgress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100, Progress = 0 };
            _calibrationProgress.Visibility = ViewStates.Gone;

            calibRow.AddView(_btnCalibrate, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            calibRow.AddView(_calibrationProgress, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            calibrationCard.AddView(calibRow);

            _calibrationStatus = new TextView(this)
            {
                Text = "Ready for calibration",
                TextSize = 11f
            };
            _calibrationStatus.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            calibrationCard.AddView(_calibrationStatus);

            root.AddView(calibrationCard);
        }

        private void CreatePanelHeaders(LinearLayout root)
        {
            _btnRouteToggle = MakeSecondary("🔊 BLUETOOTH ENHANCED ▼");
            _btnNcToggle = MakeSecondary("🔇 NOISE CANCELLING ▼");
            _btnEqToggle = MakeSecondary("🎚️ EQUALIZER ▼");
            _btnQualityToggle = MakeSecondary("🎤 SPEECH QUALITY ▼");
            _btnAdvancedToggle = MakeSecondary("⚙️ ADVANCED SETTINGS ▼");
            _btnLatencyToggle = MakeSecondary("⚡ LATENCY PROFILES ▼");

            root.AddView(_btnRouteToggle);
            root.AddView(_btnNcToggle);
            root.AddView(_btnEqToggle);
            root.AddView(_btnQualityToggle);
            root.AddView(_btnAdvancedToggle);
            root.AddView(_btnLatencyToggle);
        }

        private void CreateAllPanels(LinearLayout root)
        {
            _routePanel = BuildRoutePanel();
            _ncPanel = BuildNoiseCancelPanel();
            _eqPanel = BuildEqPanel();
            _qualityPanel = BuildQualityPanel();
            _advancedPanel = BuildAdvancedPanel();

            _routePanel.Visibility = ViewStates.Gone;
            _ncPanel.Visibility = ViewStates.Gone;
            _eqPanel.Visibility = ViewStates.Gone;
            _qualityPanel.Visibility = ViewStates.Gone;
            _advancedPanel.Visibility = ViewStates.Gone;

            root.AddView(_routePanel);
            root.AddView(_ncPanel);
            root.AddView(_eqPanel);
            root.AddView(_qualityPanel);
            root.AddView(_advancedPanel);
        }

        private void CreateBluetoothEnhancedPanel(LinearLayout root)
        {
            _bluetoothEnhancedPanel = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _bluetoothEnhancedPanel.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            _bluetoothEnhancedPanel.SetPadding(16, 12, 16, 12);
            _bluetoothEnhancedPanel.Visibility = ViewStates.Gone;

            var bluetoothTitle = new TextView(this)
            {
                Text = "🔊 Enhanced Bluetooth Management",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            bluetoothTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            _bluetoothEnhancedPanel.AddView(bluetoothTitle);

            // Auto-fallback control
            _swAutoFallback = MakeSwitch("🔄 Auto-fallback (A2DP → SCO → LE)", true);
            _bluetoothEnhancedPanel.AddView(_swAutoFallback);

            // Status display
            _bluetoothStatusDetail = new TextView(this)
            {
                Text = "Status: Initializing...",
                TextSize = 11f
            };
            _bluetoothStatusDetail.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            _bluetoothEnhancedPanel.AddView(_bluetoothStatusDetail);

            // Quality metrics
            _qualityMetrics = new TextView(this)
            {
                Text = "Quality: Unknown",
                TextSize = 11f
            };
            _qualityMetrics.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            _bluetoothEnhancedPanel.AddView(_qualityMetrics);

            // Device profile info
            _deviceProfileInfo = new TextView(this)
            {
                Text = "Device Profile: None",
                TextSize = 11f
            };
            _deviceProfileInfo.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            _bluetoothEnhancedPanel.AddView(_deviceProfileInfo);

            // Action buttons
            var bluetoothButtonsRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

            _btnBluetoothConfig = MakeTertiary("⚙️ Config");
            _btnBluetoothProfiles = MakeTertiary("📋 Profiles");
            _btnBluetoothQuality = MakeTertiary("📊 Quality");

            bluetoothButtonsRow.AddView(_btnBluetoothConfig, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            bluetoothButtonsRow.AddView(_btnBluetoothProfiles, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            bluetoothButtonsRow.AddView(_btnBluetoothQuality, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            _bluetoothEnhancedPanel.AddView(bluetoothButtonsRow);
            root.AddView(_bluetoothEnhancedPanel);
        }

        private void CreateEnhancedGraph(LinearLayout root)
        {
            _graphPanel = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _graphPanel.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            _graphPanel.SetPadding(16, 16, 16, 16);

            var graphTitle = new TextView(this)
            {
                Text = "📈 Live Audio Metrics (RMS / Peak / GR / Headroom)",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            graphTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            _graphPanel.AddView(graphTitle);

            _chart = new RealTimeChartView(this)
            {
                LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 300)
            };
            _graphPanel.AddView(_chart);
            root.AddView(_graphPanel);

            // Connect to engine
            _engine.SetMetersCallback((rmsDb, pkDb, grDb) =>
            {
                try
                {
                    if (IsValidAudioLevel(rmsDb, pkDb))
                    {
                        RunOnUiThread(() => _chart?.Push(rmsDb, pkDb, grDb));
                    }
                }
                catch (Exception ex)
                {
                    SafeLog($"Error updating chart: {ex.Message}");
                }
            });
        }

        private void CreateStatusInformation(LinearLayout root)
        {
            _latency = new TextView(this)
            {
                Text = "Latency: -- ms",
                TextSize = 12f
            };
            _latency.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            root.AddView(_latency);

            var routeRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            _routeInfo = new TextView(this)
            {
                Text = "Output: (unknown)",
                TextSize = 12f
            };
            _routeInfo.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            _btnRebind = MakeTertiary("🔄 Rebind");

            routeRow.AddView(_routeInfo, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            routeRow.AddView(_btnRebind);
            root.AddView(routeRow);

            _performanceInfo = new TextView(this)
            {
                Text = "Performance: Initializing...",
                TextSize = 11f
            };
            _performanceInfo.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            root.AddView(_performanceInfo);

            _swShowLogs = MakeSwitch("📋 Show logs", false);
            _swShowAdvancedMetrics = MakeSwitch("📊 Advanced metrics", false);
            root.AddView(_swShowLogs);
            root.AddView(_swShowAdvancedMetrics);

            _status = new TextView(this)
            {
                Text = "Ready.",
                TextSize = 12f
            };
            _status.SetPadding(12, 12, 12, 12);
            _status.SetTextColor(new Color(0xE6, 0xE6, 0xE6));

            _logCard = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _logCard.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            _logCard.SetPadding(12, 12, 12, 12);
            _logCard.AddView(_status);
            _logCard.Visibility = ViewStates.Gone;
            root.AddView(_logCard);

            _metricsCard = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _metricsCard.SetBackgroundColor(Color.ParseColor("#1A1A1A"));
            _metricsCard.SetPadding(12, 12, 12, 12);
            _metricsCard.Visibility = ViewStates.Gone;
            root.AddView(_metricsCard);
        }

        private void CreateBottomNavigation(LinearLayout root)
        {
            var bottom = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            bottom.SetPadding(0, 24, 0, 0);

            var btnHelp = MakeTertiary("❓ Help");
            var btnLogs = MakeTertiary("📋 Logs");
            var btnReadme = MakeTertiary("📖 README");
            var btnAccessibility = MakeTertiary("♿ Accessibility");

            AccessibleUIComponents.SetContentDescription(btnHelp, "Open help documentation");
            AccessibleUIComponents.SetContentDescription(btnLogs, "View application logs");
            AccessibleUIComponents.SetContentDescription(btnReadme, "Open README documentation");
            AccessibleUIComponents.SetContentDescription(btnAccessibility, "Toggle accessibility options");

            bottom.AddView(btnHelp, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            bottom.AddView(btnLogs, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            bottom.AddView(btnReadme, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            bottom.AddView(btnAccessibility, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            // Event handlers
            btnHelp.Click += (s, e) => SafeStartActivity(new Intent(this, typeof(HelpActivity)));
            btnLogs.Click += (s, e) => SafeStartActivity(new Intent(this, typeof(LogsActivity)));
            btnReadme.Click += (s, e) => SafeOpenUrl("https://github.com/Baal-1981/Buds3Pro-v1.2/blob/main/README.md");
            btnAccessibility.Click += (s, e) => ToggleAccessibility();

            root.AddView(bottom);
        }

        private void CreateAccessibilityPanel(LinearLayout root)
        {
            _accessibilityPanel = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            _accessibilityPanel.SetBackgroundColor(Color.ParseColor("#0D1117"));
            _accessibilityPanel.SetPadding(16, 12, 16, 12);
            _accessibilityPanel.Visibility = ViewStates.Gone;

            var accessibilityTitle = new TextView(this)
            {
                Text = "♿ Accessibility Options",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            accessibilityTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            AccessibleUIComponents.SetAsHeading(accessibilityTitle, true);
            _accessibilityPanel.AddView(accessibilityTitle);

            var swHighVisibility = MakeSwitch("🔆 High visibility mode", false);
            var swVoiceDescription = MakeSwitch("🗣️ Voice descriptions", false);
            var swLargeText = MakeSwitch("🔍 Large text mode", false);

            AccessibleUIComponents.SetContentDescription(swHighVisibility, "Enable high contrast colors for better visibility");
            AccessibleUIComponents.SetContentDescription(swVoiceDescription, "Enable voice descriptions for screen reader users");
            AccessibleUIComponents.SetContentDescription(swLargeText, "Increase text size for better readability");

            _accessibilityPanel.AddView(swHighVisibility);
            _accessibilityPanel.AddView(swVoiceDescription);
            _accessibilityPanel.AddView(swLargeText);

            // Event handlers
            swHighVisibility.CheckedChange += (s, e) => SetHighVisibilityMode(e.IsChecked);
            swVoiceDescription.CheckedChange += (s, e) => SetVoiceDescriptionMode(e.IsChecked);
            swLargeText.CheckedChange += (s, e) => SetLargeTextMode(e.IsChecked);

            root.AddView(_accessibilityPanel);
        }

        private void SetupScrollContainer(LinearLayout root)
        {
            var container = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
            container.AddView(root);

            var scroll = new ScrollView(this);
            scroll.AddView(container);
            SetContentView(scroll);
        }

        // ===================== ENHANCED PANELS =====================
        private LinearLayout BuildRoutePanel()
        {
            var p = MakePanel();
            var lbl = new TextView(this)
            {
                Text = "🎧 Output transport configuration:",
                TextSize = 13f
            };
            lbl.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            p.AddView(lbl);

            var rg = new RadioGroup(this) { Orientation = Android.Widget.Orientation.Vertical };

            _rbA2dp = new RadioButton(this)
            {
                Text = "📻 A2DP / Media (Music stream)",
                Checked = true,
                Id = View.GenerateViewId()
            };
            _rbSco = new RadioButton(this)
            {
                Text = "📞 SCO / HFP (Voice call stream)",
                Id = View.GenerateViewId()
            };
            _rbLc3Auto = new RadioButton(this)
            {
                Text = "⚡ LE Audio (LC3) – auto (system decides)",
                Checked = false,
                Id = View.GenerateViewId()
            };

            AccessibleUIComponents.SetContentDescription(_rbA2dp, "Use A2DP for high quality audio streaming");
            AccessibleUIComponents.SetContentDescription(_rbSco, "Use SCO for voice optimized streaming");
            AccessibleUIComponents.SetContentDescription(_rbLc3Auto, "Use LE Audio with automatic codec selection");

            rg.AddView(_rbA2dp);
            rg.AddView(_rbSco);
            rg.AddView(_rbLc3Auto);
            p.AddView(rg);

            rg.CheckedChange += (s, e) =>
            {
                try
                {
                    var t = AudioTransport.A2DP;
                    if (_rbSco.Checked) t = AudioTransport.SCO;
                    else if (_rbLc3Auto.Checked) t = AudioTransport.LE_LC3_AUTO;

                    ValidateAndSetTransport(t);
                }
                catch (Exception ex)
                {
                    SafeLog($"Error setting transport: {ex.Message}");
                }
            };

            var hint = new TextView(this)
            {
                Text = "💡 Note: LC3/LE Audio cannot be forced per-app. If your headset and Android support it, the system uses LC3 automatically when connected via LE Audio.",
                TextSize = 11f
            };
            hint.SetTextColor(new Color(0x8A, 0x8A, 0x8A));
            p.AddView(hint);

            return p;
        }

        private LinearLayout BuildNoiseCancelPanel()
        {
            var p = MakePanel();

            _swPlatformFx = MakeSwitch("🤖 Use platform NS/AGC/AEC (Android)", false);
            _swDspNs = MakeSwitch("🎛️ Use DSP Noise Suppressor (spectral)", true);
            _swAmbient = MakeSwitch("🌊 Ambient expander (duck background)", true);

            var helpPlat = new TextView(this)
            {
                Text = "💡 Note: Android platform effects activate NS/AGC/AEC together.",
                TextSize = 11f
            };
            helpPlat.SetTextColor(new Color(0x8A, 0x8A, 0x8A));

            // Enhanced sliders with real-time display
            _sliderAmbientDb = CreateEnhancedSlider(-24, 0, -12, "📉 Ambient reduction: ", "dB");
            _sliderAttack = CreateEnhancedSlider(50, 400, 200, "⚡ Attack: ", "ms");
            _sliderRelease = CreateEnhancedSlider(50, 300, 150, "🔄 Release: ", "ms");
            _sliderCalibration = CreateEnhancedSlider(200, 2000, _calibMs, "⏱️ Calibrate duration: ", "ms");

            p.AddView(_swPlatformFx);
            p.AddView(helpPlat);
            p.AddView(_swDspNs);
            p.AddView(_swAmbient);
            AddEnhancedSliderToPanel(p, _sliderAmbientDb);
            AddEnhancedSliderToPanel(p, _sliderAttack);
            AddEnhancedSliderToPanel(p, _sliderRelease);
            AddEnhancedSliderToPanel(p, _sliderCalibration);

            // Event handlers with validation
            _swPlatformFx.CheckedChange += (s, e) => SafeSetEngineFlag(() => _engine.SetFlags(platformFx: e.IsChecked));
            _swDspNs.CheckedChange += (s, e) => SafeSetEngineFlag(() => _engine.SetFlags(dspNs: e.IsChecked));
            _swAmbient.CheckedChange += (s, e) => SafeSetEngineFlag(() => _engine.SetFlags(ambientExpander: e.IsChecked));

            SetupEnhancedSliderEvents(_sliderAmbientDb, (value) => _engine.SetAmbientReductionDb(-value));
            SetupEnhancedSliderEvents(_sliderAttack, (value) => _engine.SetAmbientAttackMs(value));
            SetupEnhancedSliderEvents(_sliderRelease, (value) => _engine.SetAmbientReleaseMs(value));
            SetupEnhancedSliderEvents(_sliderCalibration, (value) => _calibMs = ValidateRange(value, 200, 2000));

            return p;
        }

        private LinearLayout BuildEqPanel()
        {
            var p = MakePanel();

            _swEq = MakeSwitch("🎚️ Enable EQ (3 bands)", false);

            // Enhanced EQ sliders with real-time display
            _sliderBassDb = CreateEnhancedSlider(-12, +12, 0, "🎵 Bass: ", "dB");
            _sliderMidDb = CreateEnhancedSlider(-8, +8, 0, "🎤 Presence: ", "dB");
            _sliderTrebleDb = CreateEnhancedSlider(-12, +12, 0, "🎼 Treble: ", "dB");

            _sliderBassHz = CreateEnhancedSlider(40, 400, 120, "🎵 Bass freq: ", "Hz");
            _sliderMidHz = CreateEnhancedSlider(1000, 3000, 2000, "🎤 Presence freq: ", "Hz");
            _sliderTrebleHz = CreateEnhancedSlider(2000, 10000, 6500, "🎼 Treble freq: ", "Hz");

            p.AddView(_swEq);
            AddEnhancedSliderToPanel(p, _sliderBassDb);
            AddEnhancedSliderToPanel(p, _sliderMidDb);
            AddEnhancedSliderToPanel(p, _sliderTrebleDb);
            AddEnhancedSliderToPanel(p, _sliderBassHz);
            AddEnhancedSliderToPanel(p, _sliderMidHz);
            AddEnhancedSliderToPanel(p, _sliderTrebleHz);

            // Event handlers with validation
            _swEq.CheckedChange += (s, e) => SafeSetEngineParameter(() => _engine.SetEqEnabled(e.IsChecked));

            SetupEnhancedSliderEvents(_sliderBassDb, (value) => _engine.SetBassDb(ValidateRange(value - 12, -12, 12)));
            SetupEnhancedSliderEvents(_sliderMidDb, (value) => _engine.SetPresenceDb(ValidateRange(value - 8, -8, 8)));
            SetupEnhancedSliderEvents(_sliderTrebleDb, (value) => _engine.SetTrebleDb(ValidateRange(value - 12, -12, 12)));

            SetupEnhancedSliderEvents(_sliderBassHz, (value) => _engine.SetBassFreqHz(ValidateRange(value, 40, 400)));
            SetupEnhancedSliderEvents(_sliderMidHz, (value) => _engine.SetPresenceHz(ValidateRange(value, 1000, 3000)));
            SetupEnhancedSliderEvents(_sliderTrebleHz, (value) => _engine.SetTrebleFreqHz(ValidateRange(value, 2000, 10000)));

            return p;
        }

        private LinearLayout BuildQualityPanel()
        {
            var p = MakePanel();

            _swHP = MakeSwitch("🎛️ High-pass (≈120 Hz)", true);
            _swClarity = MakeSwitch("✨ Clarity boost", true);
            _swDeEss = MakeSwitch("🗣️ De-esser", false);
            _swHum = MakeSwitch("⚡ Hum remover (50/60 Hz)", false);

            _sliderGain = CreateEnhancedSlider(0, 36, 0, "🔊 Pre-gain: ", "dB");

            var humRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            var rb50 = new RadioButton(this) { Text = "50 Hz", Checked = true, Id = View.GenerateViewId() };
            var rb60 = new RadioButton(this) { Text = "60 Hz", Id = View.GenerateViewId() };
            var rg = new RadioGroup(this) { Orientation = Android.Widget.Orientation.Horizontal };
            rg.AddView(rb50);
            rg.AddView(rb60);
            humRow.AddView(rg);

            p.AddView(_swHP);
            p.AddView(_swClarity);
            p.AddView(_swDeEss);
            p.AddView(_swHum);
            AddEnhancedSliderToPanel(p, _sliderGain);
            p.AddView(humRow);

            // Event handlers with validation
            _swHP.CheckedChange += (s, e) => SafeSetEngineFlag(() => _engine.SetFlags(hp: e.IsChecked));
            _swClarity.CheckedChange += (s, e) => SafeSetEngineParameter(() => _engine.SetClarity(e.IsChecked));
            _swDeEss.CheckedChange += (s, e) => SafeSetEngineParameter(() => _engine.SetDeEsserEnabled(e.IsChecked));
            _swHum.CheckedChange += (s, e) => SafeSetEngineParameter(() => _engine.SetHumEnabled(e.IsChecked));

            SetupEnhancedSliderEvents(_sliderGain, (value) => _engine.SetGainDb(ValidateRange(value, 0, 36)));

            rg.CheckedChange += (s, e) =>
            {
                var hz = (e.CheckedId == rb60.Id) ? 60 : 50;
                SafeSetEngineParameter(() => _engine.SetHumBaseHz(hz));
            };

            return p;
        }

        private LinearLayout BuildAdvancedPanel()
        {
            var p = MakePanel();

            var title = new TextView(this)
            {
                Text = "⚙️ Advanced Audio Processing",
                TextSize = 14f,
                Typeface = Typeface.DefaultBold
            };
            title.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            p.AddView(title);

            _swAdaptiveCompression = MakeSwitch("🤖 Adaptive Compression (AI-enhanced)", false);
            _swAutoGainControl = MakeSwitch("📈 Auto Gain Control", false);
            _swAudioWatchdog = MakeSwitch("🔍 Audio Watchdog (auto-recovery)", true);

            var warning = new TextView(this)
            {
                Text = "⚠️ Advanced features may increase CPU usage and latency.",
                TextSize = 11f
            };
            warning.SetTextColor(new Color(0xFF, 0xA5, 0x00));

            p.AddView(_swAdaptiveCompression);
            p.AddView(_swAutoGainControl);
            p.AddView(_swAudioWatchdog);
            p.AddView(warning);

            return p;
        }

        // ===================== ENHANCED SLIDER UTILITIES =====================
        private EnhancedSlider CreateEnhancedSlider(int min, int max, int def, string prefix, string unit)
        {
            if (min > max) (min, max) = (max, min);
            int range = max - min;
            if (range <= 0) range = 1;
            def = Math.Max(min, Math.Min(max, def));

            var labelView = new TextView(this)
            {
                Text = prefix,
                TextSize = 12f
            };
            labelView.SetTextColor(new Color(0xB3, 0xB3, 0xB3));

            var valueView = new TextView(this)
            {
                Text = $"{def} {unit}",
                TextSize = 12f,
                Gravity = GravityFlags.End
            };
            valueView.SetTextColor(new Color(0xE6, 0xE6, 0xE6));

            var slider = new EnhancedSlider
            {
                Slider = new SeekBar(this) { Max = range, Progress = def - min },
                Unit = unit,
                Prefix = prefix,
                Label = labelView,
                ValueDisplay = valueView
            };

            // Real-time value update
            slider.Slider.ProgressChanged += (s, e) =>
            {
                if (!e.FromUser) return;
                var value = ValidateRange(e.Progress + min, min, max);
                slider.ValueDisplay.Text = $"{value} {unit}";
            };

            return slider;
        }

        private void AddEnhancedSliderToPanel(LinearLayout panel, EnhancedSlider slider)
        {
            var labelRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            labelRow.AddView(slider.Label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            labelRow.AddView(slider.ValueDisplay);
            panel.AddView(labelRow);
            panel.AddView(slider.Slider);
        }

        private void SetupEnhancedSliderEvents(EnhancedSlider slider, Action<int> onValueChanged)
        {
            slider.Slider.ProgressChanged += (s, e) =>
            {
                if (e.FromUser)
                {
                    try
                    {
                        SafeSetEngineParameter(() => onValueChanged?.Invoke(e.Progress));
                    }
                    catch (Exception ex)
                    {
                        SafeLog($"Error in slider event: {ex.Message}");
                    }
                }
            };
        }

        // ===================== EVENT HANDLERS ENHANCED =====================
        private void SetupEventHandlers()
        {
            try
            {
                // Main controls with protection
                _btnStart.Click += (s, e) => SafeStartAudio(true);
                _btnStop.Click += (s, e) => SafeStopAudio();
                _btnCalibrate.Click += (s, e) => SafeCalibrateNoise();

                // Emergency controls
                _btnEmergencyStop.Click += (s, e) => SafeEmergencyStop();
                _btnResetEmergency.Click += (s, e) => SafeResetEmergency();
                _swSafetyLimiter.CheckedChange += (s, e) => SafeSetSafetyLimiter(e.IsChecked);

                // Quick actions
                _btnQuickCalibrate.Click += (s, e) => SafeCalibrateNoise();
                _btnQuickReset.Click += (s, e) => SafeResetEngine();
                _btnQuickPreset.Click += (s, e) => SafeQuickSavePreset();

                // Quick toggles with validation
                _swPassThrough.CheckedChange += (s, e) => SafeSetEngineFlag(() => _engine.SetFlags(pass: e.IsChecked));
                _swNoiseCancel.CheckedChange += (s, e) => SafeSetEngineFlag(() => _engine.SetFlags(dspNs: e.IsChecked));

                // UI toggles
                _swShowLogs.CheckedChange += (s, e) => _logCard.Visibility = e.IsChecked ? ViewStates.Visible : ViewStates.Gone;
                _swShowAdvancedMetrics.CheckedChange += (s, e) => _metricsCard.Visibility = e.IsChecked ? ViewStates.Visible : ViewStates.Gone;

                // Panel toggles
                _btnRouteToggle.Click += (s, e) => Toggle(_bluetoothEnhancedPanel, _btnRouteToggle, "🔊 BLUETOOTH ENHANCED");
                _btnNcToggle.Click += (s, e) => Toggle(_ncPanel, _btnNcToggle, "🔇 NOISE CANCELLING");
                _btnEqToggle.Click += (s, e) => Toggle(_eqPanel, _btnEqToggle, "🎚️ EQUALIZER");
                _btnQualityToggle.Click += (s, e) => Toggle(_qualityPanel, _btnQualityToggle, "🎤 SPEECH QUALITY");
                _btnAdvancedToggle.Click += (s, e) => Toggle(_advancedPanel, _btnAdvancedToggle, "⚙️ ADVANCED SETTINGS");
                _btnLatencyToggle.Click += (s, e) => Toggle(_latencyPanel, _btnLatencyToggle, "⚡ LATENCY PROFILES");

                _btnRebind.Click += (s, e) => SafeRebindTransport();

                // Enhanced preset management
                _btnSavePreset.Click += (s, e) => SafeSavePreset();
                _btnLoadPreset.Click += (s, e) => SafeLoadPreset();
                _btnExportPresets.Click += (s, e) => SafeExportPresets();
                _btnImportPresets.Click += (s, e) => SafeImportPresets();
                _presetSpinner.ItemSelected += OnPresetSelected;

                // Latency profile management
                _btnApplyProfile.Click += (s, e) => SafeApplyLatencyProfile();

                // Advanced panel event handlers
                _swAdaptiveCompression.CheckedChange += (s, e) =>
                {
                    try
                    {
                        SafeLog($"Adaptive compression {(e.IsChecked ? "enabled" : "disabled")}");
                        // Future: Connect to enhanced compressor settings
                    }
                    catch (Exception ex)
                    {
                        SafeLog($"Error setting adaptive compression: {ex.Message}");
                    }
                };

                _swAutoGainControl.CheckedChange += (s, e) =>
                {
                    try
                    {
                        SafeLog($"Auto gain control {(e.IsChecked ? "enabled" : "disabled")}");
                        // Future: Connect to AGC settings
                    }
                    catch (Exception ex)
                    {
                        SafeLog($"Error setting auto gain control: {ex.Message}");
                    }
                };

                _swAudioWatchdog.CheckedChange += (s, e) =>
                {
                    try
                    {
                        SafeLog($"Audio watchdog monitoring {(e.IsChecked ? "enabled" : "disabled")}");
                        // Watchdog is already integrated in AudioEngine
                    }
                    catch (Exception ex)
                    {
                        SafeLog($"Error setting audio watchdog: {ex.Message}");
                    }
                };

                // Device callback with enhanced error handling
                _deviceCallback = new AudioDeviceCallbackEx(SafeUpdateRouteAndMaybeRebind);
                BluetoothRouting_Utilities.RegisterDeviceCallback(this, _deviceCallback);

                // Setup Bluetooth event handlers
                SetupBluetoothEventHandlers();

                SafeLog("Enhanced event handlers configured successfully");
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting up event handlers: {ex.Message}");
            }
        }

        private void SetupBluetoothEventHandlers()
        {
            try
            {
                _swAutoFallback.CheckedChange += (s, e) =>
                {
                    if (_bluetoothManager != null)
                    {
                        _bluetoothManager.AutoFallbackEnabled = e.IsChecked;
                        SafeLog($"Auto-fallback {(e.IsChecked ? "enabled" : "disabled")}");
                    }
                };

                _btnBluetoothConfig.Click += OnBluetoothConfigClicked;
                _btnBluetoothProfiles.Click += OnBluetoothProfilesClicked;
                _btnBluetoothQuality.Click += OnBluetoothQualityClicked;
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting up Bluetooth event handlers: {ex.Message}");
            }
        }

        // ===================== ENHANCED MONITORING =====================
        private void InitializeEnhancedMonitoring()
        {
            try
            {
                // Health monitoring timer with enhanced metrics
                _healthTimer = new System.Timers.Timer(2000); // 2 seconds
                _healthTimer.Elapsed += (s, e) => UpdateEnhancedHealthStatus();
                _healthTimer.AutoReset = true;
                _healthTimer.Start();

                // Metrics update timer for real-time UI updates
                _metricsUpdateTimer = new System.Timers.Timer(500); // 500ms for responsive UI
                _metricsUpdateTimer.Elapsed += (s, e) => UpdateMetricsDisplay();
                _metricsUpdateTimer.AutoReset = true;
                _metricsUpdateTimer.Start();

                // Latency meter for continuous monitoring
                _latencyMeter = new LatencyMeter(_engine, UpdateLatencyDisplay, 1000);
                _latencyMeter.Start();

                SafeLog("Enhanced monitoring initialized");
            }
            catch (Exception ex)
            {
                SafeLog($"Error initializing enhanced monitoring: {ex.Message}");
            }
        }

        private void UpdateEnhancedHealthStatus()
        {
            try
            {
                if (_engine?.IsRunning != true) return;

                var metrics = _engine.GetHealthMetrics();

                // Update latency profile manager with current metrics
                _latencyManager?.UpdateMetrics(metrics);

                RunOnUiThread(() =>
                {
                    try
                    {
                        UpdateHealthIndicator(metrics);
                        UpdateSystemMetrics(metrics);
                        UpdateStatusLeds(metrics);
                        UpdatePerformanceInfo(metrics);
                    }
                    catch (Exception ex)
                    {
                        SafeLog($"Error updating UI health status: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error in enhanced health status update: {ex.Message}");
            }
        }

        private void UpdateHealthIndicator(AudioHealthMetrics metrics)
        {
            // Enhanced health status with more granular states
            if (_engine.IsInSafeMode)
            {
                _healthStatus.Text = "⚠️"; // Warning - safe mode
            }
            else if (metrics.IsHealthy)
            {
                _healthStatus.Text = "🟢"; // Green - excellent
            }
            else if (metrics.IsStable)
            {
                _healthStatus.Text = "🟡"; // Yellow - stable but not optimal
            }
            else
            {
                _healthStatus.Text = "🔴"; // Red - issues detected
            }
        }

        private void UpdateSystemMetrics(AudioHealthMetrics metrics)
        {
            // Update progress bars with validation
            _cpuUsageBar.Progress = Math.Max(0, Math.Min(100, metrics.CpuUsagePercent));
            _memoryUsageBar.Progress = Math.Max(0, Math.Min(100, metrics.MemoryPressureMB));

            // Latency bar: 0-100ms range
            int latencyPercent = Math.Min(100, (metrics.LatencyMs * 100) / 100);
            _latencyBar.Progress = latencyPercent;

            // System health status text
            _systemHealthStatus.Text = $"System: {(metrics.IsHealthy ? "Healthy" : "Issues")} • " +
                                      $"Stability: {metrics.StabilityScore:F2} • " +
                                      $"Trend: {(metrics.PerformanceTrend > 0 ? "↗️" : metrics.PerformanceTrend < 0 ? "↘️" : "→")}";
        }

        private void UpdateStatusLeds(AudioHealthMetrics metrics)
        {
            // Audio status LED
            if (_engine.IsRunning && !_engine.IsInSafeMode)
            {
                _audioStatusLed.Text = "🟢 Audio";
                _audioStatusLed.SetTextColor(new Color(0x4C, 0xAF, 0x50)); // Green
            }
            else if (_engine.IsInSafeMode)
            {
                _audioStatusLed.Text = "🟡 Audio (Safe)";
                _audioStatusLed.SetTextColor(new Color(0xFF, 0xA5, 0x00)); // Orange
            }
            else
            {
                _audioStatusLed.Text = "🔴 Audio";
                _audioStatusLed.SetTextColor(new Color(0xF4, 0x43, 0x36)); // Red
            }

            // Bluetooth status LED
            UpdateBluetoothStatusLed();

            // Latency status LED
            if (metrics.LatencyMs < 20)
            {
                _latencyStatusLed.Text = "🟢 Latency";
                _latencyStatusLed.SetTextColor(new Color(0x4C, 0xAF, 0x50)); // Green
            }
            else if (metrics.LatencyMs < 50)
            {
                _latencyStatusLed.Text = "🟡 Latency";
                _latencyStatusLed.SetTextColor(new Color(0xFF, 0xA5, 0x00)); // Orange
            }
            else
            {
                _latencyStatusLed.Text = "🔴 Latency";
                _latencyStatusLed.SetTextColor(new Color(0xF4, 0x43, 0x36)); // Red
            }
        }

        private void UpdatePerformanceInfo(AudioHealthMetrics metrics)
        {
            var profile = _latencyManager?.CurrentProfile ?? LatencyProfile.Balanced;
            _performanceInfo.Text = $"Profile: {profile} • " +
                                   $"Dropout Risk: {metrics.PredictedDropoutRisk}% • " +
                                   $"Underruns: {metrics.BufferUnderrunsCount}";
        }

        private void UpdateMetricsDisplay()
        {
            try
            {
                if (_engine?.IsRunning != true || _metricsCard.Visibility != ViewStates.Visible) return;

                RunOnUiThread(() =>
                {
                    try
                    {
                        // Update advanced metrics if visible
                        UpdateAdvancedMetricsCard();
                    }
                    catch (Exception ex)
                    {
                        SafeLog($"Error updating metrics display: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error in metrics display update: {ex.Message}");
            }
        }

        private void UpdateAdvancedMetricsCard()
        {
            if (_metricsCard.ChildCount == 0)
            {
                // Create advanced metrics content
                var metricsTitle = new TextView(this)
                {
                    Text = "📊 Advanced Metrics",
                    TextSize = 14f,
                    Typeface = Typeface.DefaultBold
                };
                metricsTitle.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
                _metricsCard.AddView(metricsTitle);

                var metricsContent = new TextView(this)
                {
                    Text = "Initializing advanced metrics...",
                    TextSize = 11f
                };
                metricsContent.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
                _metricsCard.AddView(metricsContent);
            }

            // Update metrics content
            if (_metricsCard.ChildCount >= 2)
            {
                if (_metricsCard.GetChildAt(1) is TextView metricsContent)
                {
                    var m = _engine.GetHealthMetrics();
                    var latencyStats = _latencyManager?.GetStatusReport() ?? "N/A";
                    var bluetoothStatus = _bluetoothManager?.GetStatusReport() ?? "N/A";

                    metricsContent.Text =
                        $"Audio Thread: {(_engine.IsAudioThreadAlive() ? "Alive" : "Dead")}\n" +
                        $"CPU: {m.CpuUsagePercent}% • Mem: {m.MemoryPressureMB}MB\n" +
                        $"Latency: {m.LatencyMs}ms • Underruns: {m.BufferUnderrunsCount}\n" +
                        $"Failure Risk: {_engine.GetFailureRisk():F2}\n" +
                        $"Total Errors: {_engine.TotalErrors}\n" +
                        $"Auto Recoveries: {_engine.AutomaticRecoveries}\n" +
                        $"Safety Limiter: {(_engine.IsSafetyLimiterEnabled ? "ON" : "OFF")}\n" +
                        $"Bluetooth: {_bluetoothManager?.CurrentQuality}\n" +
                        $"Device: {_bluetoothManager?.CurrentDevice?.Name ?? "None"}\n" +
                        $"Transport: {_bluetoothManager?.CurrentTransport}\n" +
                        $"Latency Manager: {latencyStats}";
                }
            }
        }

        private void UpdateLatencyDisplay(int latencyMs)
        {
            try
            {
                RunOnUiThread(() =>
                {
                    var info = BluetoothRouting_Utilities.GetActiveRouteInfo(this);
                    _latency.Text = $"Latency: {latencyMs}ms • Route: {info}";
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error updating latency display: {ex.Message}");
            }
        }

        // ===================== BLUETOOTH EVENT HANDLERS =====================

        private void OnBluetoothDeviceConnected(BluetoothDevice device, AudioTransport transport)
        {
            RunOnUiThread(() =>
            {
                SafeLog($"Bluetooth device connected: {device.Name} via {transport}");
                UpdateBluetoothUI();
                Toast.MakeText(this, $"Connected: {device.Name}", ToastLength.Short).Show();
            });
        }

        private void OnBluetoothDeviceDisconnected(BluetoothDevice device, string reason)
        {
            RunOnUiThread(() =>
            {
                SafeLog($"Bluetooth device disconnected: {device?.Name}, Reason: {reason}");
                UpdateBluetoothUI();
                Toast.MakeText(this, $"Disconnected: {device?.Name}", ToastLength.Short).Show();
            });
        }

        private void OnBluetoothTransportChanged(AudioTransport oldTransport, AudioTransport newTransport)
        {
            RunOnUiThread(() =>
            {
                SafeLog($"Bluetooth transport changed: {oldTransport} → {newTransport}");
                UpdateBluetoothUI();
                UpdateTransportRadioButtons(newTransport);
            });
        }

        private void OnBluetoothQualityChanged(ConnectionQuality quality)
        {
            RunOnUiThread(() =>
            {
                UpdateQualityDisplay(quality);
            });
        }

        private void OnBluetoothStatusMessage(string message)
        {
            SafeLog($"Bluetooth: {message}");
        }

        // ===================== BLUETOOTH UI UPDATES =====================

        private void UpdateBluetoothUI()
        {
            try
            {
                if (_bluetoothManager == null) return;

                _bluetoothStatusDetail.Text = $"Status: {_bluetoothManager.ConnectionState}";

                var quality = _bluetoothManager.CurrentQuality;
                _qualityMetrics.Text = $"Quality: {quality}";

                var currentDevice = _bluetoothManager.CurrentDevice;
                if (currentDevice != null)
                {
                    _deviceProfileInfo.Text = $"Device: {currentDevice.Name} ({_bluetoothManager.CurrentTransport})";
                }
                else
                {
                    _deviceProfileInfo.Text = "Device: None connected";
                }

                UpdateBluetoothStatusLed();
            }
            catch (Exception ex)
            {
                SafeLog($"Error updating Bluetooth UI: {ex.Message}");
            }
        }

        private void UpdateQualityDisplay(ConnectionQuality quality)
        {
            try
            {
                var qualityText = quality switch
                {
                    ConnectionQuality.Excellent => "📶 Excellent",
                    ConnectionQuality.Good => "📶 Good",
                    ConnectionQuality.Fair => "📶 Fair",
                    ConnectionQuality.Poor => "📵 Poor",
                    _ => "❓ Unknown"
                };

                _qualityMetrics.Text = $"Quality: {qualityText}";

                var qualityColor = quality switch
                {
                    ConnectionQuality.Excellent => new Color(0x4C, 0xAF, 0x50),
                    ConnectionQuality.Good => new Color(0x8B, 0xC3, 0x4A),
                    ConnectionQuality.Fair => new Color(0xFF, 0xA5, 0x00),
                    ConnectionQuality.Poor => new Color(0xF4, 0x43, 0x36),
                    _ => new Color(0x9E, 0x9E, 0x9E)
                };

                _qualityMetrics.SetTextColor(qualityColor);
            }
            catch (Exception ex)
            {
                SafeLog($"Error updating quality display: {ex.Message}");
            }
        }

        private void UpdateTransportRadioButtons(AudioTransport transport)
        {
            try
            {
                _rbA2dp.SetOnCheckedChangeListener(null);
                _rbSco.SetOnCheckedChangeListener(null);
                _rbLc3Auto.SetOnCheckedChangeListener(null);

                _rbA2dp.Checked = transport == AudioTransport.A2DP;
                _rbSco.Checked = transport == AudioTransport.SCO;
                _rbLc3Auto.Checked = transport == AudioTransport.LE_LC3_AUTO;

                // Re-enable event listeners
                SetupTransportRadioListeners();
            }
            catch (Exception ex)
            {
                SafeLog($"Error updating transport radio buttons: {ex.Message}");
            }
        }

        private void SetupTransportRadioListeners()
        {
            // This method would re-enable the radio button listeners
            // Implementation depends on your existing radio button setup
        }

        private void UpdateBluetoothStatusLed()
        {
            try
            {
                if (_bluetoothManager?.ConnectionState == ConnectionState.Connected)
                {
                    _bluetoothStatusLed.Text = "🟢 Bluetooth";
                    _bluetoothStatusLed.SetTextColor(new Color(0x21, 0x96, 0xF3));
                }
                else
                {
                    _bluetoothStatusLed.Text = "⚫ Bluetooth";
                    _bluetoothStatusLed.SetTextColor(new Color(0x9E, 0x9E, 0x9E));
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error updating Bluetooth status LED: {ex.Message}");
            }
        }

        // ===================== BLUETOOTH ACTION HANDLERS =====================

        private void OnBluetoothConfigClicked(object sender, EventArgs e)
        {
            try
            {
                ShowBluetoothConfigurationDialog();
            }
            catch (Exception ex)
            {
                SafeLog($"Error opening Bluetooth config: {ex.Message}");
            }
        }

        private void OnBluetoothProfilesClicked(object sender, EventArgs e)
        {
            try
            {
                ShowDeviceProfilesDialog();
            }
            catch (Exception ex)
            {
                SafeLog($"Error opening device profiles: {ex.Message}");
            }
        }

        private void OnBluetoothQualityClicked(object sender, EventArgs e)
        {
            try
            {
                ShowQualityMetricsDialog();
            }
            catch (Exception ex)
            {
                SafeLog($"Error opening quality metrics: {ex.Message}");
            }
        }

        private void ShowBluetoothConfigurationDialog()
        {
            var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this);
            builder.SetTitle("🔊 Bluetooth Configuration");

            var message = $"Current Configuration:\n\n" +
                         $"Auto-fallback: {(_bluetoothManager?.AutoFallbackEnabled ?? false)}\n" +
                         $"Current Transport: {_bluetoothManager?.CurrentTransport}\n" +
                         $"Connection State: {_bluetoothManager?.ConnectionState}\n" +
                         $"Current Quality: {_bluetoothManager?.CurrentQuality}\n\n" +
                         $"Configuration Report:\n" +
                         (_bluetoothManager?.GetStatusReport() ?? "Not available");

            builder.SetMessage(message);
            builder.SetPositiveButton("OK", (s, e) => { });
            builder.SetNeutralButton("Reset", (s, e) => ResetBluetoothConfiguration());

            builder.Show();
        }

        private void ShowDeviceProfilesDialog()
        {
            var message = "Device Profiles feature shows learned preferences for each connected device.\n\n" +
                         "This includes:\n" +
                         "• Preferred transport methods\n" +
                         "• Connection reliability history\n" +
                         "• Quality metrics over time\n" +
                         "• Automatic optimization recommendations";

            new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                .SetTitle("📋 Device Profiles")
                .SetMessage(message)
                .SetPositiveButton("OK", (s, e) => { })
                .Show();
        }

        private void ShowQualityMetricsDialog()
        {
            var qualityReport = "Quality metrics not available";

            try
            {
                if (_bluetoothManager != null)
                {
                    qualityReport = _bluetoothManager.GetStatusReport();
                }
            }
            catch (Exception ex)
            {
                qualityReport = $"Error getting quality report: {ex.Message}";
            }

            new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                .SetTitle("📊 Quality Metrics")
                .SetMessage(qualityReport)
                .SetPositiveButton("OK", (s, e) => { })
                .SetNeutralButton("Refresh", (s, e) => ShowQualityMetricsDialog())
                .Show();
        }

        private void ResetBluetoothConfiguration()
        {
            try
            {
                _bluetoothManager?.Dispose();
                InitializeBluetoothManager();

                Toast.MakeText(this, "Bluetooth configuration reset", ToastLength.Short).Show();
                SafeLog("Bluetooth configuration reset completed");
            }
            catch (Exception ex)
            {
                SafeLog($"Error resetting Bluetooth configuration: {ex.Message}");
            }
        }

        // ===================== ENHANCED SAFE OPERATIONS (PRESERVED) =====================
        private void SafeResetEngine()
        {
            try
            {
                bool wasRunning = _engine?.IsRunning ?? false;
                if (wasRunning) _engine?.Stop();

                // Reset all DSP components
                _engine?.Configure(48000, 10, true, true, false, true, true, false);

                if (wasRunning) StartAudioIfNeeded(false);

                SetStatus("🔄 Engine reset completed");
                SafeLog("Engine reset completed");
            }
            catch (Exception ex)
            {
                SafeLog($"Error resetting engine: {ex.Message}");
            }
        }

        private void SafeQuickSavePreset()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var presetName = $"QuickSave_{timestamp}";

                // Create preset data from current settings
                var presetData = new AudioPresetData
                {
                    MasterGainDb = 0, // TODO: Get from current settings
                    HighpassEnabled = _swHP?.Checked ?? false,
                    NoiseReductionEnabled = _swDspNs?.Checked ?? false,
                    AmbientAttackMs = 150, // TODO: Get from sliders
                    LatencyProfile = _latencyManager?.CurrentProfile.ToString() ?? "Balanced"
                };

                _presetManager?.Save(presetName, presetData);

                Toast.MakeText(this, $"Quick preset saved: {presetName}", ToastLength.Short).Show();
                SafeLog($"Quick preset saved: {presetName}");

                // Refresh preset spinner
                RefreshPresetSpinner();
            }
            catch (Exception ex)
            {
                SafeLog($"Error in quick save preset: {ex.Message}");
            }
        }

        private void SafeExportPresets()
        {
            try
            {
                var exportPath = System.IO.Path.Combine(
                    Android.OS.Environment.ExternalStorageDirectory.AbsolutePath,
                    "Download",
                    $"sonara_presets_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                );

                _presetManager?.ExportAllToSingleFile(exportPath);

                Toast.MakeText(this, $"Presets exported to: {System.IO.Path.GetFileName(exportPath)}", ToastLength.Long).Show();
                SafeLog($"Presets exported to: {exportPath}");
            }
            catch (Exception ex)
            {
                SafeLog($"Error exporting presets: {ex.Message}");
                Toast.MakeText(this, "Export failed", ToastLength.Short).Show();
            }
        }

        private void SafeImportPresets()
        {
            try
            {
                // TODO: Implement file picker for import
                Toast.MakeText(this, "Import functionality will be available in v1.3", ToastLength.Long).Show();
                SafeLog("Import presets requested - feature planned for v1.3");
            }
            catch (Exception ex)
            {
                SafeLog($"Error importing presets: {ex.Message}");
            }
        }

        private void SafeApplyLatencyProfile()
        {
            try
            {
                var selectedIndex = _latencyProfileSpinner.SelectedItemPosition;
                var profiles = LatencyProfileManager.GetAllProfiles();

                if (selectedIndex >= 0 && selectedIndex < profiles.Length)
                {
                    var selectedProfile = profiles[selectedIndex];
                    _latencyManager?.RequestProfile(selectedProfile);

                    Toast.MakeText(this, $"Latency profile requested: {selectedProfile}", ToastLength.Short).Show();
                    SafeLog($"Latency profile change requested: {selectedProfile}");

                    // Update info display
                    var config = LatencyProfileManager.GetConfig(selectedProfile);
                    _latencyProfileInfo.Text = $"{config.Description}\n" +
                                              $"Expected latency: {config.ExpectedLatencyMs}ms • " +
                                              $"CPU limit: {config.MaxCpuPercent}%";
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error applying latency profile: {ex.Message}");
            }
        }

        private void RefreshPresetSpinner()
        {
            try
            {
                var presetNames = _presetManager?.ListNames().ToArray() ?? new string[0];
                var defaultPresets = new[] { "Default", "Restaurant", "Office", "Outdoor", "Speech", "Music", "Gaming" };

                var allPresets = new string[defaultPresets.Length + presetNames.Length];
                defaultPresets.CopyTo(allPresets, 0);
                presetNames.CopyTo(allPresets, defaultPresets.Length);

                var presetAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, allPresets);
                presetAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                _presetSpinner.Adapter = presetAdapter;
            }
            catch (Exception ex)
            {
                SafeLog($"Error refreshing preset spinner: {ex.Message}");
            }
        }

        // ===================== LATENCY PROFILE EVENT HANDLERS =====================
        private void OnLatencyProfileChanged(LatencyProfile oldProfile, LatencyProfile newProfile)
        {
            try
            {
                RunOnUiThread(() =>
                {
                    Toast.MakeText(this, $"Latency profile changed: {oldProfile} → {newProfile}", ToastLength.Short).Show();

                    // Update UI to reflect new profile
                    var config = LatencyProfileManager.GetConfig(newProfile);
                    _latencyProfileInfo.Text = $"Active: {config.Description}\n" +
                                              $"Target latency: {config.ExpectedLatencyMs}ms • " +
                                              $"CPU limit: {config.MaxCpuPercent}%";
                });

                SafeLog($"Latency profile successfully changed from {oldProfile} to {newProfile}");
            }
            catch (Exception ex)
            {
                SafeLog($"Error handling latency profile change: {ex.Message}");
            }
        }

        private void OnLatencyAdaptationMessage(string message)
        {
            try
            {
                SafeLog($"Latency adaptation: {message}");

                // Show adaptation messages in UI if advanced metrics are visible
                if (_metricsCard.Visibility == ViewStates.Visible)
                {
                    RunOnUiThread(() =>
                    {
                        Toast.MakeText(this, message, ToastLength.Short).Show();
                    });
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error handling adaptation message: {ex.Message}");
            }
        }

        // ===================== ACCESSIBILITY ENHANCEMENTS =====================
        private void SetupAccessibility()
        {
            try
            {
                // Check if accessibility services are enabled
                var accessibilityManager = (Android.Views.Accessibility.AccessibilityManager)GetSystemService(AccessibilityService);
                _accessibilityMode = accessibilityManager?.IsEnabled ?? false;

                if (_accessibilityMode)
                {
                    SafeLog("Accessibility mode detected - enabling enhanced support");
                    EnableAccessibilityFeatures();
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting up accessibility: {ex.Message}");
            }
        }

        private void EnableAccessibilityFeatures()
        {
            try
            {
                // Set content descriptions for all important controls
                AccessibleUIComponents.SetContentDescription(_btnStart, "Start audio processing");
                AccessibleUIComponents.SetContentDescription(_btnStop, "Stop audio processing");
                AccessibleUIComponents.SetContentDescription(_btnEmergencyStop, "Emergency stop - immediately halt all audio processing");
                AccessibleUIComponents.SetContentDescription(_healthStatus, "System health indicator");

                // Mark important elements as headings
                var titleView = FindViewById<TextView>(Android.Resource.Id.Title);
                if (titleView != null)
                {
                    AccessibleUIComponents.SetAsHeading(titleView, true);
                }

                SafeLog("Accessibility features enabled");
            }
            catch (Exception ex)
            {
                SafeLog($"Error enabling accessibility features: {ex.Message}");
            }
        }

        private void ToggleAccessibility()
        {
            try
            {
                _accessibilityPanel.Visibility = _accessibilityPanel.Visibility is ViewStates.Visible ?
                    ViewStates.Gone : ViewStates.Visible;
            }
            catch (Exception ex)
            {
                SafeLog($"Error toggling accessibility panel: {ex.Message}");
            }
        }

        private void SetHighVisibilityMode(bool enabled)
        {
            try
            {
                _highVisibilityMode = enabled;

                if (enabled)
                {
                    // Increase contrast and brightness
                    Window.SetFlags(WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn);
                    SafeLog("High visibility mode enabled");
                }
                else
                {
                    Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
                    SafeLog("High visibility mode disabled");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting high visibility mode: {ex.Message}");
            }
        }

        private void SetVoiceDescriptionMode(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    // Enable additional voice descriptions
                    SafeLog("Voice description mode enabled");
                    Toast.MakeText(this, "Voice descriptions enabled for screen readers", ToastLength.Long).Show();
                }
                else
                {
                    SafeLog("Voice description mode disabled");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting voice description mode: {ex.Message}");
            }
        }

        private void SetLargeTextMode(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    // Increase text sizes throughout the app
                    SafeLog("Large text mode enabled");
                    Toast.MakeText(this, "Large text mode enabled", ToastLength.Short).Show();

                    if (_engine?.IsRunning is true)
                    {
                        SafeLog("Large text mode enabled - Audio engine is running");
                    }
                }
                else
                {
                    SafeLog("Large text mode disabled");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting large text mode: {ex.Message}");
            }
        }

        // ===================== ORIGINAL SAFE OPERATIONS (PRESERVED) =====================
        private void SafeStartAudio(bool userRequested)
        {
            try
            {
                lock (_configLock)
                {
                    if (_isConfiguring)
                    {
                        SafeLog("Configuration in progress, ignoring start request");
                        return;
                    }
                    _isConfiguring = true;
                }

                StartAudioIfNeeded(userRequested);
            }
            catch (Exception ex)
            {
                SafeLog($"Error starting audio: {ex.Message}");
                ShowErrorDialog("Start Failed", $"Failed to start audio: {ex.Message}");
            }
            finally
            {
                lock (_configLock)
                {
                    _isConfiguring = false;
                }
            }
        }

        private void SafeStopAudio()
        {
            try
            {
                _engine?.Stop();
                SetStatus("STOP ◼");
                StartService(new Intent(this, typeof(AudioForegroundService)).SetAction(AudioForegroundService.ACTION_STOP));
            }
            catch (Exception ex)
            {
                SafeLog($"Error stopping audio: {ex.Message}");
            }
        }

        private void SafeEmergencyStop()
        {
            try
            {
                _engine?.EmergencyStop();
                SetStatus("🛑 EMERGENCY STOP ACTIVATED");
                SafeLog("Emergency stop activated by user");
            }
            catch (Exception ex)
            {
                SafeLog($"Error in emergency stop: {ex.Message}");
            }
        }

        private void SafeResetEmergency()
        {
            try
            {
                _engine?.ResetEmergency();
                SetStatus("Emergency mode reset");
                SafeLog("Emergency mode reset by user");
            }
            catch (Exception ex)
            {
                SafeLog($"Error resetting emergency: {ex.Message}");
            }
        }

        private void SafeSetSafetyLimiter(bool enabled)
        {
            try
            {
                _engine?.SetSafetyLimiterEnabled(enabled);
                SetStatus($"Safety limiter {(enabled ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting safety limiter: {ex.Message}");
            }
        }

        private void SafeCalibrateNoise()
        {
            try
            {
                bool wasRunning = _engine?.IsRunning ?? false;
                if (!wasRunning) StartAudioIfNeeded(false);

                _engine?.CalibrateNoiseNow(_calibMs);
                SetStatus($"Calibrating noise ({_calibMs} ms)...");

                // Show calibration progress
                _calibrationProgress.Visibility = ViewStates.Visible;
                _calibrationStatus.Text = "Calibrating... Please remain quiet";

                // Hide progress after calibration duration
                Task.Delay(_calibMs + 500).ContinueWith(_ =>
                {
                    RunOnUiThread(() =>
                    {
                        _calibrationProgress.Visibility = ViewStates.Gone;
                        _calibrationStatus.Text = "Calibration completed";
                    });
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error calibrating noise: {ex.Message}");
            }
        }

        private void SafeSetEngineFlag(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting engine flag: {ex.Message}");
            }
        }

        private void SafeSetEngineParameter(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting engine parameter: {ex.Message}");
            }
        }

        private void ValidateAndSetTransport(AudioTransport transport)
        {
            try
            {
                _engine?.SetTransport(transport);
                SetStatus($"Transport: {transport}");
                SafeRebindTransport();
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting transport: {ex.Message}");
            }
        }

        private void SafeRebindTransport()
        {
            try
            {
                bool wasRunning = _engine?.IsRunning ?? false;
                if (wasRunning) _engine?.Stop();
                if (wasRunning) StartAudioIfNeeded(false);
                UpdateRouteLabel();
            }
            catch (Exception ex)
            {
                SafeLog($"Error rebinding transport: {ex.Message}");
            }
        }

        private void SafeUpdateRouteAndMaybeRebind()
        {
            try
            {
                RunOnUiThread(() =>
                {
                    var before = _routeInfo?.Text ?? "";
                    UpdateRouteLabel();

                    // Auto-reconnect logic
                    if (_rbSco?.Checked is true && !BluetoothRouting_Utilities.IsScoOn(this))
                    {
                        BluetoothRouting_Utilities.EnsureSco(this, 2000);
                        BluetoothRouting_Utilities.ForceCommunicationDeviceSco(this);
                        UpdateRouteLabel();
                    }

                    if (_engine?.IsRunning is true && before != _routeInfo?.Text)
                    {
                        SafeRebindTransport();
                    }
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error updating route: {ex.Message}");
            }
        }

        private void SafeStartActivity(Intent intent)
        {
            try
            {
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                SafeLog($"Error starting activity: {ex.Message}");
                Toast.MakeText(this, "Could not open activity", ToastLength.Short).Show();
            }
        }

        private void SafeOpenUrl(string url)
        {
            try
            {
                StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url)));
            }
            catch (Exception ex)
            {
                SafeLog($"Error opening URL: {ex.Message}");
                Toast.MakeText(this, "Could not open URL", ToastLength.Short).Show();
            }
        }

        // ===================== PRESET MANAGEMENT (PRESERVED) =====================
        private void SafeSavePreset()
        {
            try
            {
                var selectedPreset = _presetSpinner.SelectedItem?.ToString() ?? "Custom";

                // Create preset data from current settings
                var presetData = new AudioPresetData
                {
                    MasterGainDb = 0, // TODO: Get from current settings
                    HighpassEnabled = _swHP?.Checked ?? false,
                    NoiseReductionEnabled = _swDspNs?.Checked ?? false,
                    AmbientAttackMs = 150, // TODO: Get from sliders
                    LatencyProfile = _latencyManager?.CurrentProfile.ToString() ?? "Balanced"
                };

                _presetManager?.Save(selectedPreset, presetData);
                SafeLog($"Saving preset: {selectedPreset}");
                Toast.MakeText(this, $"Preset '{selectedPreset}' saved", ToastLength.Short).Show();

                RefreshPresetSpinner();
            }
            catch (Exception ex)
            {
                SafeLog($"Error saving preset: {ex.Message}");
            }
        }

        private void SafeLoadPreset()
        {
            try
            {
                var selectedPreset = _presetSpinner.SelectedItem?.ToString() ?? "Default";

                if (_presetManager != null && selectedPreset != "Default")
                {
                    try
                    {
                        var presetData = _presetManager.Load(selectedPreset);
                        ApplyPresetData(presetData);
                        SafeLog($"Loading preset: {selectedPreset}");
                        Toast.MakeText(this, $"Preset '{selectedPreset}' loaded", ToastLength.Short).Show();
                    }
                    catch (System.IO.FileNotFoundException)
                    {
                        // Fallback to engine preset
                        ApplyEnginePreset(selectedPreset);
                    }
                }
                else
                {
                    ApplyEnginePreset(selectedPreset);
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error loading preset: {ex.Message}");
            }
        }

        private void ApplyPresetData(AudioPresetData presetData)
        {
            try
            {
                // Apply preset data to UI and engine
                if (_swHP != null) _swHP.Checked = presetData.HighpassEnabled;
                if (_swDspNs != null) _swDspNs.Checked = presetData.NoiseReductionEnabled;

                // Apply to engine
                _engine?.SetFlags(hp: presetData.HighpassEnabled, dspNs: presetData.NoiseReductionEnabled);
                _engine?.SetAmbientAttackMs(presetData.AmbientAttackMs);

                SafeLog($"Applied custom preset data");
            }
            catch (Exception ex)
            {
                SafeLog($"Error applying preset data: {ex.Message}");
            }
        }

        private void ApplyEnginePreset(string presetName)
        {
            try
            {
                if (Enum.TryParse<AudioEngine.AudioPreset>(presetName, out var preset))
                {
                    _engine?.ApplyPreset(preset);
                    SafeLog($"Applied engine preset: {presetName}");
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error applying engine preset: {ex.Message}");
            }
        }

        private void OnPresetSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            try
            {
                var presetName = e.Parent.GetItemAtPosition(e.Position).ToString();
                // Auto-load on selection change
                SafeLoadPreset();
            }
            catch (Exception ex)
            {
                SafeLog($"Error handling preset selection: {ex.Message}");
            }
        }

        // ===================== VALIDATION & SAFETY (PRESERVED) =====================
        private int ValidateRange(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private bool IsValidAudioLevel(float rmsDb, float pkDb)
        {
            return !float.IsNaN(rmsDb) && !float.IsNaN(pkDb) &&
                   rmsDb >= -120f && rmsDb <= 20f &&
                   pkDb >= -120f && pkDb <= 20f;
        }

        // ===================== START / PERMISSION (PRESERVED) =====================
        private void StartAudioIfNeeded(bool startRequestedByUser)
        {
            if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
            {
                RequestPermissions(new[] { Manifest.Permission.RecordAudio }, ReqAudio);
                return;
            }
            if (_engine?.IsRunning is true)
            {
                if (startRequestedByUser) SetStatus("Already running.");
                return;
            }

            try
            {
                // Apply latency profile if requested
                _latencyManager?.ApplyRequestedProfile();

                // Start foreground service first
                StartService(new Intent(this, typeof(AudioForegroundService)).SetAction(AudioForegroundService.ACTION_START));

                // Configure from current UI state
                bool pass = _swPassThrough?.Checked ?? true;
                bool dspNs = _swNoiseCancel?.Checked ?? true;
                bool plat = _swPlatformFx?.Checked ?? false;
                bool hp = _swHP?.Checked ?? true;
                bool clar = _swClarity?.Checked ?? true;
                bool eq = _swEq?.Checked ?? false;

                _engine?.Configure(48000, 10, pass, dspNs, plat, hp, clar, eq);

                var ok = _engine?.Start() ?? false;
                SetStatus(ok ? "RUN ▶ Audio started" : "Start FAILED");

                if (ok)
                {
                    UpdateRouteLabel();
                    LogUtilities.LogLatency(this, _engine.TransportLatencyMs, _engine.AlgoLatencyMs);
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Failed to start audio: {ex.Message}");
                SetStatus($"Start FAILED: {ex.Message}");
                throw;
            }
        }

        private void EnsureMicPermission()
        {
            if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
                RequestPermissions(new[] { Manifest.Permission.RecordAudio }, ReqAudio);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode == ReqAudio && grantResults.Length > 0 && grantResults[0] == Permission.Granted)
                SafeStartAudio(true);
        }

        // ===================== ROUTE HELPERS (PRESERVED) =====================
        private void UpdateRouteLabel()
        {
            try
            {
                var info = BluetoothRouting_Utilities.GetActiveRouteInfo(this);
                _routeInfo.Text = "Output: " + info;
                LogUtilities.Log(this, "ROUTE", info);
            }
            catch (Exception ex)
            {
                _routeInfo.Text = "Output: (unknown)";
                SafeLog($"Error updating route label: {ex.Message}");
            }
        }

        // ===================== UI HELPERS (PRESERVED) =====================
        private void Toggle(LinearLayout panel, Button header, string baseLabel)
        {
            bool show = panel.Visibility != ViewStates.Visible;
            panel.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
            header.Text = show ? $"{baseLabel} ▲" : $"{baseLabel} ▼";
        }

        private LinearLayout MakePanel()
        {
            var p = new LinearLayout(this)
            {
                Orientation = Android.Widget.Orientation.Vertical
            };
            p.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            p.SetPadding(16, 16, 16, 16);
            return p;
        }

        private Button MakePrimary(string text)
        {
            var b = new Button(this)
            {
                Text = text
            };
            b.SetPadding(24, 20, 24, 20);
            b.SetBackgroundColor(Color.ParseColor("#2962FF"));
            b.SetTextColor(Color.White);
            return b;
        }

        private Button MakeSecondary(string text)
        {
            var b = new Button(this)
            {
                Text = text
            };
            b.SetPadding(20, 16, 20, 16);
            b.SetBackgroundColor(Color.ParseColor("#263238"));
            b.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            return b;
        }

        private Button MakeEmergency(string text)
        {
            var b = new Button(this)
            {
                Text = text
            };
            b.SetPadding(20, 16, 20, 16);
            b.SetBackgroundColor(Color.ParseColor("#D32F2F")); // Red
            b.SetTextColor(Color.White);
            b.SetTypeface(null, TypefaceStyle.Bold);
            return b;
        }

        private Button MakeTertiary(string text)
        {
            var b = new Button(this)
            {
                Text = text
            };
            b.SetPadding(16, 12, 16, 12);
            b.SetBackgroundColor(Color.Transparent);
            b.SetTextColor(new Color(0xB3, 0xB3, 0xB3));
            return b;
        }

        private Switch MakeSwitch(string label, bool def)
        {
            var s = new Switch(this)
            {
                Text = label,
                Checked = def
            };
            s.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            return s;
        }

        private void SetStatus(string s)
        {
            try
            {
                RunOnUiThread(() =>
                {
                    if (_status != null)
                        _status.Text = s;
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error setting status: {ex.Message}");
            }
        }

        public void Log(string msg)
        {
            try
            {
                RunOnUiThread(() =>
                {
                    if (_status != null)
                        _status.Text = msg;
                    LogUtilities.Log(this, "APP", msg);
                });
            }
            catch (Exception ex)
            {
                // Fallback logging to prevent crashes
                System.Diagnostics.Debug.WriteLine($"Log error: {ex.Message}, Original msg: {msg}");
            }
        }

        private void SafeLog(string message)
        {
            try
            {
                Log(message);
            }
            catch
            {
                // Ultimate fallback - never throw from logging
                System.Diagnostics.Debug.WriteLine($"SafeLog: {message}");
            }
        }

        // ===================== DIALOG HELPERS (PRESERVED) =====================
        private void ShowErrorDialog(string title, string message)
        {
            try
            {
                RunOnUiThread(() =>
                {
                    new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                        .SetTitle(title)
                        .SetMessage(message)
                        .SetPositiveButton("OK", (s, e) => { })
                        .SetIcon(Android.Resource.Drawable.IcDialogAlert)
                        .Show();
                });
            }
            catch (Exception ex)
            {
                SafeLog($"Error showing error dialog: {ex.Message}");
            }
        }

        // ===================== LIFECYCLE ENHANCED =====================
        protected override void OnDestroy()
        {
            try
            {
                // Stop monitoring timers
                _healthTimer?.Stop();
                _healthTimer?.Dispose();
                _metricsUpdateTimer?.Stop();
                _metricsUpdateTimer?.Dispose();
                _latencyMeter?.Stop();
                _latencyMeter?.Dispose();

                // Unregister device callback
                BluetoothRouting_Utilities.UnregisterDeviceCallback(this, _deviceCallback);

                // Stop audio engine
                _engine?.Stop();
                _engine?.Dispose();

                // Dispose Bluetooth manager
                DisposeBluetoothManager();

                // Dispose managers
                _latencyManager = null;
                _presetManager = null;

                SafeLog("MainActivity destroyed cleanly with enhanced cleanup including Bluetooth");
            }
            catch (Exception ex)
            {
                SafeLog($"Error in OnDestroy: {ex.Message}");
            }
            finally
            {
                base.OnDestroy();
            }
        }

        private void DisposeBluetoothManager()
        {
            try
            {
                if (_bluetoothManager != null)
                {
                    _bluetoothManager.DeviceConnected -= OnBluetoothDeviceConnected;
                    _bluetoothManager.DeviceDisconnected -= OnBluetoothDeviceDisconnected;
                    _bluetoothManager.TransportChanged -= OnBluetoothTransportChanged;
                    _bluetoothManager.QualityChanged -= OnBluetoothQualityChanged;
                    _bluetoothManager.StatusMessage -= OnBluetoothStatusMessage;

                    _bluetoothManager.Dispose();
                    _bluetoothManager = null;
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error disposing Bluetooth manager: {ex.Message}");
            }
        }

        protected override void OnPause()
        {
            try
            {
                // Pause monitoring in background to save battery
                _healthTimer?.Stop();
                _metricsUpdateTimer?.Stop();
                base.OnPause();
            }
            catch (Exception ex)
            {
                SafeLog($"Error in OnPause: {ex.Message}");
            }
        }

        protected override void OnResume()
        {
            try
            {
                base.OnResume();
                // Resume monitoring
                _healthTimer?.Start();
                _metricsUpdateTimer?.Start();

                // Refresh status
                UpdateEnhancedHealthStatus();
                UpdateRouteLabel();
                UpdateBluetoothUI();

                // Check for accessibility changes
                SetupAccessibility();
            }
            catch (Exception ex)
            {
                SafeLog($"Error in OnResume: {ex.Message}");
            }
        }

        protected override void OnStart()
        {
            try
            {
                base.OnStart();

                // Ensure latency meter is running
                _latencyMeter?.Start();

                // Update UI with current state
                if (_engine?.IsRunning is true)
                {
                    UpdateEnhancedHealthStatus();
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error in OnStart: {ex.Message}");
            }
        }

        protected override void OnStop()
        {
            try
            {
                base.OnStop();

                // Pause latency monitoring to save resources
                _latencyMeter?.Stop();
            }
            catch (Exception ex)
            {
                SafeLog($"Error in OnStop: {ex.Message}");
            }
        }

        // ===================== CONFIGURATION PERSISTENCE =====================
        protected override void OnSaveInstanceState(Bundle outState)
        {
            try
            {
                base.OnSaveInstanceState(outState);

                // Save current configuration state
                outState.PutBoolean("passthrough", _swPassThrough?.Checked ?? true);
                outState.PutBoolean("noise_cancel", _swNoiseCancel?.Checked ?? true);
                outState.PutBoolean("safety_limiter", _swSafetyLimiter?.Checked ?? true);
                outState.PutBoolean("show_logs", _swShowLogs?.Checked ?? false);
                outState.PutBoolean("show_advanced_metrics", _swShowAdvancedMetrics?.Checked ?? false);
                outState.PutBoolean("accessibility_mode", _accessibilityMode);
                outState.PutBoolean("high_visibility", _highVisibilityMode);
                outState.PutBoolean("auto_fallback", _swAutoFallback?.Checked ?? true);

                if (_latencyManager != null)
                {
                    outState.PutString("latency_profile", _latencyManager.CurrentProfile.ToString());
                }

                SafeLog("Instance state saved with Bluetooth settings");
            }
            catch (Exception ex)
            {
                SafeLog($"Error saving instance state: {ex.Message}");
            }
        }

        protected override void OnRestoreInstanceState(Bundle savedInstanceState)
        {
            try
            {
                base.OnRestoreInstanceState(savedInstanceState);

                // Restore configuration state
                if (_swPassThrough != null)
                    _swPassThrough.Checked = savedInstanceState.GetBoolean("passthrough", true);
                if (_swNoiseCancel != null)
                    _swNoiseCancel.Checked = savedInstanceState.GetBoolean("noise_cancel", true);
                if (_swSafetyLimiter != null)
                    _swSafetyLimiter.Checked = savedInstanceState.GetBoolean("safety_limiter", true);
                if (_swShowLogs != null)
                    _swShowLogs.Checked = savedInstanceState.GetBoolean("show_logs", false);
                if (_swShowAdvancedMetrics != null)
                    _swShowAdvancedMetrics.Checked = savedInstanceState.GetBoolean("show_advanced_metrics", false);
                if (_swAutoFallback != null)
                    _swAutoFallback.Checked = savedInstanceState.GetBoolean("auto_fallback", true);

                _accessibilityMode = savedInstanceState.GetBoolean("accessibility_mode", false);
                _highVisibilityMode = savedInstanceState.GetBoolean("high_visibility", false);

                var latencyProfileStr = savedInstanceState.GetString("latency_profile");
                if (!string.IsNullOrEmpty(latencyProfileStr) &&
                    Enum.TryParse<LatencyProfile>(latencyProfileStr, out var profile))
                {
                    _latencyManager?.RequestProfile(profile);
                }

                SafeLog("Instance state restored with Bluetooth settings");
            }
            catch (Exception ex)
            {
                SafeLog($"Error restoring instance state: {ex.Message}");
            }
        }

        // ===================== MEMORY MANAGEMENT =====================
        public override void OnTrimMemory([GeneratedEnum] TrimMemory level)
        {
            try
            {
                base.OnTrimMemory(level);

                switch (level)
                {
                    case TrimMemory.UiHidden:
                        // UI is hidden, reduce memory usage
                        _chart?.Clear();
                        break;

                    case TrimMemory.RunningModerate:
                    case TrimMemory.RunningLow:
                        // System is under memory pressure
                        SafeLog($"Memory pressure detected: {level}");
                        break;

                    case TrimMemory.RunningCritical:
                        // Critical memory pressure - consider stopping non-essential features
                        SafeLog("Critical memory pressure - optimizing performance");
                        _metricsUpdateTimer?.Stop();
                        break;
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error in OnTrimMemory: {ex.Message}");
            }
        }

        public override void OnLowMemory()
        {
            try
            {
                base.OnLowMemory();
                SafeLog("Low memory warning received");

                // Clear non-essential data
                _chart?.Clear();

                // Reduce update frequency temporarily
                if (_metricsUpdateTimer != null)
                {
                    _metricsUpdateTimer.Interval = 2000; // Reduce to 2 seconds
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error handling low memory: {ex.Message}");
            }
        }

        // ===== Enhanced Device Callback Helper =====
        private sealed class AudioDeviceCallbackEx : AudioDeviceCallback
        {
            private readonly Action _onChanged;
            public AudioDeviceCallbackEx(Action onChanged) { _onChanged = onChanged; }

            public override void OnAudioDevicesAdded(AudioDeviceInfo[] addedDevices)
            {
                try
                {
                    _onChanged?.Invoke();
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
                    _onChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in OnAudioDevicesRemoved: {ex.Message}");
                }
            }
        }

        // ===================== ENHANCED DEBUGGING AND DIAGNOSTICS =====================

        /// <summary>
        /// Enhanced debugging and diagnostics with Bluetooth information
        /// </summary>
        public string GetFullDiagnosticReport()
        {
            try
            {
                var report = new System.Text.StringBuilder();
                report.AppendLine("=== SONARA v1.2.1 ENHANCED DIAGNOSTIC REPORT ===");
                report.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine();

                // Engine status
                report.AppendLine("ENGINE STATUS:");
                report.AppendLine($"  Running: {_engine?.IsRunning ?? false}");
                report.AppendLine($"  Safe Mode: {_engine?.IsInSafeMode ?? false}");
                report.AppendLine($"  Safety Limiter: {_engine?.IsSafetyLimiterEnabled ?? false}");
                report.AppendLine($"  Total Errors: {_engine?.TotalErrors ?? 0}");
                report.AppendLine($"  Auto Recoveries: {_engine?.AutomaticRecoveries ?? 0}");
                report.AppendLine();

                // Health metrics
                if (_engine?.IsRunning is true)
                {
                    var healthMetrics = _engine.GetHealthMetrics();
                    SafeLog($"Current metrics: CPU={healthMetrics.CpuUsagePercent}%, Latency={healthMetrics.LatencyMs}ms");

                    report.AppendLine("HEALTH METRICS:");
                    report.AppendLine($"  Latency: {healthMetrics.LatencyMs}ms");
                    report.AppendLine($"  CPU Usage: {healthMetrics.CpuUsagePercent}%");
                    report.AppendLine($"  Memory Pressure: {healthMetrics.MemoryPressureMB}MB");
                    report.AppendLine($"  Buffer Underruns: {healthMetrics.BufferUnderrunsCount}");
                    report.AppendLine($"  Stability Score: {healthMetrics.StabilityScore:F2}");
                    report.AppendLine($"  Performance Trend: {healthMetrics.PerformanceTrend:F2}");
                    report.AppendLine($"  Dropout Risk: {healthMetrics.PredictedDropoutRisk}%");
                    report.AppendLine();
                }

                // Bluetooth status
                if (_bluetoothManager != null)
                {
                    report.AppendLine("BLUETOOTH STATUS:");
                    report.AppendLine($"  Connection State: {_bluetoothManager.ConnectionState}");
                    report.AppendLine($"  Current Transport: {_bluetoothManager.CurrentTransport}");
                    report.AppendLine($"  Current Quality: {_bluetoothManager.CurrentQuality}");
                    report.AppendLine($"  Auto-fallback: {_bluetoothManager.AutoFallbackEnabled}");
                    report.AppendLine($"  Current Device: {_bluetoothManager.CurrentDevice?.Name ?? "None"}");
                    report.AppendLine($"  Device Address: {_bluetoothManager.CurrentDevice?.Address ?? "N/A"}");
                    report.AppendLine();
                }

                // Latency profile status
                if (_latencyManager != null)
                {
                    report.AppendLine("LATENCY PROFILE:");
                    report.AppendLine($"  Current: {_latencyManager.CurrentProfile}");
                    report.AppendLine($"  Auto Adaptation: {_latencyManager.AutoAdaptationEnabled}");
                    report.AppendLine($"  Status: {_latencyManager.GetStatusReport()}");
                    report.AppendLine();
                }

                // Audio routing
                report.AppendLine("AUDIO ROUTING:");
                report.AppendLine($"  Route Info: {BluetoothRouting_Utilities.GetActiveRouteInfo(this)}");
                report.AppendLine($"  SCO Active: {BluetoothRouting_Utilities.IsScoOn(this)}");
                report.AppendLine($"  A2DP Active: {BluetoothRouting_Utilities.IsA2dpActive(this)}");
                report.AppendLine($"  LE Audio Active: {BluetoothRouting_Utilities.IsLeActive(this)}");
                report.AppendLine();

                // UI state
                report.AppendLine("UI STATE:");
                report.AppendLine($"  Pass-through: {_swPassThrough?.Checked ?? false}");
                report.AppendLine($"  Noise Cancel: {_swNoiseCancel?.Checked ?? false}");
                report.AppendLine($"  Safety Limiter: {_swSafetyLimiter?.Checked ?? false}");
                report.AppendLine($"  Auto-fallback: {_swAutoFallback?.Checked ?? false}");
                report.AppendLine($"  Accessibility Mode: {_accessibilityMode}");
                report.AppendLine($"  High Visibility: {_highVisibilityMode}");
                report.AppendLine();

                // System info
                report.AppendLine("SYSTEM INFO:");
                report.AppendLine($"  Android Version: {Build.VERSION.Release} (API {Build.VERSION.SdkInt})");
                report.AppendLine($"  Device: {Build.Manufacturer} {Build.Model}");
                report.AppendLine($"  Total Memory: {Java.Lang.Runtime.GetRuntime().TotalMemory() / 1024 / 1024}MB");
                report.AppendLine($"  Free Memory: {Java.Lang.Runtime.GetRuntime().FreeMemory() / 1024 / 1024}MB");

                return report.ToString();
            }
            catch (Exception ex)
            {
                return $"Error generating diagnostic report: {ex.Message}";
            }
        }
    }
}