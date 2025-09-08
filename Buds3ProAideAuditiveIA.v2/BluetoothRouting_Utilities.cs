using System;
using System.Threading;
using Android.Content;
using Android.Media;

namespace Buds3ProAideAuditiveIA.v2
{
    public static class BluetoothRouting_Utilities
    {
        public static void EnterCommunicationMode(Context ctx)
        {
            var am = (AudioManager)ctx.GetSystemService(Context.AudioService);
            am.Mode = Mode.InCommunication;
            am.SpeakerphoneOn = false;
        }

        public static bool EnsureSco(Context ctx, int timeoutMs = 1500)
        {
            var am = (AudioManager)ctx.GetSystemService(Context.AudioService);
            try
            {
                if (!am.BluetoothScoOn)
                {
                    am.StartBluetoothSco();
                    am.BluetoothScoOn = true;
                }
            }
            catch { /* ignore */ }

            int waited = 0;
            while (waited < timeoutMs)
            {
                if (am.BluetoothScoOn) return true;
                Thread.Sleep(100); waited += 100;
            }
            return am.BluetoothScoOn;
        }

        public static void LeaveCommunicationMode(Context ctx)
        {
            var am = (AudioManager)ctx.GetSystemService(Context.AudioService);
            am.Mode = Mode.Normal;
            am.SpeakerphoneOn = true;
        }

        public static void StopSco(Context ctx)
        {
            var am = (AudioManager)ctx.GetSystemService(Context.AudioService);
            try { am.BluetoothScoOn = false; am.StopBluetoothSco(); } catch { }
        }
    }
}
