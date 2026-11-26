using Xunit;

namespace ContourConfig.Core.Tests.Unit
{
    public sealed class BoostControllerTests
    {
        private static BoostController Create()
        {
            BoostController controller = new();
            controller.SetOptions(new DriftOptions(1000, 1000, 0), 0);
            return controller;
        }

        [Theory]
        [InlineData(BoostSuspension.SessionLocked)]
        [InlineData(BoostSuspension.SessionDisconnected)]
        [InlineData(BoostSuspension.SystemSuspended)]
        public void Suspension_PausesAndRestoresEnabledBoostOnlyAfterFreshEndSignal(BoostSuspension reason)
        {
            BoostController controller = Create();
            controller.SetEnabled(true, 0);
            controller.Update(RollerEnd.Right, 0);
            Assert.True(controller.Tick(10) > 0);

            controller.SetSuspended(reason, true, 10);
            controller.Update(RollerEnd.Right, 20);
            Assert.Equal(0, controller.Tick(30));

            controller.SetSuspended(reason, false, 40);
            Assert.True(controller.Enabled);
            Assert.Equal(0, controller.Tick(50));
            controller.Update(RollerEnd.Right, 50);
            Assert.True(controller.Tick(60) > 0);
        }

        [Fact]
        public void Resume_WaitsForAllSuspensionsToEnd()
        {
            BoostController controller = Create();
            controller.SetEnabled(true, 0);
            controller.SetSuspended(BoostSuspension.SessionLocked, true, 10);
            controller.SetSuspended(BoostSuspension.SystemSuspended, true, 20);

            controller.SetSuspended(BoostSuspension.SystemSuspended, false, 30);
            controller.Update(RollerEnd.Right, 30);
            Assert.Equal(0, controller.Tick(40));

            controller.SetSuspended(BoostSuspension.SessionLocked, false, 50);
            controller.Update(RollerEnd.Right, 50);
            Assert.True(controller.Tick(60) > 0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Resume_DoesNotEnableBoostThatWasOffOrStoppedWhilePaused(bool stopWhilePaused)
        {
            BoostController controller = Create();
            controller.SetEnabled(stopWhilePaused, 0);
            controller.SetSuspended(BoostSuspension.SessionLocked, true, 10);
            if (stopWhilePaused)
            {
                controller.SetEnabled(false, 20);
            }

            controller.SetSuspended(BoostSuspension.SessionLocked, false, 30);
            controller.Update(RollerEnd.Right, 30);

            Assert.False(controller.Enabled);
            Assert.Equal(0, controller.Tick(40));
        }
    }
}
