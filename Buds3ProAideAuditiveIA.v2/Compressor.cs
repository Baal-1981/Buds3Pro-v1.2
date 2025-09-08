namespace Buds3ProAideAuditiveIA.v2
{
    public class Compressor
    {
        public double Threshold = 0.5;   // seuil
        public double Ratio = 3.0;   // compression
        public double AttackMs = 5.0;   // attaque
        public double ReleaseMs = 50.0;  // relâchement
        public double MakeupDb = 0.0;   // gain de compensation
        public int SampleRate = 16000; // échantillonnage

        private double env = 0.0;

        public void Reset()
        {
            env = 0.0;
        }

        public void ProcessInPlace(float[] x, int n)
        {
            double att = System.Math.Exp(-1.0 / (0.001 * AttackMs * SampleRate));
            double rel = System.Math.Exp(-1.0 / (0.001 * ReleaseMs * SampleRate));
            double makeup = System.Math.Pow(10.0, MakeupDb / 20.0);

            for (int i = 0; i < n; i++)
            {
                double a = System.Math.Abs(x[i]);
                if (a > env)
                    env = att * (env - a) + a;
                else
                    env = rel * (env - a) + a;

                double gain = 1.0;
                if (env > Threshold)
                {
                    double over = env / Threshold;
                    double comp = System.Math.Pow(over, 1.0 - (1.0 / Ratio));
                    gain = 1.0 / comp;
                }

                x[i] = (float)(x[i] * gain * makeup);
            }
        }
    }
}
