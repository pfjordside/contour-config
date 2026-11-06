using Xunit;

namespace ContourConfig.Core.Tests.Unit
{
    public sealed class EndMarkerMapTests
    {
        private static readonly EndMarkerMap Map = new();

        [Theory]
        [InlineData(0x08, RollerEnd.Right)]
        [InlineData(0x04, RollerEnd.Left)]
        [InlineData(0x00, RollerEnd.None)]
        [InlineData(0x01, RollerEnd.None)]
        [InlineData(0x09, RollerEnd.Right)]
        [InlineData(0x0C, RollerEnd.None)]
        public void Decode_MarkerReport_ReturnsHeldEnd(byte markers, RollerEnd expected)
        {
            byte[] report = new byte[16];
            report[0] = EndMarkerMap.ReportId;
            report[EndMarkerMap.MarkerByteIndex] = markers;

            Assert.Equal(expected, Map.Decode(report));
        }

        [Fact]
        public void Decode_Heartbeat_IsIgnored()
        {
            byte[] heartbeat = [0x05, 0xD5, 0x11, 0x87, 0x00, 0x55, 0x00, 0x01, 0x04, 0x00, 0x01, 0x04, 0x02, 0x01, 0x04, 0x00];

            Assert.Null(Map.Decode(heartbeat));
        }

        [Fact]
        public void Decode_TruncatedReport_IsIgnored()
        {
            Assert.Null(Map.Decode([EndMarkerMap.ReportId, 0, 0]));
        }

        [Theory]
        [InlineData(0, 8)]
        [InlineData(4, 0)]
        [InlineData(0x0C, 0x08)]
        public void Validate_InvalidMasks_Throws(byte left, byte right)
        {
            Assert.Throws<ArgumentException>(() => new EndMarkerMap(left, right).Validate());
        }
    }
}
