using System;
using System.Runtime.InteropServices;

namespace Buds3ProAideAuditiveIA   // ← adapte si besoin au namespace du projet
{
    internal sealed class ApmWrapper : IDisposable
    {
        IntPtr _handle;
        bool _loaded;

        // Fonctions natives (C wrapper)
        // Remarque: la lib doit s'appeler "webrtc_apm" côté .so -> libwebrtc_apm.so
        [DllImport("webrtc_apm", EntryPoint = "apm_create", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr apm_create(int sample_rate, int enable_ns, int enable_agc, int enable_aec);

        [DllImport("webrtc_apm", EntryPoint = "apm_configure", CallingConvention = CallingConvention.Cdecl)]
        static extern void apm_configure(IntPtr h, int enable_ns, int enable_agc, int enable_aec);

        [DllImport("webrtc_apm", EntryPoint = "apm_set_stream_delay_ms", CallingConvention = CallingConvention.Cdecl)]
        static extern void apm_set_stream_delay_ms(IntPtr h, int delayMs);

        [DllImport("webrtc_apm", EntryPoint = "apm_process_mono", CallingConvention = CallingConvention.Cdecl)]
        static extern int apm_process_mono(IntPtr h, short[] frame, int length); // length = nb échantillons

        [DllImport("webrtc_apm", EntryPoint = "apm_free", CallingConvention = CallingConvention.Cdecl)]
        static extern void apm_free(IntPtr h);

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
                return null;
            }
        }

        public bool IsLoaded => _loaded;

        public void Configure(bool ns, bool agc, bool aec)
        {
            if (!_loaded) return;
            apm_configure(_handle, ns ? 1 : 0, agc ? 1 : 0, aec ? 1 : 0);
        }

        public void SetDelayMs(int delay)
        {
            if (!_loaded) return;
            apm_set_stream_delay_ms(_handle, delay);
        }

        // Traite IN-PLACE une frame mono 10 ms (ex: 160 échantillons @16k)
        public bool ProcessMono(short[] frame, int length)
        {
            if (!_loaded) return false;
            return apm_process_mono(_handle, frame, length) == 0;
        }

        public void Dispose()
        {
            if (_loaded && _handle != IntPtr.Zero)
            {
                try { apm_free(_handle); } catch { }
                _handle = IntPtr.Zero;
                _loaded = false;
            }
        }
    }
}
