using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundPlanTests
    {
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(90)]
        public void TwoSets_FourSegmentsOfNEach(int n)
        {
            var plan = RoundPlan.Create(n, sets: 2, baseSpeed: 2.0);

            Assert.That(plan.Segments.Select(s => s.Count), Is.EqualTo(new[] { n, n, n, n }));
        }

        [Test]
        public void Segments_AlternateUpAndDownBetweenOneAndThree()
        {
            var plan = RoundPlan.Create(20, sets: 2, baseSpeed: 1.0);

            Assert.That(plan.Segments.Select(s => s.Direction), Is.EqualTo(new[]
            {
                SegmentDirection.Up, SegmentDirection.Down, SegmentDirection.Up, SegmentDirection.Down
            }));
            Assert.That(plan.Segments.Select(s => s.ExpectedStartSpeed), Is.EqualTo(new[] { 1.0, 3.0, 1.0, 3.0 }).Within(1e-9));
            Assert.That(plan.Segments.Select(s => s.ExpectedEndSpeed), Is.EqualTo(new[] { 3.0, 1.0, 3.0, 1.0 }).Within(1e-9));
        }

        [Test]
        public void LargestN_StaysWithinPanelRange()
        {
            var plan = RoundPlan.Create(90, sets: 2, baseSpeed: 2.0);

            Assert.That(plan.Segments.Max(s => s.ExpectedEndSpeed), Is.EqualTo(11.0).Within(1e-9));
            Assert.That(plan.Segments.Last().ExpectedEndSpeed, Is.EqualTo(2.0).Within(1e-9));
        }
    }
}
