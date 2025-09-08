using System;

namespace Buds3ProAideAuditiveIA.v2
{
    public sealed class Highpass100Hz
    {
        private double _prevIn, _prevOut;
        private readonly double _a;
        public Highpass100Hz(int sampleRate = 48000, double fc = 100.0)
        {
            var dt = 1.0 / sampleRate;
            var rc = 1.0 / (2.0 * Math.PI * fc);
            var alpha = rc / (rc + dt);
            _a = alpha; // y[n] = a*(y[n-1] + x[n] - x[n-1])
        }
        public short Process(short s)
        {
            var x = s / 32768.0;
            var y = _a * (_prevOut + x - _prevIn);
            _prevIn = x; _prevOut = y;
            var v = Math.Max(-1.0, Math.Min(1.0, y));
            return (short)Math.Round(v * 32767.0);
        }
        public void ProcessBuffer(short[] buf, int count)
        {
            for (int i = 0; i < count; i++) buf[i] = Process(buf[i]);
        }
    }

    public sealed class TiltEqClarity
    {
        private double _prevIn;
        private readonly double _a;
        private readonly double _makeup;
        public TiltEqClarity(double a = 0.85, double makeupDb = 2.0)
        {
            _a = a; _makeup = Math.Pow(10.0, makeupDb / 20.0);
        }
        public short Process(short s)
        {
            var x = s / 32768.0;
            var y = (x - _a * _prevIn) * _makeup;
            _prevIn = x;
            var v = Math.Max(-1.0, Math.Min(1.0, y));
            return (short)Math.Round(v * 32767.0);
        }
        public void ProcessBuffer(short[] buf, int count)
        {
            for (int i = 0; i < count; i++) buf[i] = Process(buf[i]);
        }
    }
}
