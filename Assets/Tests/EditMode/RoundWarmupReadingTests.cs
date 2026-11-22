using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundWarmupReadingTests
    {
        private static RoundHarness AtWarmupReading()
        {
            var h = new RoundHarness(gapMs: 5, totalCount: 40);
            h.Start();
            h.AdvanceUntil(RoundState.AwaitingWarmupReading);
            return h;
        }

        [Test]
        public void ReadingMatchesBase_ConfirmStartsFirstUpSegment()
        {
            var h = AtWarmupReading();

            h.Round.EnterReading(1.0);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.ConfirmingWarmupReading));

            h.Round.Confirm(h.Now);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.RunningSegment));
            Assert.That(h.Arduino.Received.Last(), Is.EqualTo("RUN,UP,20,5"));
            Assert.That(h.Session.CurrentSegment.StartSpeed, Is.EqualTo(1.0));
        }

        [Test]
        public void Undo_BeforeConfirm_ReturnsToReadingInput()
        {
            var h = AtWarmupReading();

            h.Round.EnterReading(0.7);
            h.Round.Undo();

            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingWarmupReading));
            Assert.That(h.Arduino.Received, Has.No.Member("RUN,UP,20,5"));
        }

        [Test]
        public void ReadingBelowBase_OffersChoice_RewarmSendsMissingUps()
        {
            var h = AtWarmupReading();

            h.Round.EnterReading(0.8);
            h.Round.Confirm(h.Now);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.WarmupMismatch));
            Assert.That(h.Round.WarmupReading, Is.EqualTo(0.8));

            h.Round.Rewarm(h.Now);
            h.AdvanceUntil(RoundState.AwaitingWarmupReading);

            Assert.That(h.Sent("ONE,UP"), Is.EqualTo(9 + 2));
            Assert.That(h.Sent("ONE,DOWN"), Is.EqualTo(0));
        }

        [Test]
        public void ReadingAboveBase_RewarmSendsDowns()
        {
            var h = AtWarmupReading();

            h.Round.EnterReading(1.2);
            h.Round.Confirm(h.Now);
            h.Round.Rewarm(h.Now);
            h.AdvanceUntil(RoundState.AwaitingWarmupReading);

            Assert.That(h.Sent("ONE,DOWN"), Is.EqualTo(2));
        }

        [Test]
        public void ContinueFromReading_UsesActualSpeedAsFirstSegmentStart()
        {
            var h = AtWarmupReading();

            h.Round.EnterReading(0.8);
            h.Round.Confirm(h.Now);
            h.Round.ContinueFromReading(h.Now);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.RunningSegment));
            Assert.That(h.Session.CurrentSegment.StartSpeed, Is.EqualTo(0.8));
        }
    }
}
