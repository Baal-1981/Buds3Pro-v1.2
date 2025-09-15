using System;
using System.Timers;

namespace Buds3ProAideAuditiveIA.v2
{
    public sealed class LatencyMeter : IDisposable
    {
        private readonly AudioEngine _engine;
        private readonly Action<int> _onLatencyMs;
        private readonly Timer _timer;

        public LatencyMeter(AudioEngine engine, Action<int> onLatencyMs, int periodMs = 1000)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _onLatencyMs = onLatencyMs ?? (_ => { });
            _timer = new Timer(periodMs);
            _timer.Elapsed += (s, e) => _onLatencyMs(_engine.EstimatedLatencyMs);
            _timer.AutoReset = true;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            try { _timer?.Stop(); } catch { }
            try { _timer?.Dispose(); } catch { }
        }
    }
}
