
using System;
using System.Collections.Generic;
namespace Buds3ProAideAuditivelA.v2
{
    public sealed class LatencyStats
    {
        private readonly int _window;
        private readonly Queue<int> _q = new Queue<int>();
        private double _ema = double.NaN;
        private readonly double _alpha;
        public LatencyStats(int windowCount = 25, double alpha = 0.25)
        { if (windowCount < 5) windowCount = 5; _window = windowCount; _alpha = Math.Max(0.01, Math.Min(0.9, alpha)); }
        public void Push(int ms)
        { if (ms <= 0) return; if (double.IsNaN(_ema)) _ema = ms; else _ema = _alpha * ms + (1 - _alpha) * _ema; _q.Enqueue(ms); while (_q.Count > _window) _q.Dequeue(); }
        public (int avg, int min, int max) View()
        {
            if (_q.Count == 0) return (0, 0, 0);
            int lo = int.MaxValue, hi = int.MinValue; long sum = 0;
            foreach (var v in _q) { if (v < lo) lo = v; if (v > hi) hi = v; sum += v; }
            return ((int)Math.Round(_ema), lo, hi);
        }
    }
}
