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
        public void DefaultConfig_BootsAtOneAndWarmsUpTenPressesToTwo()
        {
            // XTERRA DT168：開機 1.0 km/h，也是最低速度；預熱到 2.0 再計分，各段 2.0↔4.0
            var config = new RoundConfig(gapMs: 5, totalCount: 40);
            var plan = RoundPlan.Create(config.TotalCount, config.SegmentSize, config.BaseSpeed);

            Assert.That(config.BootSpeed, Is.EqualTo(1.0));
            Assert.That(config.BaseSpeed, Is.EqualTo(2.0));
            Assert.That(config.WarmupCount, Is.EqualTo(10));
            Assert.That(plan.Segments[0].ExpectedEndSpeed, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(plan.Segments[1].ExpectedEndSpeed, Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void WarmUpFromOneToTwo_SendsTenUpsThenAsksForTwo()
        {
            var h = new RoundHarness(baseSpeed: 2.0, bootSpeed: 1.0);

            h.Start();
            h.AdvanceUntil(RoundState.AwaitingWarmupReading, maxSeconds: 30);

            Assert.That(h.Sent("ONE,UP"), Is.EqualTo(10));
            Assert.That(h.Round.ExpectedPanelSpeed, Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void BootSpeedEqualsBase_SkipsWarmupPressesAndAsksForReading()
        {
            var h = new RoundHarness(baseSpeed: 1.0, bootSpeed: 1.0);

            h.Start();
            h.AdvanceUntil(RoundState.AwaitingWarmupReading, maxSeconds: 10);

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
