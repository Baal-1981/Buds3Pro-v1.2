using System;
using System.Threading;
using System.Threading.Tasks;

namespace Buds3ProAideAuditiveIA.v2
{
    public enum LatencyProfile
    {
        Ultra,    // ~5ms
        Fast,     // ~10ms
        Balanced, // ~20ms
        Safe      // ~40ms
    }

    // This file assumes you already have a partial AudioEngine class elsewhere.
    public sealed partial class AudioEngine
    {
        private int _frameSize;      // samples per channel per processing frame
        private int _bufferCount;    // AudioTrack/AudioRecord buffer count
        private volatile bool _running;

        /// <summary>
        /// Apply profile and safely restart audio I/O to take effect.
        /// </summary>
        public async Task ApplyLatencyProfileAsync(LatencyProfile profile, CancellationToken ct = default)
        {
            var (frame, buffers) = GetParams(profile);
            await RestartWithAsync(frame, buffers, ct).ConfigureAwait(false);
        }

        private (int frame, int buffers) GetParams(LatencyProfile p) => p switch
        {
            LatencyProfile.Ultra    => (96, 2),   // ~2ms @48kHz per frame x 2 -> ~4-5ms
            LatencyProfile.Fast     => (240, 2),  // ~5ms x2 -> ~10ms
            LatencyProfile.Balanced => (480, 2),  // ~10ms x2 -> ~20ms
            LatencyProfile.Safe     => (960, 2),  // ~20ms x2 -> ~40ms
            _ => (480, 2)
        };

        private async Task RestartWithAsync(int frame, int buffers, CancellationToken ct)
        {
            // Stop pipeline
            await StopAsync(ct).ConfigureAwait(false);

            _frameSize = frame;
            _bufferCount = buffers;

            // Rebuild AudioRecord/AudioTrack with new params.
            await StartAsync(ct).ConfigureAwait(false);
        }

        // Stubs – these should bind your real start/stop logic.
        private Task StopAsync(CancellationToken ct)
        {
            _running = false;
            return Task.CompletedTask;
        }
        private Task StartAsync(CancellationToken ct)
        {
            _running = true;
            return Task.CompletedTask;
        }
    }
}
