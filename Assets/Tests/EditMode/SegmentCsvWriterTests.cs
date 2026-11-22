using System;
using System.IO;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class SegmentCsvWriterTests
    {
        private const string Header =
            "recorded_at,round_id,segment_index,entry,firmware_version,direction,count,gap_ms,start_speed,expected_end,actual_end," +
            "expected_change,actual_change,drops,reported_count,report_short,timed_out,possibly_clamped,mean_gap_us,max_gap_us";

        private static SegmentResult CompletedSegment(FakeArduino arduino, double panelEnd)
        {
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            session.RunSegment(SegmentDirection.Up, 20, 5, 1.0, 1.0);
            session.Poll(1.1);
            return session.SubmitReading(panelEnd);
        }

        [Test]
        public void NewFile_WritesHeaderThenOneRowPerSegment()
        {
            var output = new StringWriter();
            var writer = new SegmentCsvWriter(output, writeHeader: true);

            writer.Write(CompletedSegment(new FakeArduino(), 2.7), EntryType.Opto, "0.1.0",
                new DateTime(2026, 11, 22, 14, 5, 9));

            var lines = output.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo(Header));
            Assert.That(lines[1], Is.EqualTo(
                "2026-11-22 14:05:09,,,OPTO,0.1.0,UP,20,5,1.0,3.0,2.7,2.0,1.7,3,20,0,0,0,5008.0,5008"));
        }

        [Test]
        public void RoundSegment_IncludesRoundIdAndSegmentIndex()
        {
            var output = new StringWriter();
            var writer = new SegmentCsvWriter(output, writeHeader: false);

            writer.Write(CompletedSegment(new FakeArduino(), 3.0), EntryType.Rf, "0.2.0",
                new DateTime(2026, 11, 22, 0, 0, 0), "R7", 3);

            Assert.That(output.ToString().TrimEnd(), Does.StartWith("2026-11-22 00:00:00,R7,3,RF,0.2.0,UP,"));
        }

        [Test]
        public void ExistingFile_DoesNotRepeatHeader()
        {
            var output = new StringWriter();
            var writer = new SegmentCsvWriter(output, writeHeader: false);

            writer.Write(CompletedSegment(new FakeArduino(), 3.0), EntryType.Opto, "0.1.0", DateTime.Now);

            Assert.That(output.ToString(), Does.Not.Contain("recorded_at"));
        }

        [Test]
        public void TimedOutSegment_LeavesReportedCountEmptyAndSetsFlags()
        {
            var output = new StringWriter();
            var writer = new SegmentCsvWriter(output, writeHeader: false);

            var session = new StressTestSession(new FakeArduino { HangOnRun = true }, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            session.RunSegment(SegmentDirection.Up, 20, 5, 1.0, 1.0);
            session.Poll(100.0);
            var result = session.SubmitReading(1.0);

            writer.Write(result, EntryType.Opto, "0.1.0", new DateTime(2026, 11, 22, 0, 0, 0));

            Assert.That(output.ToString().TrimEnd(), Is.EqualTo(
                "2026-11-22 00:00:00,,,OPTO,0.1.0,UP,20,5,1.0,3.0,1.0,2.0,0.0,20,,1,1,0,0.0,0"));
        }
    }
}
