using Android.Content;
using Android.OS;
using Android.Widget;
using AndroidX.AppCompat.App;
using Buds3ProAideAuditivelA.v2.Services;

namespace Buds3ProAideAuditivelA.v2.Activities
{
    [Android.App.Activity(Label = "@string/title_logs", Theme = "@style/AppTheme", Exported = false)]
    public class LogsActivity : AppCompatActivity
    {
        TextView? _text;
        Button? _btnRefresh, _btnClear, _btnShare;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_logs);

            _text = FindViewById<TextView>(Resource.Id.logs_text);
            _btnRefresh = FindViewById<Button>(Resource.Id.btn_refresh);
            _btnClear = FindViewById<Button>(Resource.Id.btn_clear);
            _btnShare = FindViewById<Button>(Resource.Id.btn_share);

            _btnRefresh!.Click += (_, __) => LoadLogs();
            _btnClear!.Click   += (_, __) => { LogService.Clear(); LoadLogs(); };
            _btnShare!.Click   += (_, __) => ShareLogs();

            LoadLogs();
        }

        void LoadLogs()
        {
            var content = LogService.ReadAll();
            _text!.Text = string.IsNullOrWhiteSpace(content) ? GetString(Resource.String.logs_empty) : content;
        }

        void ShareLogs()
        {
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                this, $"{PackageName}.fileprovider", new Java.IO.File(LogService.PathFile));

            var intent = new Intent(Intent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(Intent.ExtraStream, uri);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            StartActivity(Intent.CreateChooser(intent, GetString(Resource.String.title_logs)));
        }
    }
}
