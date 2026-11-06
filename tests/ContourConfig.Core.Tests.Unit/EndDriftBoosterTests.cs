using Xunit;

namespace ContourConfig.Core.Tests.Unit
{
    public sealed class EndDriftBoosterTests
    {
        private static EndDriftBooster Create(double speed = 1000, int delay = 300) =>
            new(new DriftOptions(speed, speed, delay, 15000));

        private static int Run(EndDriftBooster booster, double from, double to, double step = 10)
        {
            int total = 0;
            for (double now = from; now <= to; now += step)
            {
                total += booster.Tick(now);
            }
            return total;
        }

        [Fact]
        public void Tick_EndHeldShorterThanDelay_DoesNotMove()
        {
            EndDriftBooster booster = Create();
            booster.Update(RollerEnd.Right, 0);

            Assert.Equal(0, Run(booster, 0, 290));
        }

        [Theory]
        [InlineData(RollerEnd.Right, 1000)]
        [InlineData(RollerEnd.Left, -1000)]
        public void Tick_EndHeldPastDelay_MovesAtConfiguredSpeedTowardThatEnd(RollerEnd end, int expected)
        {
            EndDriftBooster booster = Create();
            booster.Update(end, 0);

            int moved = Run(booster, 0, 1300);

            Assert.InRange(moved, expected - 2 - Math.Abs(expected) / 100, expected + 2 + Math.Abs(expected) / 100);
        }

        [Fact]
        public void Tick_StalledTimer_LimitsCatchUpMovement()
        {
            EndDriftBooster booster = Create(delay: 0);
            booster.Update(RollerEnd.Right, 0);

            int moved = booster.Tick(5000);

            Assert.Equal(50, moved);
        }

        [Fact]
        public void Tick_EndReleased_StopsImmediately()
        {
            EndDriftBooster booster = Create(delay: 0);
            booster.Update(RollerEnd.Right, 0);
            Run(booster, 0, 500);

            booster.Update(RollerEnd.None, 500);

            Assert.Equal(0, Run(booster, 510, 1000));
        }

        [Fact]
        public void Tick_EndHeldBeyondSafetyLimit_StopsBoosting()
        {
            EndDriftBooster booster = new(new DriftOptions(1000, 1000, 0, 1000));
            booster.Update(RollerEnd.Right, 0);
            Run(booster, 0, 1000);

            Assert.Equal(0, Run(booster, 1010, 2000));
        }

        [Fact]
        public void Update_SwitchingEnds_RestartsActivationDelay()
        {
            EndDriftBooster booster = Create();
            booster.Update(RollerEnd.Right, 0);
            Run(booster, 0, 1000);

            booster.Update(RollerEnd.Left, 1000);

            Assert.Equal(0, Run(booster, 1000, 1290));
        }
    }
}
