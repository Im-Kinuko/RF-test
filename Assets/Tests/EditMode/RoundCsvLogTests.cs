using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundCsvLogTests
    {
        private static readonly DateTime Fixed = new DateTime(2026, 11, 22, 9, 30, 0);

        private sealed class Outputs
        {
            public readonly StringWriter Commands = new StringWriter();
            public readonly StringWriter Segments = new StringWriter();
            public readonly StringWriter Rounds = new StringWriter();

            public RoundCsvLog CreateLog(bool headers = true) =>
                new RoundCsvLog(Commands, Segments, Rounds, headers, () => Fixed);

            public static string[] Lines(StringWriter w) =>
                w.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static Outputs RunFortyRound()
        {
            var outputs = new Outputs();
            var h = new RoundHarness(gapMs: 5,
                arduino: new FakeArduino { GapJitterMicros = 8 }, log: outputs.CreateLog());
            h.StartAndWarmUp();
            h.CompleteSegment(3.0); // 第一段全收到
            h.CompleteSegment(1.3); // 第二段漏 3 筆
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 1);
            h.Round.Finish();
            return outputs;
        }

        [Test]
        public void Commands_OneRowPerTimestampWithActualGap()
        {
            var lines = Outputs.Lines(RunFortyRound().Commands);

            Assert.That(lines[0], Is.EqualTo("round_id,entry,gap_ms,total_count,segment_index,seq,direction,start_us,end_us,gap_us," +
                "send_mode,cmd_seq,status,pc_send_us,pc_reply_us,rx_us"));
            Assert.That(lines.Length, Is.EqualTo(1 + 40));
            Assert.That(lines[1], Is.EqualTo("R1,OPTO,5,40,0,0,UP,1000000,1120000,,RUN,,,,,"));
            Assert.That(lines[2], Is.EqualTo("R1,OPTO,5,40,0,1,UP,1125008,1245008,5008,RUN,,,,,"));
            Assert.That(lines[21], Does.StartWith("R1,OPTO,5,40,1,0,DOWN,"));
        }

        [Test]
        public void Segments_OneRowPerConfirmedSegmentWithRoundId()
        {
            var lines = Outputs.Lines(RunFortyRound().Segments);

            Assert.That(lines.Length, Is.EqualTo(1 + 2));
            Assert.That(lines[1], Does.StartWith("2026-11-22 09:30:00,R1,0,OPTO,0.1.0,UP,20,5,1.0,3.0,3.0,"));
            Assert.That(lines[2], Does.StartWith("2026-11-22 09:30:00,R1,1,OPTO,0.1.0,DOWN,20,5,3.0,1.0,1.3,"));
        }

        [Test]
        public void Rounds_OneSummaryRowWhenCompleted()
        {
            var lines = Outputs.Lines(RunFortyRound().Rounds);

            Assert.That(lines[0], Is.EqualTo(
                "recorded_at,round_id,entry,firmware_version,firmware_params,gap_ms,total_count,segment_size,segments," +
                "total_sent,total_drops,warmup_reading,end_reading,any_possibly_clamped,any_report_short,mean_gap_us,max_gap_us," +
                "up_planned,down_planned,up_sent,down_sent,warmup_up,warmup_down," +
                "send_mode,pc_sent,arduino_received,arduino_executed,link_lost,treadmill_missing,any_rebooted," +
                "status,void_reason"));
            Assert.That(lines.Skip(1), Is.EqualTo(new[]
            {
                "2026-11-22 09:30:00,R1,OPTO,0.1.0,press_ms=120;action_ms=120;max_run=90;ack=1,5,40,20,2,40,3,1.0,1.3,0,0,5008.0,5008,20,20,20,20,0,0,RUN,,,,,3,0,completed,"
            }));
        }

        [Test]
        public void ExistingFiles_DoNotRepeatHeaders()
        {
            var outputs = new Outputs();
            outputs.CreateLog(headers: false);

            Assert.That(outputs.Commands.ToString(), Is.Empty);
            Assert.That(outputs.Segments.ToString(), Is.Empty);
            Assert.That(outputs.Rounds.ToString(), Is.Empty);
        }
    }
}
