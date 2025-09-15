using Android.App;
using Android.Content;
using Android.OS;

namespace Buds3ProAideAuditiveIA.v2
{
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { ACTION_SHOW, ACTION_EXIT })]
    public class AppControlReceiver : BroadcastReceiver
    {
        public const string ACTION_SHOW = "sonara.app.SHOW";
        public const string ACTION_EXIT = "sonara.app.EXIT";

        public override void OnReceive(Context context, Intent intent)
        {
            var action = intent?.Action ?? string.Empty;

            if (action == ACTION_SHOW)
            {
                // Ramène l'activité au premier plan
                var show = new Intent(context, typeof(MainActivity));
                show.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
                context.StartActivity(show);
            }
            else if (action == ACTION_EXIT)
            {
                // Arrête le service et tue le process (exit propre pour appli utilitaire)
                try
                {
                    context.StartService(new Intent(context, typeof(AudioForegroundService))
                        .SetAction(AudioForegroundService.ACTION_STOP));
                } catch { }

                try { Android.OS.Process.KillProcess(Android.OS.Process.MyPid()); }
                catch { Java.Lang.JavaSystem.Exit(0); }
            }
        }
    }
}