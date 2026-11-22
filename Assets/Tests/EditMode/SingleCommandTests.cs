using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class SingleCommandTests
    {
        private static StressTestSession Connect(FakeArduino arduino, EntryType expected)
        {
            var session = new StressTestSession(arduino, expected);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            return session;
        }

        [TestCase(SingleCommand.Start, "ONE,START")]
        [TestCase(SingleCommand.Up, "ONE,UP")]
        [TestCase(SingleCommand.Down, "ONE,DOWN")]
        [TestCase(SingleCommand.Stop, "ONE,STOP")]
        public void SendSingle_WhenReady_SendsCommandAndRecordsAck(SingleCommand command, string expectedLine)
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino, EntryType.Opto);

            Assert.That(session.SendSingle(command), Is.True);
            session.Poll(0.2);

            Assert.That(arduino.Received, Has.Member(expectedLine));
            Assert.That(session.LastAck, Is.EqualTo(expectedLine));
            Assert.That(session.LastError, Is.Null);
        }

        [Test]
        public void SendSingle_WhenEntryMismatch_IsRefusedAndSendsNothing()
        {
            var arduino = new FakeArduino("RF");
            var session = Connect(arduino, EntryType.Opto);

            Assert.That(session.SendSingle(SingleCommand.Up), Is.False);
            Assert.That(arduino.Received, Is.EqualTo(new[] { "HELLO" }));
        }

        [Test]
        public void SendSingle_WhenNotConnected_IsRefused()
        {
            var arduino = new FakeArduino();
            var session = new StressTestSession(arduino, EntryType.Opto);

            Assert.That(session.SendSingle(SingleCommand.Up), Is.False);
            Assert.That(arduino.Received, Is.Empty);
        }

        [Test]
        public void ErrReply_IsExposedAsLastError()
        {
            var arduino = new FakeArduino { ErrorReply = "BUSY" };
            var session = Connect(arduino, EntryType.Opto);

            session.SendSingle(SingleCommand.Up);
            session.Poll(0.2);

            Assert.That(session.LastError, Is.EqualTo("BUSY"));
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
        }
    }
}
