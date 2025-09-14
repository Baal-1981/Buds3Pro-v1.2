using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace Buds3ProAideAuditiveIA.v2
{
    // IMPORTANT: Exported = true is required on Android 12+ if this Activity has an intent filter (MainLauncher).
    [Activity(
        Label = "Buds3Pro Hearing Assist",
        MainLauncher = true,
        Exported = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
    )]
    public class MainActivity : Activity
    {
        private const int ReqAudio = 0xB301;

        // Buttons (EN)
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnCalibrate;
        private Button _btnNcToggle;
        private Button _btnEqToggle;
        private Button _btnQualityToggle;

        // Switches / Controls (EN labels kept minimal)
        private Switch _swPassThrough;
        private Switch _swNoiseCancel;
        private Switch _swShowLogs;

        // Status / logs
        private TextView _status;
        private TextView _latency;
        private ScrollView _logScroll;

        // TODO: Re-wire your audio engine objects here if/when needed
        // private AudioEngine _engine;
        // private LatencyMeter _latMeter;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // ----- Root UI (programmatic layout to avoid XML ID mismatches) -----
            var scroll = new ScrollView(this);
            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetPadding(24, 24, 24, 24);
            scroll.AddView(root);

            // Row: Start / Stop
            var rowMain = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            rowMain.SetPadding(0, 0, 0, 16);

            _btnStart = new Button(this) { Text = "START" };
            _btnStop  = new Button(this) { Text = "STOP" };
            rowMain.AddView(_btnStart, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            rowMain.AddView(_btnStop,  new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            // Switch: Pass-through
            _swPassThrough = new Switch(this) { Text = "Pass-through (ON = audio active)", Checked = true };

            // Noise Cancelling
            _swNoiseCancel = new Switch(this) { Text = "Enable Noise Cancelling", Checked = true };
            _btnCalibrate  = new Button(this) { Text = "Calibrate noise (0.5s)" };
            _btnNcToggle   = new Button(this) { Text = "Noise Cancelling ▼" };

            // Equalizer / Quality groups (just toggles; expand behavior is up to you)
            _btnEqToggle     = new Button(this) { Text = "Equalizer ▼" };
            _btnQualityToggle= new Button(this) { Text = "Speech Quality ▼" };

            // Latency + logs
            _latency = new TextView(this) { Text = "Latency: -- ms", TextSize = 12f };
            _swShowLogs = new Switch(this) { Text = "Show logs", Checked = true };
            _status = new TextView(this)
            {
                Text = "Ready.",
                TextSize = 12f
            };
            _status.SetPadding(12, 12, 12, 12);
            _status.SetBackgroundColor(Android.Graphics.Color.Argb(24, 0, 0, 0));
            _logScroll = new ScrollView(this);
            _logScroll.AddView(_status);
            _logScroll.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 600);

            // Assemble UI
            root.AddView(rowMain);
            root.AddView(_swPassThrough);
            root.AddView(_swNoiseCancel);
            root.AddView(_btnCalibrate);
            root.AddView(_btnNcToggle);
            root.AddView(_btnEqToggle);
            root.AddView(_btnQualityToggle);
            root.AddView(_latency);
            root.AddView(_swShowLogs);
            root.AddView(_logScroll);

            SetContentView(scroll);

            // ----- Events -----
            _btnStart.Click += (s, e) =>
            {
                // TODO: if you use your AudioEngine, start it here.
                // _engine ??= new AudioEngine(/* ... */);
                // _engine.Start();
                SetStatus("Started.");
            };

            _btnStop.Click += (s, e) =>
            {
                // TODO: stop your engine here.
                // _engine?.Stop();
                SetStatus("Stopped.");
            };

            _btnCalibrate.Click += (s, e) =>
            {
                // TODO: run your calibration method if available.
                // _engine?.CalibrateNoise(0.5);
                SetStatus("Calibrating noise (0.5s)...");
            };

            _btnNcToggle.Click += (s, e) =>
            {
                // Toggle visibility of a NC panel if you have one
                Toast.MakeText(this, "Noise Cancelling panel toggled", ToastLength.Short).Show();
            };

            _btnEqToggle.Click += (s, e) =>
            {
                Toast.MakeText(this, "Equalizer panel toggled", ToastLength.Short).Show();
            };

            _btnQualityToggle.Click += (s, e) =>
            {
                Toast.MakeText(this, "Speech Quality panel toggled", ToastLength.Short).Show();
            };

            _swPassThrough.CheckedChange += (s, e) =>
            {
                // TODO: _engine?.SetPassThrough(e.IsChecked);
                SetStatus($"Pass-through: {(e.IsChecked ? "ON" : "OFF")}");
            };

            _swNoiseCancel.CheckedChange += (s, e) =>
            {
                // TODO: _engine?.EnableNoiseCancelling(e.IsChecked);
                SetStatus($"Noise Cancelling: {(e.IsChecked ? "ON" : "OFF")}");
            };

            _swShowLogs.CheckedChange += (s, e) =>
            {
                _logScroll.Visibility = e.IsChecked ? ViewStates.Visible : ViewStates.Gone;
            };

            // Runtime permission for microphone (Android 6+)
            EnsureMicPermission();
        }

        private void EnsureMicPermission()
        {
            if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
            {
                RequestPermissions(new[] { Manifest.Permission.RecordAudio }, ReqAudio);
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode == ReqAudio)
            {
                if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
                {
                    SetStatus("Microphone permission granted.");
                }
                else
                {
                    SetStatus("Microphone permission denied.");
                    Toast.MakeText(this, "Microphone permission is required for audio processing.", ToastLength.Long).Show();
                }
            }
        }

        private void SetStatus(string message)
        {
            _status.Text = message;
            // Auto-scroll to bottom
            _logScroll.Post(() => _logScroll.FullScroll(FocusSearchDirection.Down));
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            // _engine?.Dispose(); // if you wire your engine later
        }
    }
}
