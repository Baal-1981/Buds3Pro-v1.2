using System;
using System.Runtime.InteropServices;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Wrapper minimal pour l'Audio Processing Module (WebRTC) côté natif.
    /// La lib côté Android doit être packagée sous le nom: libwebrtc_apm.so
    /// (=> DllImport("webrtc_apm")) et exposer les symboles C ci-dessous.
    /// </summary>
    internal sealed class ApmWrapper : IDisposable
    {
        private IntPtr _handle;
        private bool _loaded;

        // --- Fonctions natives (C wrapper) ---
        // int apm_create(int sample_rate, int enable_ns, int enable_agc, int enable_aec) -> handle*
        [DllImport("webrtc_apm", EntryPoint = "apm_create", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr apm_create(int sample_rate, int enable_ns, int enable_agc, int enable_aec);

        // void apm_configure(handle*, int ns, int agc, int aec)
        [DllImport("webrtc_apm", EntryPoint = "apm_configure", CallingConvention = CallingConvention.Cdecl)]
        private static extern void apm_configure(IntPtr h, int enable_ns, int enable_agc, int enable_aec);

        // void apm_set_stream_delay_ms(handle*, int delayMs)
        [DllImport("webrtc_apm", EntryPoint = "apm_set_stream_delay_ms", CallingConvention = CallingConvention.Cdecl)]
        private static extern void apm_set_stream_delay_ms(IntPtr h, int delayMs);

        // int apm_process_mono(handle*, short* frame, int length)  // 0 = OK
        [DllImport("webrtc_apm", EntryPoint = "apm_process_mono", CallingConvention = CallingConvention.Cdecl)]
        private static extern int apm_process_mono(IntPtr h, short[] frame, int length);

        // void apm_free(handle*)
        [DllImport("webrtc_apm", EntryPoint = "apm_free", CallingConvention = CallingConvention.Cdecl)]
        private static extern void apm_free(IntPtr h);

        /// <summary>
        /// Tente de créer l'APM (retourne null si la lib native n'est pas présente).
        /// </summary>
        public static ApmWrapper TryCreate(int sampleRate, bool ns, bool agc, bool aec)
        {
            try
            {
                var h = apm_create(sampleRate, ns ? 1 : 0, agc ? 1 : 0, aec ? 1 : 0);
                if (h == IntPtr.Zero) return null;
                return new ApmWrapper { _handle = h, _loaded = true };
            }
            catch (DllNotFoundException)
            {
                return null; // lib absente: on continue sans APM
            }
            catch
            {
                return null; // échec de création: ignorer proprement
            }
        }

        public bool IsLoaded => _loaded;

        public void Configure(bool ns, bool agc, bool aec)
        {
            if (!_loaded) return;
            apm_configure(_handle, ns ? 1 : 0, agc ? 1 : 0, aec ? 1 : 0);
        }

        public void SetDelayMs(int delayMs)
        {
            if (!_loaded) return;
            apm_set_stream_delay_ms(_handle, delayMs);
        }

        /// <summary>
        /// Traite IN-PLACE une trame mono (ex: 10 ms @ 48 kHz → 480 échantillons).
        /// Retourne false si l’APM n’est pas chargé ou en cas d’erreur native.
        /// </summary>
        public bool ProcessMono(short[] frame, int length)
        {
            if (!_loaded || frame == null || length <= 0) return false;
            if (length > frame.Length) length = frame.Length;
            return apm_process_mono(_handle, frame, length) == 0;
        }

        public void Dispose()
        {
            if (_loaded && _handle != IntPtr.Zero)
            {
                try { apm_free(_handle); } catch { /* ignore */ }
                _handle = IntPtr.Zero;
                _loaded = false;
            }
        }
    }
}
