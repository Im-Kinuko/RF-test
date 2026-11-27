using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class ConditionMatrixTests
    {
        private static ConditionMatrix NewMatrix(int repetitions = 3) => new ConditionMatrix(repetitions);

        private static void Complete(ConditionMatrix m, EntryType entry, int gap, int count) =>
            m.Record(entry, gap, count, voided: false);

        [Test]
        public void Empty_FirstScheduledIsOptoGapZeroForty()
        {
            var next = NewMatrix().Next();

            Assert.That(next.Entry, Is.EqualTo(EntryType.Opto));
            Assert.That(next.GapMs, Is.EqualTo(0));
            Assert.That(next.TotalCount, Is.EqualTo(40));
            Assert.That(next.Repetition, Is.EqualTo(1));
        }

        [Test]
        public void WithinACondition_OtherEntryFollows()
        {
            var m = NewMatrix();
            Complete(m, EntryType.Opto, 0, 40);

            var next = m.Next();

            Assert.That(next.Entry, Is.EqualTo(EntryType.Rf));
            Assert.That((next.GapMs, next.TotalCount), Is.EqualTo((0, 40)));
        }

        [Test]
        public void SecondRepetition_RfGoesFirst()
        {
            var m = NewMatrix();
            foreach (var gap in ConditionMatrix.Gaps)
            foreach (var count in ConditionMatrix.Counts)
            {
                Complete(m, EntryType.Opto, gap, count);
                Complete(m, EntryType.Rf, gap, count);
            }

            var next = m.Next();

            Assert.That(next.Entry, Is.EqualTo(EntryType.Rf));
            Assert.That((next.GapMs, next.TotalCount, next.Repetition), Is.EqualTo((0, 40, 2)));
        }

        [Test]
        public void NextFor_ConnectedEntry_SkipsAheadOfOtherEntry()
        {
            var m = NewMatrix();
            Complete(m, EntryType.Opto, 0, 40);

            var next = m.NextFor(EntryType.Opto);

            Assert.That((next.GapMs, next.TotalCount, next.Repetition), Is.EqualTo((0, 80, 1)));
        }

        [Test]
        public void VoidedRounds_DoNotCount()
        {
            var m = NewMatrix();
            m.Record(EntryType.Opto, 0, 40, voided: true);

            Assert.That(m.Completed(EntryType.Opto, 0, 40), Is.EqualTo(0));
            Assert.That(m.Next().Entry, Is.EqualTo(EntryType.Opto));
        }

        [Test]
        public void AllDone_NextIsNull()
        {
            var m = NewMatrix(repetitions: 1);
            foreach (var gap in ConditionMatrix.Gaps)
            foreach (var count in ConditionMatrix.Counts)
            {
                Complete(m, EntryType.Opto, gap, count);
                Complete(m, EntryType.Rf, gap, count);
            }

            Assert.That(m.Next(), Is.Null);
            Assert.That(m.NextFor(EntryType.Rf), Is.Null);
        }

        [Test]
        public void ExtraRoundsBeyondRepetitions_AreCountedButDoNotScheduleMore()
        {
            var m = NewMatrix(repetitions: 1);
            Complete(m, EntryType.Opto, 0, 40);
            Complete(m, EntryType.Opto, 0, 40);

            Assert.That(m.Completed(EntryType.Opto, 0, 40), Is.EqualTo(2));
            Assert.That((m.NextFor(EntryType.Opto).GapMs, m.NextFor(EntryType.Opto).TotalCount), Is.EqualTo((0, 80)));
        }
    }
}
