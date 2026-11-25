using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundStartTests
    {
        [Test]
        public void Start_SendsStartThenCountsDownFiveSeconds()
        {
            var h = new RoundHarness();

            h.Start();
            h.Round.Tick(1.1);

            Assert.That(h.Sent("ONE,START"), Is.EqualTo(1));
            Assert.That(h.Round.State, Is.EqualTo(RoundState.Starting));
            Assert.That(h.Round.CountdownRemaining, Is.EqualTo(5.0).Within(0.11));

            h.Round.Tick(5.9);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.Starting));
            Assert.That(h.Sent("ONE,UP"), Is.EqualTo(0));
        }

        [Test]
        public void BootSpeedEqualsBase_SkipsWarmupPressesAndAsksForReading()
        {
            // 跑步機開機就是 1.0 km/h，與計分起點相同：不需預熱
            var h = new RoundHarness();

            h.Start();
            h.AdvanceUntil(RoundState.AwaitingWarmupReading, maxSeconds: 10);

            Assert.That(h.Round.Config.BootSpeed, Is.EqualTo(1.0));
            Assert.That(h.Round.Config.WarmupCount, Is.EqualTo(0));
            Assert.That(h.Sent("ONE,UP"), Is.EqualTo(0));
            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingWarmupReading));
            Assert.That(h.Round.ExpectedPanelSpeed, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void WarmUp_SendsNineUpsOneSecondApartThenAsksForReading()
        {
            var h = new RoundHarness(bootSpeed: 0.1);

            h.Start();
            h.AdvanceUntil(RoundState.WarmingUp);
            var warmupStarted = h.Now;
            h.AdvanceUntil(RoundState.AwaitingWarmupReading);

            Assert.That(h.Sent("ONE,UP"), Is.EqualTo(9));
            Assert.That(h.Now - warmupStarted, Is.EqualTo(8.0).Within(0.25)); // 9 筆，間隔 1 秒
            Assert.That(h.Round.ExpectedPanelSpeed, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void WarmUp_WaitsForAckBeforeNextPress()
        {
            var h = new RoundHarness(bootSpeed: 0.1);
            h.Start();
            h.AdvanceUntil(RoundState.WarmingUp);

            h.Arduino.ErrorReply = "BUSY"; // 之後的 ONE 都回 ERR，沒有 ACK
            h.AdvanceUntil(RoundState.Failed, maxSeconds: 10);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.Failed));
            Assert.That(h.Round.FailureReason, Does.Contain("BUSY"));
        }
    }
}
