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
        public void DefaultConfig_BootsAtOneAndWarmsUpElevenPressesToTwo()
        {
            // XTERRA DT168：開機 1.0 km/h，也是最低速度；到 2.0 是 10 格，實機會少吃 1 下，所以送 11 下
            var config = new RoundConfig(gapMs: 5, n: 30);
            var plan = RoundPlan.Create(config.N, config.Sets, config.BaseSpeed);

            Assert.That(config.BootSpeed, Is.EqualTo(1.0));
            Assert.That(config.BaseSpeed, Is.EqualTo(2.0));
            Assert.That(config.IgnoredWarmupPresses, Is.EqualTo(1));
            Assert.That(config.WarmupCount, Is.EqualTo(11));
            Assert.That((config.Sets, config.TotalCount), Is.EqualTo((2, 120)));
            Assert.That(plan.Segments[0].ExpectedEndSpeed, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(plan.Segments[1].ExpectedEndSpeed, Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void DefaultRound_SendsElevenWarmupUps()
        {
            var arduino = new FakeArduino();
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            var round = new TestRound(session, new RoundConfig(gapMs: 5, n: 30), "R1");

            round.Start(1.0);
            for (var now = 1.1; now < 30 && round.State != RoundState.AwaitingWarmupReading; now += 0.1) round.Tick(now);

            Assert.That(arduino.Received.FindAll(l => l == "ONE,UP"), Has.Count.EqualTo(11));
            Assert.That(round.WarmupUpSent, Is.EqualTo(11));
            Assert.That(round.ExpectedPanelSpeed, Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void SecondRoundOnSameSession_StillWaitsForEachAck()
        {
            var first = new RoundHarness(bootSpeed: 0.1);
            first.StartAndWarmUp();
            first.CompleteSegment(3.0);
            first.CompleteSegment(1.0);
            first.AdvanceUntil(RoundState.Completed, maxSeconds: 1);
            var second = new TestRound(first.Session, first.Round.Config, "R2");

            first.Arduino.ErrorReply = "BUSY"; // 沒有任何 ACK
            second.Start(100.0);
            second.Tick(100.1);

            Assert.That(second.State, Is.EqualTo(RoundState.Failed));
        }

        [Test]
        public void OldFirmwareWithoutMaxRun_RefusesToStartBeyondThirtyTwo()
        {
            var arduino = new FakeArduino(parameters: "press_ms=120;action_ms=120");
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            var round = new TestRound(session, new RoundConfig(gapMs: 5, n: 60) { Mode = SendMode.Burst }, "R1");

            round.Start(1.0);

            Assert.That(session.Firmware.MaxRunCount, Is.EqualTo(32));
            Assert.That(round.State, Is.EqualTo(RoundState.Failed));
            Assert.That(round.FailureReason, Does.Contain("燒錄"));
            Assert.That(arduino.Received, Does.Not.Contain("ONE,START"));
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
