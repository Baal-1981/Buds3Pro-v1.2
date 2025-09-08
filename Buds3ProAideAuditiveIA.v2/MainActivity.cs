using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System;
using System.Threading.Tasks;

namespace Buds3ProAideAuditivelA.v2
{
    [Activity(Label = "Buds3Pro Aide Auditive IA", MainLauncher = true, Exported = true,
              ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class MainActivity : Activity, ILogSink
    {
        private const int ReqAudio = 0xB301;
        private const string TAG = "Buds3ProUI";

        private AudioEngine _engine;

        // UI
        private Button _btnStart;
        private Button _btnStop;
        private Switch _swPass, _swGate, _swDucker, _swHp, _swClarity;
        private Spinner _spRate, _spFrame;
        private TextView _status;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // UI simple programmatique
            LinearLayout root = new LinearLayout(this);
            root.Orientation = Orientation.Vertical;
            root.SetPadding(24, 24, 24, 24);

            _btnStart = new Button(this);
            _btnStart.Text = "START";
            _btnStop = new Button(this);
            _btnStop.Text = "STOP";

            _swPass = new Switch(this);
            _swPass.Text = "Pass-through";
            _swPass.Checked = true;

            _swGate = new Switch(this);
            _swGate.Text = "Gate";

            _swDucker = new Switch(this);
            _swDucker.Text = "Ducker";

            _swHp = new Switch(this);
            _swHp.Text = "High-pass";

            _swClarity = new Switch(this);
            _swClarity.Text = "Clarity";

            _spRate = new Spinner(this);
            _spFrame = new Spinner(this);

            _status = new TextView(this);
            _status.Text = "Prêt.";
            _status.TextSize = 12f;

            ArrayAdapter<string> rateAdapter =
                new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                    new string[] { "Auto", "48000", "44100" });
            rateAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spRate.Adapter = rateAdapter;
            _spRate.SetSelection(0);

            ArrayAdapter<string> frameAdapter =
                new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                    new string[] { "2 ms", "5 ms", "10 ms" });
            frameAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spFrame.Adapter = frameAdapter;
            _spFrame.SetSelection(2); // 10 ms

            // Compose UI
            root.AddView(_btnStart);
            root.AddView(_btnStop);
            root.AddView(_swPass);
            root.AddView(_swGate);
            root.AddView(_swDucker);
            root.AddView(_swHp);
            root.AddView(_swClarity);

            TextView rateLbl = new TextView(this);
            rateLbl.Text = "Sample Rate";
            root.AddView(rateLbl);
            root.AddView(_spRate);

            TextView frameLbl = new TextView(this);
            frameLbl.Text = "Frame Size";
            root.AddView(frameLbl);
            root.AddView(_spFrame);

            TextView logLbl = new TextView(this);
            logLbl.Text = "Logs";
            root.AddView(logLbl);
            root.AddView(_status);

            SetContentView(root);

            _engine = new AudioEngine(this);

            _btnStart.Click += async (s, e) => await StartEngineAsync();
            _btnStop.Click += async (s, e) => await StopEngineAsync();

            UpdateUiState();
        }

        public void Log(string msg)
        {
            RunOnUiThread(() =>
            {
                string t = DateTime.Now.ToString("HH:mm:ss.fff");
                _status.Text = "[" + t + "] " + msg + "\n" + _status.Text;
            });
            try { Android.Util.Log.Debug(TAG, msg); } catch { }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_engine != null)
            {
                var _ = _engine.StopAsync();
            }
        }

        private async Task StartEngineAsync()
        {
            if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
            {
                RequestPermissions(new[] { Manifest.Permission.RecordAudio }, ReqAudio);
                return;
            }

            ApplyConfigFromUi();
            SetControlsEnabled(false);

            bool ok = await _engine.StartAsync();
            if (!ok)
            {
                Toast.MakeText(this, "Erreur démarrage audio", ToastLength.Short).Show();
                SetControlsEnabled(true);
            }
            UpdateUiState();
        }

        private async Task StopEngineAsync()
        {
            if (_engine != null)
            {
                await _engine.StopAsync();
            }
            SetControlsEnabled(true);
            UpdateUiState();
        }

        private void ApplyConfigFromUi()
        {
            int sr = 0;
            switch (_spRate.SelectedItemPosition)
            {
                case 1: sr = 48000; break;
                case 2: sr = 44100; break;
                default: sr = 0; break; // Auto
            }

            int frameMs = 10;
            switch (_spFrame.SelectedItemPosition)
            {
                case 0: frameMs = 2; break;
                case 1: frameMs = 5; break;
                default: frameMs = 10; break;
            }

            _engine.Configure(sr, frameMs,
                pass: _swPass.Checked,
                gate: _swGate.Checked,
                ducker: _swDucker.Checked,
                hp: _swHp.Checked,
                clarity: _swClarity.Checked);
        }

        private void SetControlsEnabled(bool enabled)
        {
            bool running = (_engine != null && _engine.IsRunning);
            _btnStart.Enabled = enabled && !running;
            _btnStop.Enabled = !enabled && running;

            _swPass.Enabled = enabled;
            _swGate.Enabled = enabled;
            _swDucker.Enabled = enabled;
            _swHp.Enabled = enabled;
            _swClarity.Enabled = enabled;
            _spRate.Enabled = enabled;
            _spFrame.Enabled = enabled;
        }

        private void UpdateUiState()
        {
            bool running = (_engine != null && _engine.IsRunning);
            _btnStart.Enabled = !running;
            _btnStop.Enabled = running;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode == ReqAudio && grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                var _ = StartEngineAsync();
            }
            else
            {
                Toast.MakeText(this, "Permission micro requise", ToastLength.Short).Show();
            }
        }
    }
}
