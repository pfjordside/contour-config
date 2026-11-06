namespace ContourConfig.Core
{
    public sealed record DriftConfiguration
    {
        public const int CurrentVersion = 2;

        public int Version { get; init; } = CurrentVersion;
        public DriftOptions Options { get; init; } = new();
        public EndMarkerMap Markers { get; init; } = new();
        public bool EnableBoostOnStart { get; init; }

        public void Validate()
        {
            // Version 1 used calibrated movement patterns; only its speed options carry over.
            if (Version is not (1 or CurrentVersion))
            {
                throw new InvalidDataException($"Unsupported settings version: {Version}.");
            }
            ArgumentNullException.ThrowIfNull(Options);
            ArgumentNullException.ThrowIfNull(Markers);
            Options.Validate();
            Markers.Validate();
        }
    }
}
