using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class PanelSpeedInputTests
    {
        [TestCase("1", 1.0)]
        [TestCase("1.0", 1.0)]
        [TestCase("10", 1.0)]   // 面板沒有小數點時直接照打
        [TestCase("27", 2.7)]
        [TestCase(" 2.7 ", 2.7)]
        [TestCase("0.1", 0.1)]
        [TestCase("12.0", 12.0)]
        public void Accepts(string text, double expected)
        {
            Assert.That(PanelSpeedInput.TryParse(text, out var speed), Is.True);
            Assert.That(speed, Is.EqualTo(expected).Within(1e-9));
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("12.5")]
        [TestCase("130")]
        public void Rejects(string text)
        {
            Assert.That(PanelSpeedInput.TryParse(text, out _), Is.False);
        }
    }
}
