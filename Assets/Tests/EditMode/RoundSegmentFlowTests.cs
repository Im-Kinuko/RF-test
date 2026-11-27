using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundSegmentFlowTests
    {
        [Test]
        public void SegmentDone_AsksForReadingWithExpectedEndSpeed()
        {
            var h = new RoundHarness(gapMs: 1);
            h.StartAndWarmUp();

            h.AdvanceUntil(RoundState.AwaitingSegmentReading);

            Assert.That(h.Round.SegmentIndex, Is.EqualTo(0));
            Assert.That(h.Round.ExpectedPanelSpeed, Is.EqualTo(3.0).Within(1e-9));
        }

        [Test]
        public void EnteredReading_ShowsDropsBeforeConfirm_UndoClearsIt()
        {
            var h = new RoundHarness(gapMs: 1);
            h.StartAndWarmUp();
            h.AdvanceUntil(RoundState.AwaitingSegmentReading);

            h.Round.EnterReading(2.7);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.ConfirmingSegmentReading));
            Assert.That(h.Round.PendingResult.Drops, Is.EqualTo(3));

            h.Round.Undo();
            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingSegmentReading));
            Assert.That(h.Round.PendingResult, Is.Null);
            Assert.That(h.Round.Results, Is.Empty);
        }

        [Test]
        public void Confirm_StartsNextSegmentFromActualReading()
        {
            var h = new RoundHarness(gapMs: 1);
            h.StartAndWarmUp();

            h.CompleteSegment(2.7);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.RunningSegment));
            Assert.That(h.Round.SegmentIndex, Is.EqualTo(1));
            Assert.That(h.Arduino.Received.Last(), Is.EqualTo("RUN,DOWN,20,1"));
            Assert.That(h.Session.CurrentSegment.StartSpeed, Is.EqualTo(2.7));
            Assert.That(h.Round.Results.Single().Drops, Is.EqualTo(3));
        }

        [Test]
        public void TimedOutSegment_NextSegmentWaitsForLateDoneInsteadOfFailing()
        {
            // 第一段卡住不回報（RUN 只回 ACK），逾時後輸入讀數並確認
            var h = new RoundHarness(gapMs: 0, arduino: new FakeArduino { HangOnRun = true });
            h.StartAndWarmUp();
            h.CompleteSegment(3.0); // 逾時後輸入讀數並確認

            Assert.That(h.Round.State, Is.EqualTo(RoundState.WaitingForLateDone));

            h.Arduino.HangOnRun = false;
            h.Arduino.Emit("DONE,20");
            h.AdvanceUntil(RoundState.RunningSegment, maxSeconds: 1);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.RunningSegment));
            Assert.That(h.Round.SegmentIndex, Is.EqualTo(1));
            Assert.That(h.Session.CurrentSegment.StartSpeed, Is.EqualTo(3.0));
        }

        [Test]
        public void TimedOutSegment_FailsOnlyAfterBoundedWait()
        {
            // 第一段卡住不回報（RUN 只回 ACK），逾時後輸入讀數並確認
            var h = new RoundHarness(gapMs: 0, arduino: new FakeArduino { HangOnRun = true });
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);

            h.AdvanceUntil(RoundState.Failed, maxSeconds: TestRound.LateDoneWaitSeconds + 1);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.Failed));
        }

        [Test]
        public void DownSegmentEndingBelowTreadmillMinimum_IsFlaggedPossiblyClamped()
        {
            // 最低速度 1.0：第一段漏 3 筆停在 2.7，第二段減 2.0 理論終點 0.7，會撞到底
            var h = new RoundHarness(gapMs: 1);
            h.StartAndWarmUp();
            h.CompleteSegment(2.7);

            var second = h.CompleteSegment(1.0);

            Assert.That(second.PossiblyClamped, Is.True);
        }

        [Test]
        public void DownSegmentEndingExactlyAtMinimum_IsNotClamped()
        {
            var h = new RoundHarness(gapMs: 1);
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);

            Assert.That(h.CompleteSegment(1.0).PossiblyClamped, Is.False);
        }

        [Test]
        public void ExpectedEndOutsidePanelRange_IsFlaggedPossiblyClamped()
        {
            var h = new RoundHarness(gapMs: 1);
            h.StartAndWarmUp();
            h.CompleteSegment(1.5); // 第一段只上升 0.5

            // 第二段從 1.5 減 2.0，理論終點 -0.5，低於最低速度 1.0
            var second = h.CompleteSegment(1.0);

            Assert.That(h.Round.Results[0].PossiblyClamped, Is.False);
            Assert.That(second.PossiblyClamped, Is.True);
        }
    }
}
