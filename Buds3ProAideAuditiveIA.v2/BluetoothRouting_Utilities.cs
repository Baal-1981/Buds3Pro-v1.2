using Android.Content;
using Android.Media;
using Android.OS;
using Android.Runtime;
using System;
using System.Linq;

namespace Buds3ProAideAuditiveIA.v2
{
    public static class BluetoothRouting_Utilities
    {
        private static AudioManager _amCached;
        private static AudioDeviceCallback _callbackCached;

        private static AudioManager AM(Context ctx)
        {
            _amCached ??= (AudioManager)ctx.GetSystemService(Context.AudioService);
            return _amCached;
        }

        // ====== Mode communication / SCO ======
        public static void EnterCommunicationMode(Context ctx)
        {
            var am = AM(ctx);
            try
            {
                am.Mode = Mode.InCommunication;
                am.SpeakerphoneOn = false;
                am.MicrophoneMute = false;
            } catch { }
        }

        public static void LeaveCommunicationMode(Context ctx)
        {
            var am = AM(ctx);
            try
            {
                try { StopSco(ctx); } catch { }
                am.Mode = Mode.Normal;
                am.SpeakerphoneOn = false;
            } catch { }
        }

        /// <summary>Essayez d'activer SCO et attendez brièvement. Retourne true si SCO est actif.</summary>
        public static bool EnsureSco(Context ctx, int timeoutMs = 3000)
        {
            var am = AM(ctx);
            try
            {
                if (am.BluetoothScoOn) return true;
                am.StartBluetoothSco();
                am.BluetoothScoOn = true;
            } catch { }

            var t0 = Java.Lang.JavaSystem.CurrentTimeMillis();
            while (Java.Lang.JavaSystem.CurrentTimeMillis() - t0 < timeoutMs)
            {
                if (IsScoOn(ctx)) return true;
                try { System.Threading.Thread.Sleep(50); } catch { }
            }
            return IsScoOn(ctx);
        }

        public static void StopSco(Context ctx)
        {
            var am = AM(ctx);
            try { am.BluetoothScoOn = false; am.StopBluetoothSco(); } catch { }
        }

        public static bool IsScoOn(Context ctx)
        {
            try { return AM(ctx).BluetoothScoOn; } catch { return false; }
        }

        // ====== Device insight / LE-Audio vs A2DP ======
        public static string GetActiveRouteInfo(Context ctx)
        {
            try
            {
                var am = AM(ctx);
                var outs = am.GetDevices(GetDevicesTargets.Outputs);
                bool hasBle = outs.Any(d => d.Type == AudioDeviceType.BleHeadset || d.Type == AudioDeviceType.BleSpeaker || d.Type == AudioDeviceType.BleBroadcast);
                bool hasA2dp = outs.Any(d => d.Type == AudioDeviceType.BluetoothA2dp || d.Type == AudioDeviceType.BluetoothSco);
                bool sco = IsScoOn(ctx);

                if (sco) return "SCO (HFP/VoiceCall)";
                if (hasBle) return "LE (LC3) — auto";
                if (hasA2dp) return "A2DP (Media)";
                return "Device speaker";
            }
            catch { return "(unknown)"; }
        }

        public static bool IsLeActive(Context ctx)
        {
            try
            {
                var outs = AM(ctx).GetDevices(GetDevicesTargets.Outputs);
                return outs.Any(d => d.Type == AudioDeviceType.BleHeadset || d.Type == AudioDeviceType.BleSpeaker || d.Type == AudioDeviceType.BleBroadcast);
            }
            catch { return false; }
        }

        public static bool IsA2dpActive(Context ctx)
        {
            try
            {
                var outs = AM(ctx).GetDevices(GetDevicesTargets.Outputs);
                return outs.Any(d => d.Type == AudioDeviceType.BluetoothA2dp);
            } catch { return false; }
        }

        // ====== Device callbacks ======
        public static void RegisterDeviceCallback(Context ctx, AudioDeviceCallback cb)
        {
            if (cb == null) return;
            try
            {
                _callbackCached = cb;
                AM(ctx).RegisterAudioDeviceCallback(cb, null);
            } catch { }
        }

        public static void UnregisterDeviceCallback(Context ctx, AudioDeviceCallback cb)
        {
            try
            {
                var am = AM(ctx);
                am.UnregisterAudioDeviceCallback(cb ?? _callbackCached);
            } catch { }
        }
    }
}
