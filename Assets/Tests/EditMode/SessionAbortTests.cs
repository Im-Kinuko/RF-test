using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class SessionAbortTests
    {
        private static StressTestSession Connect(FakeArduino arduino)
        {
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            return session;
        }

        [Test]
        public void AbortDuringRun_SendsAbortAndReturnsToReadyWithAbortedSegment()
        {
            var arduino = new FakeArduino { HangOnRun = true, AbortReportCount = 3 };
            var session = Connect(arduino);
            session.RunSegment(SegmentDirection.Up, 20, 0, 1.0, 1.0);
            session.Poll(1.1);

            Assert.That(session.Abort(), Is.True);
            session.Poll(1.2);

            Assert.That(arduino.Received, Has.Member("ABORT"));
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(session.CurrentSegment.Aborted, Is.True);
            Assert.That(session.CurrentSegment.ReportedCount, Is.EqualTo(3));
        }

        [Test]
        public void Abort_WhenNotRunning_SendsNothing()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);

            Assert.That(session.Abort(), Is.False);
            Assert.That(arduino.Received, Has.No.Member("ABORT"));
        }

        [Test]
        public void DiscardSegment_WhileAwaitingReading_ReturnsToReady()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);
            session.RunSegment(SegmentDirection.Up, 20, 0, 1.0, 1.0);
            session.Poll(1.1);

            session.DiscardSegment();

            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(session.SendSingle(SingleCommand.Stop), Is.True);
        }
    }
}
