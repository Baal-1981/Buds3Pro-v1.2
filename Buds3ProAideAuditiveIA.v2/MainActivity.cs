using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;                 // AppCompatActivity + AlertDialog
using Buds3ProAideAuditivelA.v2.Helpers;      // LocaleManager
using System;

namespace Buds3ProAideAuditivelA.v2
{
    [Activity(Label = "Buds3Pro Aide Auditive IA", MainLauncher = true, Exported = true,
              ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class MainActivity : AppCompatActivity, ILogSink
    {
        private const int ReqAudio = 0xB301;

        private AudioEngine _engine;
        private LatencyMeter _latMeter;

        // --- UI principal ---
        private Button _btnStart, _btnStop, _btnCalib;
        private Switch _swPass, _swNCEnable, _swShowLogs;
        private SeekBar _sbGain;
        private TextView _lblGain, _status, _latency;


        private RealtimeChartView _chart;
        private LatencyStats _latStats;
        // --- Noise Cancelling ---
        private LinearLayout _ncPanel;
        private Button _btnNcDrop;
        private Switch _swExpander, _swHp, _swClarity, _swPlatformFx, _swDspNs, _swVad;
        private SeekBar _sbAmbCut, _sbAmbRelease;
        private TextView _lblAmb, _lblAmbRelease;
        private Spinner _spRate, _spFrame, _spHpCut;

        // --- Égalisation existante ---
        private Switch _swEqEnable;
        private Button _btnEqDrop;
        private LinearLayout _eqPanel;
        private TextView _lblBass, _lblTreble;
        private SeekBar _sbBass, _sbTreble;
        private Spinner _spBassFreq, _spTrebleFreq;

        // --- Qualité voix ---
        private Button _btnQualDrop;
        private LinearLayout _qualPanel;

        private Switch _swPresence;
        private TextView _lblPresence, _lblPresenceHz;
        private SeekBar _sbPresence;
        private Spinner _spPresenceHz;

        private Switch _swHum;
        private Spinner _spHumBase;

        private Switch _swDeEsser;
        private TextView _lblDeEsser;
        private SeekBar _sbDeEsser;

        // Headroom meter
        private TextView _lblHeadroom;

        // Logs
        private ScrollView _logScroll;

        protected override void AttachBaseContext(Context newBase)
        {
            base.AttachBaseContext(LocaleManager.Wrap(newBase));
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Title = GetString(Resource.String.title_main);

            // Root scrollable container
            var scroll = new ScrollView(this);
            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetPadding(24, 24, 24, 24);
            scroll.AddView(root);

            // --- Principal ---
            _btnStart = new Button(this) { Text = "START" };
            _btnStop = new Button(this) { Text = "STOP" };

            _swPass = new Switch(this) { Text = "Pass-through (ON = audio actif)", Checked = true };

            _lblGain = new TextView(this) { Text = "Gain: +19 dB" };
            _sbGain = new SeekBar(this) { Max = 36, Progress = 19 };

            // NC
            _swNCEnable = new Switch(this) { Text = "Activer Noise Cancelling", Checked = true };
            _btnNcDrop = new Button(this) { Text = "Noise Cancelling ▼" };
            _btnCalib = new Button(this) { Text = "Calibrer le bruit (0.5 s)" };

            // Latence + Logs
            _latency = new TextView(this) { Text = "Latence: -- ms", TextSize = 12f };
            _swShowLogs = new Switch(this) { Text = "Afficher les logs", Checked = true };
            _status = new TextView(this)
            {
                Text = "Prêt.",
                TextSize = 12f
            };
            _status.SetPadding(12, 12, 12, 12);
            _status.SetBackgroundColor(Android.Graphics.Color.Argb(24, 255, 255, 255));

            _logScroll = new ScrollView(this);
            _logScroll.AddView(_status);
            _logScroll.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 600);

            // --- Panneau NC ---
            _ncPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Visible };
            _swExpander = new Switch(this) { Text = "Silence intelligent (expander)", Checked = true };
            _lblAmb = new TextView(this) { Text = "Silence atténuation: -12 dB" };
            _sbAmbCut = new SeekBar(this) { Max = 24, Progress = 12 };

            _lblAmbRelease = new TextView(this) { Text = "Relâche expander: 150 ms" };
            _sbAmbRelease = new SeekBar(this) { Max = 300, Progress = 150 };

            _swHp = new Switch(this) { Text = "High-pass", Checked = false };
            _spHpCut = new Spinner(this);

            _swClarity = new Switch(this) { Text = "Clarity", Checked = true };
            _swPlatformFx = new Switch(this) { Text = "Android NS/AGC/AEC", Checked = false };
            _swDspNs = new Switch(this) { Text = "Spectral NS (FFT)", Checked = false };
            _swVad = new Switch(this) { Text = "VAD (voix seulement)", Checked = true };

            _spRate = new Spinner(this);
            _spFrame = new Spinner(this);

            // --- EQ existante ---
            _swEqEnable = new Switch(this) { Text = "Activer Égalisation", Checked = false };
            _btnEqDrop = new Button(this) { Text = "Égalisation ▼" };
            _eqPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
            _lblBass = new TextView(this) { Text = "Basses: 0 dB" };
            _sbBass = new SeekBar(this) { Max = 24, Progress = 12 };
            _spBassFreq = new Spinner(this);
            _lblTreble = new TextView(this) { Text = "Aigus: 0 dB" };
            _sbTreble = new SeekBar(this) { Max = 24, Progress = 12 };
            _spTrebleFreq = new Spinner(this);

            // --- Qualité (présence / hum / de-esser) ---
            _btnQualDrop = new Button(this) { Text = "Qualité voix ▼" };
            _qualPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };

            _swPresence = new Switch(this) { Text = "Présence voix (1–3 kHz)", Checked = true };
            _lblPresence = new TextView(this) { Text = "Présence: 0 dB" };
            _sbPresence = new SeekBar(this) { Max = 16, Progress = 8 };
            _lblPresenceHz = new TextView(this) { Text = "Fréquence présence: 2.0 kHz" };
            _spPresenceHz = new Spinner(this);

            _swHum = new Switch(this) { Text = "Hum remover (secteur)", Checked = false };
            _spHumBase = new Spinner(this);

            _swDeEsser = new Switch(this) { Text = "De-esser (sifflantes)", Checked = false };
            _lblDeEsser = new TextView(this) { Text = "Force de-esser: 0 dB (max)" };
            _sbDeEsser = new SeekBar(this) { Max = 8, Progress = 0 };

            _lblHeadroom = new TextView(this) { Text = "Headroom: ok", TextSize = 12f };

            // Spinners
            var rateAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                new string[] { "Auto", "48000", "44100" });
            rateAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spRate.Adapter = rateAdapter; _spRate.SetSelection(0);

