using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class ConditionMatrixTests
    {
        private static ConditionMatrix NewMatrix(int repetitions = 3) => new ConditionMatrix(repetitions);

        private static void Complete(ConditionMatrix m, EntryType entry, int gap, int n) =>
            m.Record(entry, gap, n, voided: false);

        [Test]
        public void Empty_FirstScheduledIsOptoGapZeroThirty()
        {
            var next = NewMatrix().Next();

            Assert.That(next.Entry, Is.EqualTo(EntryType.Opto));
            Assert.That(next.GapMs, Is.EqualTo(0));
            Assert.That(next.N, Is.EqualTo(30));
            Assert.That(next.Repetition, Is.EqualTo(1));
        }

        [Test]
        public void WithinACondition_OtherEntryFollows()
        {
            var m = NewMatrix();
            Complete(m, EntryType.Opto, 0, 30);

            var next = m.Next();

            Assert.That(next.Entry, Is.EqualTo(EntryType.Rf));
            Assert.That((next.GapMs, next.N), Is.EqualTo((0, 30)));
        }

        [Test]
        public void SecondRepetition_RfGoesFirst()
        {
            var m = NewMatrix();
            foreach (var gap in ConditionMatrix.Gaps)
            foreach (var n in ConditionMatrix.NValues)
            {
                Complete(m, EntryType.Opto, gap, n);
                Complete(m, EntryType.Rf, gap, n);
            }

            var next = m.Next();

            Assert.That(next.Entry, Is.EqualTo(EntryType.Rf));
            Assert.That((next.GapMs, next.N, next.Repetition), Is.EqualTo((0, 30, 2)));
        }

        [Test]
        public void NextFor_ConnectedEntry_SkipsAheadOfOtherEntry()
        {
            var m = NewMatrix();
            Complete(m, EntryType.Opto, 0, 30);

            var next = m.NextFor(EntryType.Opto);

            Assert.That((next.GapMs, next.N, next.Repetition), Is.EqualTo((0, 60, 1)));
        }

        [Test]
        public void VoidedRounds_DoNotCount()
        {
            var m = NewMatrix();
            m.Record(EntryType.Opto, 0, 30, voided: true);

            Assert.That(m.Completed(EntryType.Opto, 0, 30), Is.EqualTo(0));
            Assert.That(m.Next().Entry, Is.EqualTo(EntryType.Opto));
        }

        [Test]
        public void AllDone_NextIsNull()
        {
            var m = NewMatrix(repetitions: 1);
            foreach (var gap in ConditionMatrix.Gaps)
            foreach (var n in ConditionMatrix.NValues)
            {
                Complete(m, EntryType.Opto, gap, n);
                Complete(m, EntryType.Rf, gap, n);
            }

            Assert.That(m.Next(), Is.Null);
            Assert.That(m.NextFor(EntryType.Rf), Is.Null);
        }

        [Test]
        public void ExtraRoundsBeyondRepetitions_AreCountedButDoNotScheduleMore()
        {
            var m = NewMatrix(repetitions: 1);
            Complete(m, EntryType.Opto, 0, 30);
            Complete(m, EntryType.Opto, 0, 30);

            Assert.That(m.Completed(EntryType.Opto, 0, 30), Is.EqualTo(2));
            Assert.That((m.NextFor(EntryType.Opto).GapMs, m.NextFor(EntryType.Opto).N), Is.EqualTo((0, 60)));
        }
    }
}
