namespace ContourConfig.Core
{
    [Flags]
    public enum BoostSuspension
    {
        None = 0,
        SessionLocked = 1,
        SessionDisconnected = 2,
        SystemSuspended = 4
    }

    public sealed class BoostController
    {
        private DriftOptions options = new();
        private EndDriftBooster? booster;
        private BoostSuspension suspension;
        private double resumedAtMilliseconds = double.NegativeInfinity;

        public bool Enabled { get; private set; }
        public bool IsSuspended => suspension != BoostSuspension.None;
        public RollerEnd End { get; private set; }

        public void SetOptions(DriftOptions value, double nowMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
            options = value;
            if (Enabled && !IsSuspended)
            {
                Start(nowMilliseconds);
            }
        }

        public void SetEnabled(bool enabled, double nowMilliseconds)
        {
            if (Enabled == enabled)
            {
                return;
            }
            Enabled = enabled;
            if (enabled && !IsSuspended)
            {
                Start(nowMilliseconds);
            }
            else
            {
                booster = null;
            }
        }

        public void SetSuspended(BoostSuspension reason, bool suspended, double nowMilliseconds)
        {
            bool wasSuspended = IsSuspended;
            suspension = suspended ? suspension | reason : suspension & ~reason;
            if (!wasSuspended && IsSuspended)
            {
                End = RollerEnd.None;
                booster = null;
            }
            else if (wasSuspended && !IsSuspended)
            {
                resumedAtMilliseconds = nowMilliseconds;
                if (Enabled)
                {
                    Start(nowMilliseconds);
                }
            }
        }

        public void Update(RollerEnd end, double nowMilliseconds)
        {
            if (!IsSuspended && nowMilliseconds >= resumedAtMilliseconds)
            {
                End = end;
                booster?.Update(end, nowMilliseconds);
            }
        }

        public int Tick(double nowMilliseconds) => booster?.Tick(nowMilliseconds) ?? 0;

        private void Start(double nowMilliseconds)
        {
            booster = new(options);
            booster.Update(End, nowMilliseconds);
        }
    }
}
