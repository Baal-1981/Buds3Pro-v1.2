using System;
using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Graphics;
using Android.Webkit;
using Android.Content;
using AndroidX.AppCompat.App;

namespace Buds3ProAideAuditiveIA.v2
{
    [Activity(Label = "@string/title_help", Theme = "@style/Theme.Sonara.Dark", Exported = false)]
    public class HelpActivity : AppCompatActivity
    {
        private WebView _web;
        private ProgressBar _progress;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetBackgroundColor(Color.ParseColor("#121212"));

            // ==== Header ====
            var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            header.SetPadding(16, 16, 16, 16);

            var btnBack = new Button(this) { Text = "← Back" };
            btnBack.Click += (s, e) => Finish();

            var title = new TextView(this)
            {
                Text = GetString(Resource.String.title_help),
                TextSize = 18f,
                Typeface = Typeface.DefaultBold
            };
            title.SetTextColor(new Color(0xE6, 0xE6, 0xE6));
            title.SetPadding(16, 10, 0, 0);

            var btnOpen = new Button(this) { Text = "Open in browser" };
            var btnShare = new Button(this) { Text = "Share" };

            header.AddView(btnBack);
            header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            header.AddView(btnOpen);
            header.AddView(btnShare);
            root.AddView(header);

            // ==== Progress ====
            _progress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal)
            {
                Indeterminate = false,
                Max = 100
            };
            root.AddView(_progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 6));

            // ==== WebView ====
            _web = new WebView(this);
            _web.SetBackgroundColor(Color.ParseColor("#121212"));
            var settings = _web.Settings;
            settings.JavaScriptEnabled = true;
            settings.DomStorageEnabled = true;
            settings.LoadWithOverviewMode = true;
            settings.UseWideViewPort = true;

            _web.SetWebChromeClient(new ProgressChromeClient(p =>
            {
                _progress.Progress = p;
                _progress.Visibility = p >= 100 ? ViewStates.Gone : ViewStates.Visible;
            }));

            _web.SetWebViewClient(new InternalWebViewClient());

            // Charge l’URL d’aide depuis les ressources, fallback si indisponible
            string helpUrl = null;
            try { helpUrl = GetString(Resource.String.help_url); } catch { /* resource absente */ }

            if (!string.IsNullOrWhiteSpace(helpUrl))
            {
                _web.LoadUrl(helpUrl);
            }
            else
            {
                // Fallback local minimal
                string html = @"<html><body style='background:#121212;color:#EEE;font-family:sans-serif;padding:16px'>
<h2>Help</h2>
<p>Aucun lien d'aide n'est configuré (resource <code>help_url</code> manquante).</p>
<p>Ajoutez <code>&lt;string name=""help_url""&gt;https://ton.site/aide&lt;/string&gt;</code> dans <code>Resources/values/strings.xml</code>.</p>
</body></html>";
                _web.LoadDataWithBaseURL(null, html, "text/html", "utf-8", null);
            }

            root.AddView(_web, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            SetContentView(root);

            // Actions header
            btnOpen.Click += (s, e) =>
            {
                try
                {
                    var url = _web?.Url ?? helpUrl;
                    if (!string.IsNullOrWhiteSpace(url))
                        StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url)));
                }
                catch { Toast.MakeText(this, "Cannot open link.", ToastLength.Short).Show(); }
            };
            btnShare.Click += (s, e) =>
            {
                var url = _web?.Url ?? helpUrl ?? "";
                var send = new Intent(Intent.ActionSend);
                send.SetType("text/plain");
                send.PutExtra(Intent.ExtraText, url);
                StartActivity(Intent.CreateChooser(send, "Share help link"));
            };
        }

        // Support du bouton "retour" matériel (navigue d’abord dans le WebView)
        public override bool OnKeyDown(Keycode keyCode, KeyEvent e)
        {
            if (keyCode == Keycode.Back && _web != null && _web.CanGoBack())
            {
                _web.GoBack();
                return true;
            }
            return base.OnKeyDown(keyCode, e);
        }

        protected override void OnDestroy()
        {
            try
            {
                _web?.StopLoading();
                _web?.Destroy();
            }
            catch { }
            base.OnDestroy();
        }

        private sealed class InternalWebViewClient : WebViewClient
        {
            public override bool ShouldOverrideUrlLoading(WebView view, IWebResourceRequest request)
            {
                // Garde la navigation dans la WebView
                return false;
            }
        }

        private sealed class ProgressChromeClient : WebChromeClient
        {
            private readonly Action<int> _onProgress;
            public ProgressChromeClient(Action<int> onProgress) { _onProgress = onProgress; }
            public override void OnProgressChanged(WebView view, int newProgress) => _onProgress?.Invoke(newProgress);
        }
    }
}
