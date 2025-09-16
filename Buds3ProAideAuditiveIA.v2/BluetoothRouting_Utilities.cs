using System;
using System.Linq;
using Android.Content;
using Android.Media;
using Android.OS;

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

        /// <summary>Active SCO et attend CONNECTED via broadcast (jusqu’à timeoutMs). Retourne true si SCO est actif.</summary>
        public static bool EnsureSco(Context ctx, int timeoutMs = 4000)
        {
            var am = AM(ctx);
            try
            {
                // Toujours en mode InCommunication AVANT de démarrer le SCO
                try { am.Mode = Mode.InCommunication; } catch { }
                try { am.SpeakerphoneOn = false; } catch { }

                if (am.BluetoothScoOn) return true;

                bool connected = false;
                BroadcastReceiver br = null;

                try
                {
                    // Receiver pour l’état SCO (on évite les constantes obsolètes)
                    br = new ScoStateReceiver(() => connected = true);
                    ctx.RegisterReceiver(br, new IntentFilter(AudioManager.ActionScoAudioStateUpdated));
                }
                catch { /* best effort */ }

                try
                {
                    am.StartBluetoothSco();
                    am.BluetoothScoOn = true;
                }
                catch { }

                var t0 = Java.Lang.JavaSystem.CurrentTimeMillis();
                while (Java.Lang.JavaSystem.CurrentTimeMillis() - t0 < timeoutMs)
                {
                    if (connected || IsScoOn(ctx)) break;
                    try { System.Threading.Thread.Sleep(50); } catch { }
                }

                try { if (br != null) ctx.UnregisterReceiver(br); } catch { }
                return IsScoOn(ctx);
            }
            catch
            {
                return false;
            }
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

        /// <summary>API 31+: force le device de communication vers le casque SCO s'il est présent.</summary>
        public static void ForceCommunicationDeviceSco(Context ctx)
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.S) return;
                var am = AM(ctx);
                var outs = am.GetDevices(GetDevicesTargets.Outputs);
                var sco = outs.FirstOrDefault(d => d?.Type == AudioDeviceType.BluetoothSco);
                if (sco != null)
                {
                    try { am.SetCommunicationDevice(sco); } catch { }
                }
            }
            catch { }
        }

        private sealed class ScoStateReceiver : BroadcastReceiver
        {
            private readonly Action _onConnected;
            public ScoStateReceiver(Action onConnected) { _onConnected = onConnected; }

            public override void OnReceive(Context context, Intent intent)
            {
                if (intent == null) return;

                if (intent.Action == AudioManager.ActionScoAudioStateUpdated)
                {
                    // Utiliser l'enum ScoAudioState (pas les constantes obsolètes)
                    var state = (ScoAudioState)intent.GetIntExtra(
                        AudioManager.ExtraScoAudioState,
                        (int)ScoAudioState.Disconnected);

                    if (state == ScoAudioState.Connected)
                        _onConnected?.Invoke();
                }
            }
        }
    }
}
