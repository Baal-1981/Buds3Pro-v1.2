using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Media;

namespace Buds3ProAideAuditiveIA.v2
{
    public static class LatencyMeter
    {
        public static async Task<int?> MeasureAsync(AudioRecord rec, AudioTrack track, int sampleRate, CancellationToken ct)
        {
            int pulseLen = sampleRate / 100; // 10 ms
            short[] pulse = new short[pulseLen];
            pulse[0] = 30000;

            int captureMs = 400;
            int captureLen = (int)(sampleRate * (captureMs / 1000.0));
            short[] cap = new short[captureLen];

            await Task.Delay(50, ct);

            track.Play();
            int wrote = track.Write(pulse, 0, pulse.Length, WriteMode.Blocking);
            if (wrote <= 0) return null;

            int read = 0;
            while (read < cap.Length)
            {
                ct.ThrowIfCancellationRequested();
                int n = rec.Read(cap, read, cap.Length - read);
                if (n <= 0) break;
                read += n;
            }
            if (read <= 0) return null;

            int bestIndex = 0; double bestVal = double.NegativeInfinity;
            for (int i = 0; i < read; i++)
            {
                double v = Math.Abs(cap[i]);
                if (v > bestVal) { bestVal = v; bestIndex = i; }
            }
            double ms = 1000.0 * bestIndex / sampleRate;
            return (int)Math.Round(ms);
        }
    }
}
