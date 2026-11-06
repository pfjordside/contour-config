using Xunit;

namespace ContourConfig.Core.Tests.Unit
{
    public sealed class MarkerLearnerTests
    {
        [Fact]
        public void Finish_EndHeldWithShortNoisePulses_ReturnsLongestHeldBit()
        {
            MarkerLearner learner = new();
            learner.Observe(0x00, 0);
            learner.Observe(0x01, 100);
            learner.Observe(0x00, 180);
            learner.Observe(0x04, 500);
            learner.Observe(0x05, 1500);
            learner.Observe(0x04, 1560);

            Assert.Equal((byte)0x04, learner.Finish(3500, 1500));
        }

        [Fact]
        public void Finish_NothingHeldLongEnough_ReturnsNull()
        {
            MarkerLearner learner = new();
            learner.Observe(0x00, 0);
            learner.Observe(0x08, 100);
            learner.Observe(0x00, 400);

            Assert.Null(learner.Finish(4000, 1500));
        }
    }
}
