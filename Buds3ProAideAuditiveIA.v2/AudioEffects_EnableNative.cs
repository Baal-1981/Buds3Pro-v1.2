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

        AcousticEchoCanceler _aec;
        NoiseSuppressor _ns;
        AutomaticGainControl _agc;
        int _sessionId = -1;

        public static bool IsAecAvailable => AcousticEchoCanceler.IsAvailable;
        public static bool IsNsAvailable => NoiseSuppressor.IsAvailable;
        public static bool IsAgcAvailable => AutomaticGainControl.IsAvailable;

        public void AttachToInputSession(int audioSessionId, bool wantAec, bool wantNs, bool wantAgc)
        {
            Detach();
            _sessionId = audioSessionId;
            AecRequested = wantAec; NsRequested = wantNs; AgcRequested = wantAgc;

            if (_sessionId <= 0) return;

            if (wantAec && IsAecAvailable)
            {
                try { _aec = AcousticEchoCanceler.Create(_sessionId); if (_aec != null) { _aec.SetEnabled(true); AecActive = _aec.Enabled; } }
                catch { AecActive = false; }
            }
            if (wantNs && IsNsAvailable)
            {
                try { _ns = NoiseSuppressor.Create(_sessionId); if (_ns != null) { _ns.SetEnabled(true); NsActive = _ns.Enabled; } }
                catch { NsActive = false; }
            }
            if (wantAgc && IsAgcAvailable)
            {
                try { _agc = AutomaticGainControl.Create(_sessionId); if (_agc != null) { _agc.SetEnabled(true); AgcActive = _agc.Enabled; } }
                catch { AgcActive = false; }
            }
        }

        public void Detach()
        {
            AecActive = NsActive = AgcActive = false;
            try { if (_aec != null) { try { _aec.SetEnabled(false); } catch { } _aec.Release(); } } catch { }
            try { if (_ns != null) { try { _ns.SetEnabled(false); } catch { } _ns.Release(); } } catch { }
            try { if (_agc != null) { try { _agc.SetEnabled(false); } catch { } _agc.Release(); } } catch { }
            _aec = null; _ns = null; _agc = null; _sessionId = -1;
        }

        public void Dispose() => Detach();
    }
}
