using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundPlanTests
    {
        [TestCase(40, 2)]
        [TestCase(80, 4)]
        [TestCase(120, 6)]
        public void TotalCount_SplitsIntoSegmentsOfTwenty(int total, int expectedSegments)
        {
            var plan = RoundPlan.Create(total, segmentSize: 20, baseSpeed: 1.0);

            Assert.That(plan.Segments, Has.Count.EqualTo(expectedSegments));
            Assert.That(plan.Segments.Sum(s => s.Count), Is.EqualTo(total));
        }

        [Test]
        public void Segments_AlternateUpAndDownBetweenOneAndThree()
        {
            var plan = RoundPlan.Create(80, segmentSize: 20, baseSpeed: 1.0);

            Assert.That(plan.Segments.Select(s => s.Direction), Is.EqualTo(new[]
            {
                SegmentDirection.Up, SegmentDirection.Down, SegmentDirection.Up, SegmentDirection.Down
            }));
            Assert.That(plan.Segments.Select(s => s.ExpectedStartSpeed), Is.EqualTo(new[] { 1.0, 3.0, 1.0, 3.0 }).Within(1e-9));
            Assert.That(plan.Segments.Select(s => s.ExpectedEndSpeed), Is.EqualTo(new[] { 3.0, 1.0, 3.0, 1.0 }).Within(1e-9));
        }

        [Test]
        public void SegmentSize_IsConfigurable_LastSegmentTakesRemainder()
        {
            var plan = RoundPlan.Create(40, segmentSize: 15, baseSpeed: 1.0);

            Assert.That(plan.Segments.Select(s => s.Count), Is.EqualTo(new[] { 15, 15, 10 }));
            Assert.That(plan.Segments.Last().ExpectedEndSpeed, Is.EqualTo(2.0).Within(1e-9));
        }
    }
}
