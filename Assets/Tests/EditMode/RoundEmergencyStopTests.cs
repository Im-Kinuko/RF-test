using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundEmergencyStopTests
    {
        [Test]
        public void DuringSegment_AbortsThenStopsAndEndsVoided()
        {
            var h = new RoundHarness(gapMs: 0, totalCount: 40, arduino: new FakeArduino { HangOnRun = true });
            h.StartAndWarmUp();
            h.Round.Tick(h.Now + 0.1);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.RunningSegment));

            h.Round.EmergencyStop(h.Now);
            Assert.That(h.Arduino.Received.Last(), Is.EqualTo("ABORT"));
            Assert.That(h.Round.State, Is.EqualTo(RoundState.Aborting));

            h.AdvanceUntil(RoundState.Completed, maxSeconds: 1);

            var afterAbort = h.Arduino.Received.SkipWhile(l => l != "ABORT").ToList();
            Assert.That(afterAbort, Is.EqualTo(new[] { "ABORT", "ONE,STOP" }));
            Assert.That(h.Round.State, Is.EqualTo(RoundState.Completed));
            Assert.That(h.Round.IsVoided, Is.True);
            Assert.That(h.Round.VoidReason, Is.EqualTo("緊急停止"));
            Assert.That(h.Round.Summary, Is.Not.Null);
        }

        [Test]
        public void DuringWarmup_StopsWithoutAbort()
        {
            var h = new RoundHarness(bootSpeed: 0.1);
            h.Start();
            h.AdvanceUntil(RoundState.WarmingUp);

            h.Round.EmergencyStop(h.Now);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 5);

            Assert.That(h.Arduino.Received, Has.No.Member("ABORT"));
            Assert.That(h.Sent("ONE,STOP"), Is.EqualTo(1));
            Assert.That(h.Round.IsVoided, Is.True);
        }

        [Test]
        public void WhileAwaitingReading_DiscardsSegmentAndStops()
        {
            var h = new RoundHarness(gapMs: 0, totalCount: 40);
            h.StartAndWarmUp();
            h.AdvanceUntil(RoundState.AwaitingSegmentReading);

            h.Round.EmergencyStop(h.Now);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 5);

            Assert.That(h.Sent("ONE,STOP"), Is.EqualTo(1));
            Assert.That(h.Round.Results, Is.Empty);
            Assert.That(h.Session.State, Is.EqualTo(SessionState.Ready));
        }

        [Test]
        public void WhileStartAckPending_StopWaitsForBothAcks()
        {
            var h = new RoundHarness();
            h.Start(); // ONE,START 已送出、尚未 Poll 到 ACK

            h.Round.EmergencyStop(h.Now);
            h.Round.Tick(h.Now + 0.1);

            Assert.That(h.Sent("ONE,STOP"), Is.EqualTo(1));
            Assert.That(h.Round.State, Is.EqualTo(RoundState.Completed));
            Assert.That(h.Round.IsVoided, Is.True);
        }

        [Test]
        public void AfterSegmentTimeout_AbortsLateRunBeforeStopping()
        {
            // 第一段逾時，韌體仍在跑；確認讀數後本輪在等遲到的 DONE
            var h = new RoundHarness(gapMs: 0, totalCount: 40, arduino: new FakeArduino { HangOnRun = true });
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.WaitingForLateDone));

            h.Round.EmergencyStop(h.Now);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 3);

            var afterAbort = h.Arduino.Received.SkipWhile(l => l != "ABORT").ToList();
            Assert.That(afterAbort, Is.EqualTo(new[] { "ABORT", "ONE,STOP" }));
            Assert.That(h.Round.State, Is.EqualTo(RoundState.Completed));
            Assert.That(h.Session.WaitingForLateDone, Is.False);
        }

        [Test]
        public void FirmwareIgnoresAbort_FailsAndTellsResearcherToUsePanelStop()
        {
            var h = new RoundHarness(gapMs: 0, totalCount: 40,
                arduino: new FakeArduino { HangOnRun = true, RespondToAbort = false });
            h.StartAndWarmUp();

            h.Round.EmergencyStop(h.Now);
            h.AdvanceUntil(RoundState.Failed, maxSeconds: 3);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.Failed));
            Assert.That(h.Round.FailureReason, Does.Contain("停止鍵"));
        }

        [Test]
        public void WhenFinished_EmergencyStopDoesNothing()
        {
            var h = new RoundHarness();
            h.Round.EmergencyStop(h.Now);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.Idle));
            Assert.That(h.Arduino.Received, Has.No.Member("ONE,STOP"));
        }
    }
}
