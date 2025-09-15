using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Graphics;
using Android.Content;
using AndroidX.AppCompat.App;

namespace Buds3ProAideAuditiveIA.v2
{
    // === Journal central accessible partout ===
    public static class AppLog
    {
        // Buffer circulaire (max lignes pour éviter l’overgrowth)
        private const int MaxLines = 4000;

        private static readonly LinkedList<string> _lines = new LinkedList<string>();
        private static readonly object _lock = new object();

        // Événement déclenché à chaque nouvelle ligne
        public static event Action<string> LineAppended;

        public static void Append(string line)
        {
            if (line == null) return;
            lock (_lock)
            {
                _lines.AddLast(line);
                while (_lines.Count > MaxLines) _lines.RemoveFirst();
            }
            try { LineAppended?.Invoke(line); } catch { }
        }

        public static string Snapshot()
        {
            lock (_lock)
            {
                var sb = new StringBuilder(_lines.Count * 64);
                foreach (var l in _lines) sb.AppendLine(l);
                return sb.ToString();
            }
        }

        public static void Clear()
        {
            lock (_lock) { _lines.Clear(); }
            try { LineAppended?.Invoke("[logs cleared]"); } catch { }
        }
    }

    [Activity(Label = "Logs", Theme = "@style/Theme.Sonara.Dark", Exported = false)]
    public class LogsActivity : AppCompatActivity
    {
        private TextView _logText;
        private ScrollView _scroll;
        private Switch _autoScroll;
        private Button _btnShare, _btnClear, _btnBack;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetBackgroundColor(Color.ParseColor("#121212"));

            // --- Header ---
            var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            header.SetPadding(16, 16, 16, 16);

            _btnBack = new Button(this) { Text = "← Back" };
            _btnBack.Click += (s, e) => Finish();

            var title = new TextView(this) { Text = "Logs", TextSize = 18f, Typeface = Typeface.DefaultBold };
            title.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            title.SetPadding(16, 10, 0, 0);

            _btnShare = new Button(this) { Text = "Share" };
            _btnClear = new Button(this) { Text = "Clear" };

            header.AddView(_btnBack);
            header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            header.AddView(_btnShare);
            header.AddView(_btnClear);

            root.AddView(header);

            // --- Options ---
            var options = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            options.SetPadding(16, 0, 16, 8);
            _autoScroll = new Switch(this) { Text = "Auto-scroll", Checked = true };
            _autoScroll.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            options.AddView(_autoScroll);
            root.AddView(options);

            // --- Zone logs ---
            _logText = new TextView(this) { Text = "No logs yet…", TextSize = 12f };
            _logText.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            _logText.SetPadding(12, 12, 12, 12);
            _logText.SetBackgroundColor(Color.ParseColor("#1E1E1E"));

            _scroll = new ScrollView(this);
            _scroll.AddView(_logText);

            root.AddView(_scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            SetContentView(root);

            // --- Actions ---
            _btnShare.Click += (s, e) =>
            {
                var text = AppLog.Snapshot();
                var sendIntent = new Intent(Intent.ActionSend);
                sendIntent.SetType("text/plain");
                sendIntent.PutExtra(Intent.ExtraText, text);
                StartActivity(Intent.CreateChooser(sendIntent, "Partager les logs"));
            };

            _btnClear.Click += (s, e) =>
            {
                AppLog.Clear();
                _logText.Text = "";
            };
        }

        protected override void OnStart()
        {
            base.OnStart();
            // Affiche l’existant
            _logText.Text = AppLog.Snapshot();

            // S’abonne aux nouvelles lignes
            AppLog.LineAppended += OnLine;
        }

        protected override void OnStop()
        {
            base.OnStop();
            AppLog.LineAppended -= OnLine;
        }

        private void OnLine(string line)
        {
            RunOnUiThread(() =>
            {
                if (!string.IsNullOrEmpty(_logText.Text))
                    _logText.Text += "\n" + line;
                else
                    _logText.Text = line;

                if (_autoScroll.Checked)
                    _scroll.Post(() => _scroll.FullScroll(FocusSearchDirection.Down));
            });
        }
    }
}
