using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundPressCountTests
    {
        [TestCase(40, 20, 20, 20)]
        [TestCase(80, 20, 40, 40)]
        [TestCase(120, 20, 60, 60)]
        [TestCase(40, 15, 25, 15)] // 15 加、15 減、10 加
        public void Plan_CountsUpAndDownPresses(int total, int segmentSize, int ups, int downs)
        {
            var plan = RoundPlan.Create(total, segmentSize, baseSpeed: 1.0);

            Assert.That(plan.UpCount, Is.EqualTo(ups));
            Assert.That(plan.DownCount, Is.EqualTo(downs));
        }

        [Test]
        public void CompletedRound_ReportsPlannedAndSentPressesByDirection()
        {
            var h = new RoundHarness(gapMs: 5, totalCount: 80);
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);
            h.CompleteSegment(1.0);
            h.CompleteSegment(3.0);
            h.CompleteSegment(1.0);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 1);

            var s = h.Round.Summary;

            Assert.That((s.UpPlanned, s.DownPlanned), Is.EqualTo((40, 40)));
            Assert.That((s.UpSent, s.DownSent), Is.EqualTo((40, 40)));
            Assert.That((s.WarmupUpSent, s.WarmupDownSent), Is.EqualTo((0, 0)));
        }

        [Test]
        public void SentCounts_UseArduinoReportedCounts()
        {
            // 韌體每段只回報 17 筆
            var h = new RoundHarness(gapMs: 0, arduino: new FakeArduino { ReportOnly = 17 });
            h.StartAndWarmUp();
            h.CompleteSegment(2.7);
            h.CompleteSegment(1.0);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 1);

            var s = h.Round.Summary;

            Assert.That((s.UpPlanned, s.DownPlanned), Is.EqualTo((20, 20)));
            Assert.That((s.UpSent, s.DownSent), Is.EqualTo((17, 17)));
        }

        [Test]
        public void WarmupAndRewarmPresses_AreCountedSeparately()
        {
            var h = new RoundHarness(gapMs: 5, bootSpeed: 0.1);
            h.Start();
            h.AdvanceUntil(RoundState.AwaitingWarmupReading);
            h.Round.EnterReading(1.2);
            h.Round.Confirm(h.Now);
            h.Round.Rewarm(h.Now);               // 補 2 次減速
            h.AdvanceUntil(RoundState.AwaitingWarmupReading);

            Assert.That(h.Round.WarmupUpSent, Is.EqualTo(9));
            Assert.That(h.Round.WarmupDownSent, Is.EqualTo(2));
            Assert.That(h.Round.UpSent, Is.EqualTo(0)); // 預熱不計入計分段
        }

        [Test]
        public void RejectedWarmupPress_IsNotCounted()
        {
            var h = new RoundHarness(gapMs: 5, bootSpeed: 0.1);
            h.Start();
            h.AdvanceUntil(RoundState.WarmingUp);
            h.Round.Tick(h.Now + 0.1);          // 第 1 次加速已 ACK
            h.Arduino.ErrorReply = "BUSY";       // 之後的單發都被拒絕
            h.AdvanceUntil(RoundState.Failed, maxSeconds: 5);

            Assert.That(h.Round.WarmupUpSent, Is.EqualTo(1));
        }

        [Test]
        public void DuringRound_SentCountsGrowSegmentBySegment()
        {
            var h = new RoundHarness(gapMs: 5);
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);

            Assert.That((h.Round.UpSent, h.Round.DownSent), Is.EqualTo((20, 0)));
        }
    }
}
