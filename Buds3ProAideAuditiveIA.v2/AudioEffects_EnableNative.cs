using System;
using Android.Media.Audiofx;

namespace Buds3ProAideAuditiveIA.v2
{
    public sealed class AudioEffects_EnableNative : IDisposable
    {
        public bool AecRequested { get; private set; }
        public bool NsRequested { get; private set; }
        public bool AgcRequested { get; private set; }

        public bool AecActive { get; private set; }
        public bool NsActive { get; private set; }
        public bool AgcActive { get; private set; }

        public int SessionId => _sessionId;

        private AcousticEchoCanceler _aec;
        private NoiseSuppressor _ns;
        private AutomaticGainControl _agc;
        private int _sessionId = -1;

        public static bool IsAecAvailable => AcousticEchoCanceler.IsAvailable;
        public static bool IsNsAvailable => NoiseSuppressor.IsAvailable;
        public static bool IsAgcAvailable => AutomaticGainControl.IsAvailable;

        public void AttachOrUpdate(int audioSessionId, bool wantAec, bool wantNs, bool wantAgc)
        {
            if (audioSessionId <= 0)
            {
                Detach();
                return;
            }

            if (_sessionId != audioSessionId)
            {
                Detach();
                _sessionId = audioSessionId;
            }

            AecRequested = wantAec; NsRequested = wantNs; AgcRequested = wantAgc;

            // AEC
            if (wantAec && IsAecAvailable)
            {
                if (_aec == null) { try { _aec = AcousticEchoCanceler.Create(_sessionId); } catch { _aec = null; } }
                TryEnable(_aec, true, out var activeAec);   // IDE0018: inline out var
                AecActive = activeAec;
            }
            else
            {
                TryEnable(_aec, false, out _);
                SafeRelease(ref _aec);
                AecActive = false;
            }

            // NS
            if (wantNs && IsNsAvailable)
            {
                if (_ns == null) { try { _ns = NoiseSuppressor.Create(_sessionId); } catch { _ns = null; } }
                TryEnable(_ns, true, out var activeNs);     // IDE0018
                NsActive = activeNs;
            }
            else
            {
                TryEnable(_ns, false, out _);
                SafeRelease(ref _ns);
                NsActive = false;
            }

            // AGC
            if (wantAgc && IsAgcAvailable)
            {
                if (_agc == null) { try { _agc = AutomaticGainControl.Create(_sessionId); } catch { _agc = null; } }
                TryEnable(_agc, true, out var activeAgc);   // IDE0018
                AgcActive = activeAgc;
            }
            else
            {
                TryEnable(_agc, false, out _);
                SafeRelease(ref _agc);
                AgcActive = false;
            }
        }

        public void Update(bool wantAec, bool wantNs, bool wantAgc)
        {
            if (_sessionId <= 0) return;
            AttachOrUpdate(_sessionId, wantAec, wantNs, wantAgc);
        }

        public void Detach()
        {
            AecActive = NsActive = AgcActive = false;

            TryEnable(_aec, false, out _);
            TryEnable(_ns, false, out _);
            TryEnable(_agc, false, out _);

            SafeRelease(ref _aec);
            SafeRelease(ref _ns);
            SafeRelease(ref _agc);

            _sessionId = -1;
        }

        public void Dispose() => Detach();

        // ===== Helpers =====
        private static void SafeRelease<T>(ref T fx) where T : AudioEffect
        {
            try { fx?.Release(); } catch { }
            fx = null;
        }

        private static void TryEnable(AudioEffect fx, bool on, out bool isOn)
        {
            isOn = false;
            if (fx == null) return;
            try { fx.SetEnabled(on); } catch { }
            try { isOn = fx.Enabled; } catch { isOn = false; }
        }
    }
}
