using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class SegmentFailureTests
    {
        private static StressTestSession Connect(FakeArduino arduino)
        {
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            return session;
        }

        [Test]
        public void FirmwareReportsFewerThanRequested_IsFlaggedReportShort()
        {
            var arduino = new FakeArduino { ReportOnly = 17 };
            var session = Connect(arduino);

            session.RunSegment(SegmentDirection.Up, 20, 0, 1.0, 1.0);
            session.Poll(1.1);
            var result = session.SubmitReading(2.7);

            Assert.That(result.Segment.ReportedCount, Is.EqualTo(17));
            Assert.That(result.Segment.ReportShort, Is.True);
            Assert.That(result.Segment.TimedOut, Is.False);
        }

        [Test]
        public void AllReported_IsNotFlagged()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);

            session.RunSegment(SegmentDirection.Up, 20, 0, 1.0, 1.0);
            session.Poll(1.1);

            Assert.That(session.CurrentSegment.ReportShort, Is.False);
        }

        [Test]
        public void NoDoneWithinDeadline_TimesOutAndAwaitsReading()
        {
            // action_ms=120、間隔 5 ms、20 筆：20 × (120 + 5) × 2 / 1000 + 2 = 7.0 秒
            var arduino = new FakeArduino { HangOnRun = true };
            var session = Connect(arduino);

            session.RunSegment(SegmentDirection.Up, 20, 5, 1.0, 10.0);
            session.Poll(16.9);
            Assert.That(session.State, Is.EqualTo(SessionState.RunningSegment));

            session.Poll(17.1);
            Assert.That(session.State, Is.EqualTo(SessionState.AwaitingReading));
            Assert.That(session.CurrentSegment.TimedOut, Is.True);
            Assert.That(session.CurrentSegment.ReportShort, Is.True);
            Assert.That(session.CurrentSegment.ReportedCount, Is.Null);
        }

        [Test]
        public void AfterTimeout_NextSegmentWaitsForLateDoneAndStaysClean()
        {
            var arduino = new FakeArduino { HangOnRun = true };
            var session = Connect(arduino);
            session.RunSegment(SegmentDirection.Up, 20, 5, 1.0, 10.0);
            session.Poll(100.0);
            session.SubmitReading(1.5);

            // 韌體還沒交出上一段的 DONE，不可開始下一段
            Assert.That(session.WaitingForLateDone, Is.True);
            Assert.That(session.RunSegment(SegmentDirection.Down, 20, 5, 1.5, 101.0), Is.False);

            // 上一段遲到的回報到達後被丟棄
            arduino.Emit("T,0,1000,121000");
            arduino.Emit("DONE,1");
            session.Poll(102.0);
            Assert.That(session.WaitingForLateDone, Is.False);

            arduino.HangOnRun = false;
            Assert.That(session.RunSegment(SegmentDirection.Down, 20, 5, 1.5, 103.0), Is.True);
            session.Poll(103.1);

            Assert.That(session.CurrentSegment.Direction, Is.EqualTo(SegmentDirection.Down));
            Assert.That(session.CurrentSegment.Timestamps.Count, Is.EqualTo(20));
            Assert.That(session.CurrentSegment.ReportedCount, Is.EqualTo(20));
        }

        [Test]
        public void AfterTimeout_FirmwareReboot_ClearsLateDoneWait()
        {
            var arduino = new FakeArduino { HangOnRun = true };
            var session = Connect(arduino);
            session.RunSegment(SegmentDirection.Up, 20, 5, 1.0, 10.0);
            session.Poll(100.0);
            session.SubmitReading(1.0);

            arduino.Boot();
            session.Poll(101.0);

            Assert.That(session.WaitingForLateDone, Is.False);
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
        }
    }
}
