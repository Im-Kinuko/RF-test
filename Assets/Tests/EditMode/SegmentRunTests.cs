using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class SegmentRunTests
    {
        private static StressTestSession Connect(FakeArduino arduino)
        {
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            return session;
        }

        [Test]
        public void RunSegment_SendsRunAndWaitsForReadingAfterDone()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);

            Assert.That(session.RunSegment(SegmentDirection.Up, 20, 5, 1.0, 1.0), Is.True);
            Assert.That(arduino.Received, Has.Member("RUN,UP,20,5"));

            session.Poll(1.1);

            Assert.That(session.State, Is.EqualTo(SessionState.AwaitingReading));
            Assert.That(session.CurrentSegment.ReportedCount, Is.EqualTo(20));
            Assert.That(session.CurrentSegment.Timestamps.Count, Is.EqualTo(20));
            Assert.That(session.CurrentSegment.ExpectedEndSpeed, Is.EqualTo(3.0).Within(1e-9));
        }

        [Test]
        public void RunSegment_ComputesActualGapsFromTimestamps()
        {
            // 間隔 5 ms，假韌體每筆多 8 µs：實際間隔應為 5008 µs
            var arduino = new FakeArduino { GapJitterMicros = 8 };
            var session = Connect(arduino);

            session.RunSegment(SegmentDirection.Down, 20, 5, 3.0, 1.0);
            session.Poll(1.1);

            Assert.That(session.CurrentSegment.ExpectedEndSpeed, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(session.CurrentSegment.MeanGapMicros, Is.EqualTo(5008).Within(1e-9));
            Assert.That(session.CurrentSegment.MaxGapMicros, Is.EqualTo(5008));
        }

        [Test]
        public void RunSegment_AcrossMicrosWraparound_KeepsGapsCorrect()
        {
            // 第 0 筆結束於 2^32 − 2000 µs，休息期間計數器溢位，第 1 筆從 3008 µs 開始
            var arduino = new FakeArduino { StartMicros = (1L << 32) - 120_000 - 2_000, GapJitterMicros = 8 };
            var session = Connect(arduino);

            session.RunSegment(SegmentDirection.Up, 20, 5, 1.0, 1.0);
            session.Poll(1.1);

            Assert.That(session.CurrentSegment.MeanGapMicros, Is.EqualTo(5008).Within(1e-9));
            Assert.That(session.CurrentSegment.MaxGapMicros, Is.EqualTo(5008));
        }

        [Test]
        public void RunSegment_WhenNotReady_IsRefused()
        {
            var arduino = new FakeArduino("RF");
            var session = Connect(arduino);

            Assert.That(session.RunSegment(SegmentDirection.Up, 20, 0, 1.0, 1.0), Is.False);
            Assert.That(arduino.Received, Has.No.Member("RUN,UP,20,0"));
        }
    }
}
