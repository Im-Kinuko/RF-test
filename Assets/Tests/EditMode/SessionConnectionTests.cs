using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class SessionConnectionTests
    {
        [Test]
        public void Handshake_WithMatchingEntry_BecomesReadyAndReportsFirmware()
        {
            var arduino = new FakeArduino("OPTO", "0.1.0", "press_ms=120");
            var session = new StressTestSession(arduino, EntryType.Opto);

            session.BeginHandshake(0.0);
            session.Poll(0.1);

            Assert.That(arduino.Received, Does.Contain("HELLO"));
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(session.Firmware.Entry, Is.EqualTo(EntryType.Opto));
            Assert.That(session.Firmware.Version, Is.EqualTo("0.1.0"));
            Assert.That(session.Firmware.Parameters["press_ms"], Is.EqualTo("120"));
        }

        [Test]
        public void Handshake_WithDifferentEntry_IsMismatchNotReady()
        {
            var arduino = new FakeArduino("RF", "0.1.0", "repeat=2;release_ms=250");
            var session = new StressTestSession(arduino, EntryType.Opto);

            session.BeginHandshake(0.0);
            session.Poll(0.1);

            Assert.That(session.State, Is.EqualTo(SessionState.EntryMismatch));
            Assert.That(session.Firmware.Entry, Is.EqualTo(EntryType.Rf));
        }

        [Test]
        public void Handshake_WithNoReply_TimesOutAfterFourSeconds()
        {
            var arduino = new FakeArduino { RespondToHello = false };
            var session = new StressTestSession(arduino, EntryType.Opto);

            session.BeginHandshake(10.0);
            session.Poll(13.9);
            Assert.That(session.State, Is.EqualTo(SessionState.Handshaking));

            session.Poll(14.1);
            Assert.That(session.State, Is.EqualTo(SessionState.HandshakeTimeout));
        }

        [Test]
        public void ReadyFromBootReset_DuringHandshake_IsAccepted()
        {
            // Nano 開埠時會重設，HELLO 可能在 bootloader 期間遺失，只收到開機的 READY
            var arduino = new FakeArduino { RespondToHello = false };
            var session = new StressTestSession(arduino, EntryType.Opto);

            session.BeginHandshake(0.0);
            arduino.Boot();
            session.Poll(2.0);

            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
        }
    }
}
