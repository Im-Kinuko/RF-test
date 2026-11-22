using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class SegmentReadingTests
    {
        private static StressTestSession RunUpSegmentFrom(double startSpeed, FakeArduino arduino = null)
        {
            arduino = arduino ?? new FakeArduino();
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            session.RunSegment(SegmentDirection.Up, 20, 5, startSpeed, 1.0);
            session.Poll(1.1);
            return session;
        }

        [TestCase(3.0, 0)]
        [TestCase(2.7, 3)]  // 漏 3 筆
        [TestCase(3.2, 2)]  // 多 2 筆
        [TestCase(1.0, 20)] // 全部沒收到
        public void SubmitReading_ReportsDropsFromPanelChange(double panelEnd, int expectedDrops)
        {
            var session = RunUpSegmentFrom(1.0);

            var result = session.SubmitReading(panelEnd);

            Assert.That(result.Drops, Is.EqualTo(expectedDrops));
            Assert.That(result.ActualEndSpeed, Is.EqualTo(panelEnd));
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
        }

        [Test]
        public void SubmitReading_UsesSegmentStartReadingNotTheory()
        {
            // 起點讀數 1.3（上一段已漏），本段 20 筆全收到應到 3.3
            var session = RunUpSegmentFrom(1.3);

            var result = session.SubmitReading(3.3);

            Assert.That(result.ExpectedChange, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(result.ActualChange, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(result.Drops, Is.EqualTo(0));
        }

        [Test]
        public void SubmitReading_WhenNotAwaiting_ReturnsNullAndKeepsState()
        {
            var arduino = new FakeArduino();
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);

            Assert.That(session.SubmitReading(1.0), Is.Null);
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
        }
    }
}
