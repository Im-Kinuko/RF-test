using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundCompletionTests
    {
        [Test]
        public void AfterLastSegment_SendsStopThenCompletes()
        {
            var h = new RoundHarness(gapMs: 10);
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);
            h.CompleteSegment(1.0);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.Stopping));
            Assert.That(h.Sent("ONE,STOP"), Is.EqualTo(1));

            h.AdvanceUntil(RoundState.Completed, maxSeconds: 1);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.Completed));
        }

        [Test]
        public void Summary_TotalsDropsAndGapsAcrossSegments()
        {
            var h = new RoundHarness(gapMs: 5, totalCount: 120, arduino: new FakeArduino { GapJitterMicros = 8 });
            h.StartAndWarmUp();
            // 預期 3.0 / 1.0 / 3.0 / 1.0 / 3.0 / 1.0，每段以實際讀數為起點
            h.CompleteSegment(2.7); // +1.7 → 漏 3
            h.CompleteSegment(0.7); // −2.0 → 0
            h.CompleteSegment(2.6); // +1.9 → 漏 1
            h.CompleteSegment(0.6); // −2.0 → 0
            h.CompleteSegment(2.6); // +2.0 → 0
            h.CompleteSegment(0.8); // −1.8 → 漏 2
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 1);

            var summary = h.Round.Summary;

            Assert.That(summary.SegmentCount, Is.EqualTo(6));
            Assert.That(summary.TotalSent, Is.EqualTo(120));
            Assert.That(summary.TotalDrops, Is.EqualTo(6));
            Assert.That(summary.WarmupReading, Is.EqualTo(1.0));
            Assert.That(summary.EndReading, Is.EqualTo(0.8));
            Assert.That(summary.AnyReportShort, Is.False);
            Assert.That(summary.MeanGapMicros, Is.EqualTo(5008).Within(1e-9));
            Assert.That(summary.MaxGapMicros, Is.EqualTo(5008));
        }

        [Test]
        public void Summary_IsNullUntilCompleted()
        {
            var h = new RoundHarness();
            h.StartAndWarmUp();

            Assert.That(h.Round.Summary, Is.Null);
        }
    }
}
