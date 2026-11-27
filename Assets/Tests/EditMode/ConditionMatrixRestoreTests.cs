using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class ConditionMatrixRestoreTests
    {
        private const string Header = "﻿" + RoundCsvLog.RoundsHeader;

        private static string Row(string entry, int gap, int n, string status, int sets = 2) =>
            $"2026-11-22 10:00:00,R,{entry},0.4.0,press_ms=120,{gap},{n},{sets},{2 * sets * n},4,120,0,2.0,2.0,0,0,5008.0,5008,60,60,60,60,11,0,A,120,120,120,0,0,0,{status},";

        [Test]
        public void CountsOnlyCompletedRows()
        {
            var m = new ConditionMatrix(3);

            var loaded = m.LoadRoundsCsv(new[]
            {
                Header,
                Row("OPTO", 0, 30, "completed"),
                Row("OPTO", 0, 30, "completed"),
                Row("RF", 0, 30, "voided"),
                Row("RF", 5, 60, "completed"),
            });

            Assert.That(loaded, Is.EqualTo(3));
            Assert.That(m.Completed(EntryType.Opto, 0, 30), Is.EqualTo(2));
            Assert.That(m.Completed(EntryType.Rf, 0, 30), Is.EqualTo(0));
            Assert.That(m.Completed(EntryType.Rf, 5, 60), Is.EqualTo(1));
        }

        [Test]
        public void FindsColumnsByHeaderName_InAnyOrder_AndFilesWithoutSendModeAreRun()
        {
            var m = new ConditionMatrix(3) { Mode = SendMode.Burst };

            m.LoadRoundsCsv(new[]
            {
                "round_id,status,sets,n,gap_ms,entry",
                "R1,completed,2,90,10,RF",
            });

            Assert.That(m.Completed(EntryType.Rf, 10, 90), Is.EqualTo(1));
        }

        [Test]
        public void SkipsMalformedRowsAndFilesWithoutRequiredColumns()
        {
            var m = new ConditionMatrix(3);

            var loaded = m.LoadRoundsCsv(new[]
            {
                Header,
                "",
                "garbage",
                Row("XYZ", 0, 30, "completed"),
                "2026-11-22 10:00:00,R,OPTO,0.4.0,p,abc,30,2,completed",
            });
            var fromOtherFile = m.LoadRoundsCsv(new[] { "recorded_at,round_id", "x,y" });

            Assert.That(loaded, Is.EqualTo(0));
            Assert.That(fromOtherFile, Is.EqualTo(0));
            Assert.That(m.Next().Repetition, Is.EqualTo(1));
        }

        [Test]
        public void SeveralFiles_Accumulate()
        {
            var m = new ConditionMatrix(3);

            m.LoadRoundsCsv(new[] { Header, Row("OPTO", 1, 60, "completed") });
            m.LoadRoundsCsv(new[] { Header, Row("OPTO", 1, 60, "completed") });

            Assert.That(m.Completed(EntryType.Opto, 1, 60), Is.EqualTo(2));
        }

        [Test]
        public void OldDesignRecordsAndOtherSetCounts_DoNotCount()
        {
            var m = new ConditionMatrix(3);

            var old = m.LoadRoundsCsv(new[]
            {
                "round_id,entry,gap_ms,total_count,segment_size,status",
                "R1,OPTO,0,120,30,completed",
            });
            var oneSet = m.LoadRoundsCsv(new[] { Header, Row("OPTO", 0, 30, "completed", sets: 1) });

            Assert.That((old, oneSet), Is.EqualTo((0, 0)));
        }
    }
}
