namespace ContourConfig.Core
{
    public sealed record DriftOptions(
        double LeftAdditionalSpeed = 1500,
        double RightAdditionalSpeed = 1500,
        int ActivationDelayMilliseconds = 300,
        int MaximumBoostMilliseconds = 15000)
    {
        public const double MaximumSpeed = 8000;

        public void Validate()
        {
            if (!double.IsFinite(LeftAdditionalSpeed) || LeftAdditionalSpeed is < 0 or > MaximumSpeed)
            {
                throw new ArgumentOutOfRangeException(nameof(LeftAdditionalSpeed), $"Additional speed must be between 0 and {MaximumSpeed} pixels per second.");
            }
            if (!double.IsFinite(RightAdditionalSpeed) || RightAdditionalSpeed is < 0 or > MaximumSpeed)
            {
                throw new ArgumentOutOfRangeException(nameof(RightAdditionalSpeed), $"Additional speed must be between 0 and {MaximumSpeed} pixels per second.");
            }
            if (ActivationDelayMilliseconds is < 0 or > 2000)
            {
                throw new ArgumentOutOfRangeException(nameof(ActivationDelayMilliseconds), "Activation delay must be between 0 and 2000 milliseconds.");
            }
            if (MaximumBoostMilliseconds is < 1000 or > 60000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBoostMilliseconds), "Maximum boost duration must be between 1 and 60 seconds.");
            }
        }
    }
}
