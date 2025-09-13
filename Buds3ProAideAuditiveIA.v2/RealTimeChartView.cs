
using System;
using Android.Content;
using Android.Graphics;
using Android.Views;

namespace Buds3ProAideAuditivelA.v2
{
    public sealed class RealtimeChartView : View
    {
        private const int Capacity = 512;
        private readonly float[] _pk = new float[Capacity];
        private readonly float[] _rms = new float[Capacity];
        private readonly float[] _gr = new float[Capacity];
        private readonly float[] _hr = new float[Capacity];
        private int _head;
        private bool _wrapped;
        private readonly Paint _axis = new Paint() { AntiAlias = true, StrokeWidth = 1 };
        private readonly Paint _text = new Paint() { AntiAlias = true, TextSize = 28 };
        private readonly Paint _pPk = new Paint() { AntiAlias = true, StrokeWidth = 3, StrokeCap = Paint.Cap.Round };
        private readonly Paint _pRms = new Paint() { AntiAlias = true, StrokeWidth = 3, StrokeCap = Paint.Cap.Round };
        private readonly Paint _pGr = new Paint() { AntiAlias = true, StrokeWidth = 3, StrokeCap = Paint.Cap.Round };
        private readonly Paint _pHr = new Paint() { AntiAlias = true, StrokeWidth = 3, StrokeCap = Paint.Cap.Round };
        private const float TopDb = 0f;
        private const float BottomDb = -60f;
        public RealtimeChartView(Context ctx) : base(ctx)
        {
            _axis.Color = Color.Argb(80, 200, 200, 200);
            _text.Color = Color.Argb(200, 230, 230, 230);
            _pPk.Color = Color.Argb(255, 244, 67, 54);
            _pRms.Color = Color.Argb(255, 76, 175, 80);
            _pGr.Color = Color.Argb(255, 3, 169, 244);
            _pHr.Color = Color.Argb(255, 255, 193, 7);
            for (int i = 0; i < Capacity; i++) _pk[i] = _rms[i] = _gr[i] = _hr[i] = -120f;
        }
        public void AddPoint(float peakDb, float rmsDb, float grDb, float headroomDb)
        {
            if (float.IsNaN(peakDb)) peakDb = -120; if (peakDb > 0) peakDb = 0;
            if (float.IsNaN(rmsDb)) rmsDb = -120; if (rmsDb > 0) rmsDb = 0;
            if (float.IsNaN(grDb)) grDb = 0; if (grDb < -60) grDb = -60;
            if (float.IsNaN(headroomDb)) headroomDb = 0; if (headroomDb < 0) headroomDb = 0;
            _pk[_head] = peakDb; _rms[_head] = rmsDb; _gr[_head] = grDb; _hr[_head] = headroomDb;
            _head = (_head + 1) % Capacity; if (_head == 0) _wrapped = true;
            PostInvalidateOnAnimation();
        }
        protected override void OnDraw(Canvas c)
        {
            base.OnDraw(c);
            float w = Width, h = Height; if (w <= 0 || h <= 0) return;
            c.DrawColor(Color.Argb(255, 18, 18, 18));
            for (int i = 0; i <= 3; i++)
            {
                float db = TopDb - i * 20f;
                float y = MapDbToY(db, h);
                c.DrawLine(0, y, w, y, _axis);
                c.DrawText(string.Format("{0:0} dB", db), 8, y - 6, _text);
            }
            DrawSeries(c, _pk, w, h, _pPk);
            DrawSeries(c, _rms, w, h, _pRms);
            DrawSeries(c, _gr, w, h, _pGr);
            DrawSeries(c, _hr, w, h, _pHr);
        }
        private void DrawSeries(Canvas c, float[] ser, float w, float h, Paint p)
        {
            int n = _wrapped ? Capacity : _head; if (n < 2) return;
            float dx = w / (Capacity - 1); int idx = _wrapped ? _head : 0;
            float px = 0, py = 0;
            for (int i = 0; i < n; i++)
            {
                int j = _wrapped ? (idx + i) % Capacity : i;
                float x = i * dx; float y = MapDbToY(ser[j], h);
                if (i > 0) c.DrawLine(px, py, x, y, p); px = x; py = y;
            }
        }
        private static float MapDbToY(float db, float h)
        {
            if (db > TopDb) db = TopDb; if (db < BottomDb) db = BottomDb;
            float t = (TopDb - db) / (TopDb - BottomDb);
            return t * (h - 12) + 6;
        }
    }
}
