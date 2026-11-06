namespace ContourConfig.Core
{
    // The RollerMouse reports roller end contact as bits in a vendor byte of its consumer-control input report.
    public sealed record EndMarkerMap(byte LeftMask = 0x04, byte RightMask = 0x08)
    {
        public const byte ReportId = 0x03;
        public const int MarkerByteIndex = 7;

        public void Validate()
        {
            if (LeftMask == 0 || RightMask == 0)
            {
                throw new ArgumentException("End marker masks must be non-zero.");
            }
            if ((LeftMask & RightMask) != 0)
            {
                throw new ArgumentException("Left and right end markers must use different bits.");
            }
        }

        // Returns null for reports that carry no end-marker state (other report IDs or truncated reports).
        public RollerEnd? Decode(ReadOnlySpan<byte> report)
        {
            if (report.Length <= MarkerByteIndex || report[0] != ReportId)
            {
                return null;
            }
            byte markers = report[MarkerByteIndex];
            bool left = (markers & LeftMask) != 0;
            bool right = (markers & RightMask) != 0;
            // Both ends at once is physically impossible; treat it as no end so nothing is boosted.
            return left == right ? RollerEnd.None : left ? RollerEnd.Left : RollerEnd.Right;
        }

        public static byte? ReadMarkerByte(ReadOnlySpan<byte> report)
        {
            return report.Length > MarkerByteIndex && report[0] == ReportId ? report[MarkerByteIndex] : null;
        }
    }
}
