namespace ContourConfig.Core
{
    // Learns which marker bit is held longest while the user holds one roller end.
    public sealed class MarkerLearner
    {
        private readonly double[] heldMilliseconds = new double[8];
        private byte current;
        private double since;

        public void Observe(byte markers, double nowMilliseconds)
        {
            Accumulate(nowMilliseconds);
            current = markers;
            since = nowMilliseconds;
        }

        public byte? Finish(double nowMilliseconds, double minimumHeldMilliseconds)
        {
            Accumulate(nowMilliseconds);
            current = 0;
            int best = Array.IndexOf(heldMilliseconds, heldMilliseconds.Max());
            return heldMilliseconds[best] >= minimumHeldMilliseconds ? (byte)(1 << best) : null;
        }

        private void Accumulate(double nowMilliseconds)
        {
            double elapsed = nowMilliseconds - since;
            if (elapsed <= 0)
            {
                return;
            }
            for (int bit = 0; bit < 8; bit++)
            {
                if ((current & (1 << bit)) != 0)
                {
                    heldMilliseconds[bit] += elapsed;
                }
            }
        }
    }
}
