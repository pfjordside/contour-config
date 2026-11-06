namespace ContourConfig.Core
{
    public sealed class EndDriftBooster
    {
        public const double MaximumStepMilliseconds = 50;
        private readonly DriftOptions options;
        private RollerEnd end;
        private double heldSince;
        private double previousTick;
        private double remainder;

        public EndDriftBooster(DriftOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
            this.options = options;
        }

        public RollerEnd End => end;

        public void Update(RollerEnd value, double nowMilliseconds)
        {
            if (value == end)
            {
                return;
            }
            end = value;
            heldSince = nowMilliseconds;
            previousTick = nowMilliseconds;
            remainder = 0;
        }

        // Returns signed horizontal units to inject for the time elapsed since the previous tick.
        public int Tick(double nowMilliseconds)
        {
            if (end == RollerEnd.None || !double.IsFinite(nowMilliseconds))
            {
                return 0;
            }
            double held = nowMilliseconds - heldSince;
            if (held > options.MaximumBoostMilliseconds)
            {
                Update(RollerEnd.None, nowMilliseconds);
                return 0;
            }
            double activation = heldSince + options.ActivationDelayMilliseconds;
            double from = Math.Max(previousTick, activation);
            previousTick = Math.Max(previousTick, nowMilliseconds);
            if (nowMilliseconds <= from)
            {
                return 0;
            }

            // A stalled timer must never produce a large catch-up jump.
            double elapsed = Math.Min(nowMilliseconds - from, MaximumStepMilliseconds);
            double speed = end == RollerEnd.Left ? options.LeftAdditionalSpeed : options.RightAdditionalSpeed;
            remainder += speed * elapsed / 1000;
            int units = (int)Math.Floor(remainder + 1e-9);
            remainder = Math.Max(0, remainder - units);
            return end == RollerEnd.Left ? -units : units;
        }

        public void Reset() => Update(RollerEnd.None, 0);
    }
}
