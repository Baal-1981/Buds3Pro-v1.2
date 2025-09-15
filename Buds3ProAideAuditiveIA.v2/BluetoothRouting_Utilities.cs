using System;
using System.Linq;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Runtime;

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
            }
            catch { }
        }

        public static void LeaveCommunicationMode(Context ctx)
        {
            var am = AM(ctx);
            try
            {
                try { StopSco(ctx); } catch { }
                am.Mode = Mode.Normal;
                am.SpeakerphoneOn = false;
            }
            catch { }
        }

        /// <summary>Active SCO et attend jusqu’à <paramref name="timeoutMs"/> ms. Retourne true si SCO est actif.</summary>
        public static bool EnsureSco(Context ctx, int timeoutMs = 3000)
        {
            var am = AM(ctx);
            try
            {
                if (am.BluetoothScoOn) return true;
                am.StartBluetoothSco();
                am.BluetoothScoOn = true;
            }
            catch { }

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

        // ====== Infos route / LE-Audio vs A2DP ======
        public static string GetActiveRouteInfo(Context ctx)
        {
            try
            {
                var outs = AM(ctx).GetDevices(GetDevicesTargets.Outputs);
                bool sco = IsScoOn(ctx);
                bool hasBle = outs.Any(d =>
                    d.Type == AudioDeviceType.BleHeadset ||
                    d.Type == AudioDeviceType.BleSpeaker ||
                    d.Type == AudioDeviceType.BleBroadcast);
                bool hasA2dp = outs.Any(d =>
                    d.Type == AudioDeviceType.BluetoothA2dp ||
                    d.Type == AudioDeviceType.BluetoothSco);

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
                return outs.Any(d =>
                    d.Type == AudioDeviceType.BleHeadset ||
                    d.Type == AudioDeviceType.BleSpeaker ||
                    d.Type == AudioDeviceType.BleBroadcast);
            }
            catch { return false; }
        }

        public static bool IsA2dpActive(Context ctx)
        {
            try
            {
                var outs = AM(ctx).GetDevices(GetDevicesTargets.Outputs);
                return outs.Any(d => d.Type == AudioDeviceType.BluetoothA2dp);
            }
            catch { return false; }
        }

        // ====== Device callbacks ======
        public static void RegisterDeviceCallback(Context ctx, AudioDeviceCallback cb)
        {
            if (cb == null) return;
            try
            {
                _callbackCached = cb;
                AM(ctx).RegisterAudioDeviceCallback(cb, null);
            }
            catch { }
        }

        public static void UnregisterDeviceCallback(Context ctx, AudioDeviceCallback cb)
        {
            try
            {
                var am = AM(ctx);
                am.UnregisterAudioDeviceCallback(cb ?? _callbackCached);
            }
            catch { }
        }

        // ====== Helpers de binding explicite (API >= 23) ======
        public static void BindRecordToBtSco(AudioRecord rec, Context ctx = null)
        {
            if (rec == null || Build.VERSION.SdkInt < BuildVersionCodes.M) return;
            try
            {
                var am = ctx != null
                    ? AM(ctx)
                    : (AudioManager)Android.App.Application.Context.GetSystemService(Context.AudioService);

                // Pour l'entrée, inspecter les INPUTS
                var inputs = am?.GetDevices(GetDevicesTargets.Inputs);
                var scoIn = inputs?.FirstOrDefault(d => d?.Type == AudioDeviceType.BluetoothSco)
                            ?? am?.GetDevices(GetDevicesTargets.All)?.FirstOrDefault(d => d?.Type == AudioDeviceType.BluetoothSco);

                if (scoIn != null)
                {
                    try { rec.SetPreferredDevice(scoIn); } catch { }
                }
            }
            catch { }
        }

        public static void BindTrackToBtSco(AudioTrack trk, Context ctx = null)
        {
            if (trk == null || Build.VERSION.SdkInt < BuildVersionCodes.M) return;
            try
            {
                var am = ctx != null
                    ? AM(ctx)
                    : (AudioManager)Android.App.Application.Context.GetSystemService(Context.AudioService);

                // Pour la sortie, inspecter les OUTPUTS
                var outs = am?.GetDevices(GetDevicesTargets.Outputs);
                var target = outs?.FirstOrDefault(d => d?.Type == AudioDeviceType.BluetoothSco)
                             ?? outs?.FirstOrDefault(d => d?.Type == AudioDeviceType.BluetoothA2dp); // fallback

                if (target != null)
                {
                    try { trk.SetPreferredDevice(target); } catch { }
                }
            }
            catch { }
        }
    }
}
