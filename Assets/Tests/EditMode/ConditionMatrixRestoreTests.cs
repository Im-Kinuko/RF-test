using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class ConditionMatrixRestoreTests
    {
        private const string Header = "﻿" + RoundCsvLog.RoundsHeader;

        private static string Row(string entry, int gap, int count, string status) =>
            $"2026-11-22 10:00:00,R,{entry},0.3.0,press_ms=120,{gap},{count},20,2,40,0,1.0,1.0,0,0,5008.0,5008,{status},";

        [Test]
        public void CountsOnlyCompletedRows()
        {
            var m = new ConditionMatrix(3);

            var loaded = m.LoadRoundsCsv(new[]
            {
                Header,
                Row("OPTO", 0, 40, "completed"),
                Row("OPTO", 0, 40, "completed"),
                Row("RF", 0, 40, "voided"),
                Row("RF", 5, 80, "completed"),
            });

            Assert.That(loaded, Is.EqualTo(3));
            Assert.That(m.Completed(EntryType.Opto, 0, 40), Is.EqualTo(2));
            Assert.That(m.Completed(EntryType.Rf, 0, 40), Is.EqualTo(0));
            Assert.That(m.Completed(EntryType.Rf, 5, 80), Is.EqualTo(1));
        }

        [Test]
        public void FindsColumnsByHeaderName_OlderFileWithoutVoidReason()
        {
            var m = new ConditionMatrix(3);

            m.LoadRoundsCsv(new[]
            {
                "round_id,status,total_count,gap_ms,entry",
                "R1,completed,120,10,RF",
            });

            Assert.That(m.Completed(EntryType.Rf, 10, 120), Is.EqualTo(1));
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
                Row("XYZ", 0, 40, "completed"),
                "2026-11-22 10:00:00,R,OPTO,0.3.0,p,abc,40,20,2,40,0,1.0,1.0,0,0,1,1,completed,",
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

            m.LoadRoundsCsv(new[] { Header, Row("OPTO", 1, 80, "completed") });
            m.LoadRoundsCsv(new[] { Header, Row("OPTO", 1, 80, "completed") });

            Assert.That(m.Completed(EntryType.Opto, 1, 80), Is.EqualTo(2));
        }
    }
}