            var frameAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                new string[] { "2 ms", "5 ms", "10 ms" });
            frameAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spFrame.Adapter = frameAdapter; _spFrame.SetSelection(2);

            var hpAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                new string[] { "80 Hz", "100 Hz", "120 Hz", "160 Hz", "200 Hz" });
            hpAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spHpCut.Adapter = hpAdapter; _spHpCut.SetSelection(2);

            var bassFreqAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                new string[] { "60 Hz", "80 Hz", "100 Hz", "120 Hz", "160 Hz", "200 Hz", "250 Hz", "300 Hz" });
            bassFreqAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spBassFreq.Adapter = bassFreqAdapter; _spBassFreq.SetSelection(3);

            var trebleFreqAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                new string[] { "3 kHz", "4 kHz", "6 kHz", "8 kHz" });
            trebleFreqAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spTrebleFreq.Adapter = trebleFreqAdapter; _spTrebleFreq.SetSelection(1);

            var presenceHzAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                new string[] { "1.0 kHz", "1.5 kHz", "2.0 kHz", "2.5 kHz", "3.0 kHz" });
            presenceHzAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spPresenceHz.Adapter = presenceHzAdapter; _spPresenceHz.SetSelection(2);

            var humBaseAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                new string[] { "50 Hz", "60 Hz" });
            humBaseAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _spHumBase.Adapter = humBaseAdapter; _spHumBase.SetSelection(1);

            // Panneau NC
            _ncPanel.AddView(_swExpander);
            _ncPanel.AddView(_lblAmb);
            _ncPanel.AddView(_sbAmbCut);
            _ncPanel.AddView(_lblAmbRelease);
            _ncPanel.AddView(_sbAmbRelease);
            _ncPanel.AddView(_swHp);
            _ncPanel.AddView(_spHpCut);
            _ncPanel.AddView(_swClarity);
            _ncPanel.AddView(_swPlatformFx);
            _ncPanel.AddView(_swDspNs);
            _ncPanel.AddView(_swVad);

            var lblRate = new TextView(this) { Text = "Sample Rate (au redémarrage)" };
            var lblFrame = new TextView(this) { Text = "Frame Size (au redémarrage)" };
            _ncPanel.AddView(lblRate); _ncPanel.AddView(_spRate);
            _ncPanel.AddView(lblFrame); _ncPanel.AddView(_spFrame);

            // Panneau EQ
            _eqPanel.AddView(_lblBass); _eqPanel.AddView(_sbBass); _eqPanel.AddView(_spBassFreq);
            _eqPanel.AddView(_lblTreble); _eqPanel.AddView(_sbTreble); _eqPanel.AddView(_spTrebleFreq);

            // Panneau Qualité
            _qualPanel.AddView(_swPresence);
            _qualPanel.AddView(_lblPresence);
            _qualPanel.AddView(_sbPresence);
            _qualPanel.AddView(_lblPresenceHz);
            _qualPanel.AddView(_spPresenceHz);
            _qualPanel.AddView(_swHum);
            _qualPanel.AddView(_spHumBase);
            _qualPanel.AddView(_swDeEsser);
            _qualPanel.AddView(_lblDeEsser);
            _qualPanel.AddView(_sbDeEsser);

            // Layout racine
            root.AddView(_btnStart);
            root.AddView(_btnStop);

            root.AddView(_swPass);
            root.AddView(_lblGain); root.AddView(_sbGain);

            root.AddView(_swNCEnable);
            root.AddView(_btnNcDrop);
            root.AddView(_ncPanel);
            root.AddView(_btnCalib);

            root.AddView(_swEqEnable);
            root.AddView(_btnEqDrop);
            root.AddView(_eqPanel);

            root.AddView(_btnQualDrop);
            root.AddView(_qualPanel);


            // Graphique temps réel
            _chart = new RealtimeChartView(this);
            _chart.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 320);
            root.AddView(_chart);
            root.AddView(_lblHeadroom);
            root.AddView(_latency);

            root.AddView(_swShowLogs);
            root.AddView(_logScroll);

            SetContentView(scroll);

            _engine = new AudioEngine(this);
            _engine.SetMetersCallback((pkDb, rmsDb, grDb) =>
            {
                RunOnUiThread(() =>
                {
                    var head = pkDb <= 0f ? -pkDb : 0f;
                    _lblHeadroom.Text = $"Headroom: {head:0.0} dB | Peak {pkDb:0.0} dBFS | RMS {rmsDb:0.0} dBFS | GR {grDb:0.0} dB";
                    _chart?.AddPoint(pkDb, rmsDb, grDb, head);
                });
            });

            _latMeter = new LatencyMeter(_engine, ms => RunOnUiThread(() => _latency.Text = $"Latence: {ms} ms"), 400);

            // Boutons
            _btnStart.Click += (s, e) => StartEngine();
            _btnStop.Click += (s, e) => StopEngine();

            _btnNcDrop.Click += (s, e) =>
            {
                bool show = _ncPanel.Visibility != ViewStates.Visible;
                _ncPanel.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
                _btnNcDrop.Text = show ? "Noise Cancelling ▲" : "Noise Cancelling ▼";
            };

            _btnEqDrop.Click += (s, e) =>
            {
                bool show = _eqPanel.Visibility != ViewStates.Visible;
                _eqPanel.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
                _btnEqDrop.Text = show ? "Égalisation ▲" : "Égalisation ▼";
            };

            _btnQualDrop.Click += (s, e) =>
            {
                bool show = _qualPanel.Visibility != ViewStates.Visible;
                _qualPanel.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
                _btnQualDrop.Text = show ? "Qualité voix ▲" : "Qualité voix ▼";
            };

            _btnCalib.Click += (s, e) =>
            {
                if (_swNCEnable.Checked)
                {
                    _engine.CalibrateNoiseNow(500);
                    Toast.MakeText(this, "Restez silencieux 0.5 s", ToastLength.Short).Show();
                }
            };

            // Toggles runtime
            _swPass.CheckedChange += (s, e) => _engine.SetFlags(pass: e.IsChecked);
            _swNCEnable.CheckedChange += (s, e) => SetNcEnabled(e.IsChecked);

            _swExpander.CheckedChange += (s, e) => _engine.SetFlags(ambientExpander: e.IsChecked, gate: e.IsChecked);
            _sbAmbCut.ProgressChanged += (s, e) => { _engine.SetAmbientReductionDb(e.Progress); _lblAmb.Text = $"Silence atténuation: -{e.Progress} dB"; };
            _sbAmbRelease.ProgressChanged += (s, e) =>
            {
                int ms = Math.Max(50, e.Progress);
                _engine.SetAmbientReleaseMs(ms);
                _lblAmbRelease.Text = $"Relâche expander: {ms} ms";
            };

            _swHp.CheckedChange += (s, e) => _engine.SetFlags(hp: e.IsChecked);
            _spHpCut.ItemSelected += (s, e) =>
            {
                int hz = e.Position switch { 0 => 80, 1 => 100, 2 => 120, 3 => 160, _ => 200 };
                _engine.SetHighPassCutoffHz(hz);
            };

            _swClarity.CheckedChange += (s, e) => _engine.SetFlags(clarity: e.IsChecked);
            _swPlatformFx.CheckedChange += (s, e) => _engine.SetFlags(platformFx: e.IsChecked);
            _swDspNs.CheckedChange += (s, e) => _engine.SetFlags(dspNs: e.IsChecked);
            _swVad.CheckedChange += (s, e) => _engine.SetFlags(vad: e.IsChecked);

            _sbGain.ProgressChanged += (s, e) =>
            {
                _engine.SetGainDb(e.Progress);
                _lblGain.Text = $"Gain: +{e.Progress} dB";
            };

            // EQ
            _swEqEnable.CheckedChange += (s, e) => _engine.SetEqEnabled(e.IsChecked);
            _sbBass.ProgressChanged += (s, e) =>
            {
                int db = e.Progress - 12;
                _engine.SetBassDb(db);
                _lblBass.Text = $"Basses: {db:+#;-#;0} dB";
            };
            _sbTreble.ProgressChanged += (s, e) =>
            {
                int db = e.Progress - 12;
                _engine.SetTrebleDb(db);
                _lblTreble.Text = $"Aigus: {db:+#;-#;0} dB";
            };
            _spBassFreq.ItemSelected += (s, e) =>
            {
                int hz = e.Position switch { 0 => 60, 1 => 80, 2 => 100, 3 => 120, 4 => 160, 5 => 200, 6 => 250, _ => 300 };
                _engine.SetBassFreqHz(hz);
            };
            _spTrebleFreq.ItemSelected += (s, e) =>
            {
                int hz = e.Position switch { 0 => 3000, 1 => 4000, 2 => 6000, _ => 8000 };
                _engine.SetTrebleFreqHz(hz);
            };

            // Qualité
            _swPresence.CheckedChange += (s, e) => _engine.SetPresenceEnabled(e.IsChecked);
            _sbPresence.ProgressChanged += (s, e) =>
            {
                int db = e.Progress - 8;
                _engine.SetPresenceDb(db);
                _lblPresence.Text = $"Présence: {db:+#;-#;0} dB";
            };
            _spPresenceHz.ItemSelected += (s, e) =>
            {
                int hz = e.Position switch { 0 => 1000, 1 => 1500, 2 => 2000, 3 => 2500, _ => 3000 };
                _engine.SetPresenceHz(hz);
                _lblPresenceHz.Text = $"Fréquence présence: {(hz >= 1000 ? (hz / 1000.0).ToString("0.0") + " kHz" : hz + " Hz")}";
            };

            _swHum.CheckedChange += (s, e) => _engine.SetHumEnabled(e.IsChecked);
            _spHumBase.ItemSelected += (s, e) =>
            {
                int hz = e.Position == 0 ? 50 : 60;
                _engine.SetHumBaseHz(hz);
            };

            _swDeEsser.CheckedChange += (s, e) => _engine.SetDeEsserEnabled(e.IsChecked);
            _sbDeEsser.ProgressChanged += (s, e) =>
            {
                _engine.SetDeEsserMaxDb(e.Progress);
                _lblDeEsser.Text = $"Force de-esser: {e.Progress} dB (max)";
            };

            _swShowLogs.CheckedChange += (s, e) =>
            {
                _logScroll.Visibility = e.IsChecked ? ViewStates.Visible : ViewStates.Gone;
            };

            UpdateUiState();
        }

        private void SetNcEnabled(bool enabled)
        {
            // Ne pas masquer EQ/Qualité : on ne touche qu'au panneau NC.
            _ncPanel.Visibility = enabled ? ViewStates.Visible : ViewStates.Gone;
            _btnNcDrop.Enabled = enabled;
            _btnCalib.Visibility = enabled ? ViewStates.Visible : ViewStates.Gone;

            if (!enabled)
            {
                _engine.SetFlags(ambientExpander: false, gate: false, hp: false,
                                 clarity: false, platformFx: false, dspNs: false, vad: false);
                _swExpander.Checked = false;
                _swHp.Checked = false;
                _swClarity.Checked = false;
                _swPlatformFx.Checked = false;
                _swDspNs.Checked = false;
                _swVad.Checked = false;
            }
            else
            {
                _engine.SetFlags(ambientExpander: _swExpander.Checked, gate: _swExpander.Checked,
                                 hp: _swHp.Checked, clarity: _swClarity.Checked,
                                 platformFx: _swPlatformFx.Checked, dspNs: _swDspNs.Checked, vad: _swVad.Checked);
            }
        }

        public void Log(string msg)
        {
            RunOnUiThread(() =>
            {
                string t = DateTime.Now.ToString("HH:mm:ss.fff");
                _status.Text = $"[{t}] {msg}\n{_status.Text}";
                _logScroll.FullScroll(FocusSearchDirection.Up);
            });
            try { Android.Util.Log.Debug("Buds3ProUI", msg); } catch { }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            StopEngine();
            _latMeter?.Dispose();
        }

        private void StartEngine()
        {
            if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
            {
                RequestPermissions(new[] { Manifest.Permission.RecordAudio }, ReqAudio);
                return;
            }

            ApplyConfigFromUi();

            if (_engine.Start())
            {
                if (_swNCEnable.Checked) Toast.MakeText(this, "Calibration auto (0.5 s)", ToastLength.Short).Show();
                SetControlsEnabled(running: true);
                _latMeter.Start();
            }
            else
            {
                Toast.MakeText(this, "Erreur de démarrage audio", ToastLength.Short).Show();
                SetControlsEnabled(running: false);
            }
            UpdateUiState();
        }

        private void StopEngine()
        {
            _latMeter.Stop();
            _engine.Stop();
            SetControlsEnabled(running: false);
            UpdateUiState();
        }

        private void ApplyConfigFromUi()
        {
            int sr = (_spRate.SelectedItemPosition == 1) ? 48000 :
                     (_spRate.SelectedItemPosition == 2) ? 44100 : 0;

            int frameMs = (_spFrame.SelectedItemPosition == 0) ? 2 :
                          (_spFrame.SelectedItemPosition == 1) ? 5 : 10;

            _engine.Configure(sr, frameMs,
                pass: _swPass.Checked,
                gateIgnored: _swExpander.Checked,
                duckerIgnored: false,
                hp: _swHp.Checked,
                clarity: _swClarity.Checked);

            // NC global
            SetNcEnabled(_swNCEnable.Checked);

            // ÉQ global
            _engine.SetEqEnabled(_swEqEnable.Checked);
            _engine.SetBassDb(_sbBass.Progress - 12);
            _engine.SetTrebleDb(_sbTreble.Progress - 12);
            int bassHz = _spBassFreq.SelectedItemPosition switch { 0 => 60, 1 => 80, 2 => 100, 3 => 120, 4 => 160, 5 => 200, 6 => 250, _ => 300 };
            int trebHz = _spTrebleFreq.SelectedItemPosition switch { 0 => 3000, 1 => 4000, 2 => 6000, _ => 8000 };
            _engine.SetBassFreqHz(bassHz);
            _engine.SetTrebleFreqHz(trebHz);

            // Qualité
            _engine.SetPresenceEnabled(_swPresence.Checked);
            _engine.SetPresenceDb(_sbPresence.Progress - 8);
            int presHz = _spPresenceHz.SelectedItemPosition switch { 0 => 1000, 1 => 1500, 2 => 2000, 3 => 2500, _ => 3000 };
            _engine.SetPresenceHz(presHz);

            _engine.SetHumEnabled(_swHum.Checked);
            _engine.SetHumBaseHz(_spHumBase.SelectedItemPosition == 0 ? 50 : 60);

            _engine.SetDeEsserEnabled(_swDeEsser.Checked);
            _engine.SetDeEsserMaxDb(_sbDeEsser.Progress);

            _engine.SetGainDb(_sbGain.Progress);
            _engine.SetAmbientReductionDb(_sbAmbCut.Progress);
            _engine.SetAmbientReleaseMs(Math.Max(50, _sbAmbRelease.Progress));
            int hpHz = _spHpCut.SelectedItemPosition switch { 0 => 80, 1 => 100, 2 => 120, 3 => 160, _ => 200 };
            _engine.SetHighPassCutoffHz(hpHz);
        }

        private void SetControlsEnabled(bool running)
        {
            _btnStart.Enabled = !running;
            _btnStop.Enabled = running;
            _btnCalib.Enabled = running && _swNCEnable.Checked;

            _swPass.Enabled = true;
            _sbGain.Enabled = true;

            _swNCEnable.Enabled = true;
            _btnNcDrop.Enabled = _swNCEnable.Checked;

            _swEqEnable.Enabled = true;
            _btnEqDrop.Enabled = true;

            _btnQualDrop.Enabled = true;

            _spRate.Enabled = !running;
            _spFrame.Enabled = !running;
        }

        private void UpdateUiState() => SetControlsEnabled(_engine?.IsRunning == true);

        public override bool OnCreateOptionsMenu(Android.Views.IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.main_menu, menu);
            return base.OnCreateOptionsMenu(menu);
        }

        public override bool OnOptionsItemSelected(Android.Views.IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.action_language:
                    ShowLanguageDialog();
                    return true;

                case Resource.Id.action_logs:
                    Toast.MakeText(this, "Logs: page à venir", ToastLength.Short).Show();
                    return true;

                case Resource.Id.action_help:
                    Toast.MakeText(this, "Aide: page à venir", ToastLength.Short).Show();
                    return true;
            }
            return base.OnOptionsItemSelected(item);
        }

        private void ShowLanguageDialog()
        {
            var langs = new[] { GetString(Resource.String.lang_en), GetString(Resource.String.lang_fr) };

            new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                .SetTitle(Resource.String.lang_dialog_title)
                .SetItems(langs, (s, e) =>
                {
                    var code = (e.Which == 0) ? "en" : "fr";
                    LocaleManager.SetLocale(this, code);
                    Recreate(); // recharge l’Activity pour appliquer la nouvelle langue
                })
                .SetNegativeButton(Android.Resource.String.Cancel, (s, e) => { })
                .Show();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode == ReqAudio && grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                StartEngine();
            }
            else
            {
                Toast.MakeText(this, "Permission micro requise", ToastLength.Short).Show();
            }
        }
    }
}
