using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace StressTest.Core
{
    /// <summary>
    /// 一輪測試的三份 CSV：逐筆（commands）、每段（segments）、每輪（rounds）。
    /// 每段確認時寫入逐筆與每段，一輪完成時寫入每輪；每次寫入都 flush。
    /// </summary>
    public sealed class RoundCsvLog
    {
        public const string CommandsHeader =
            "round_id,entry,gap_ms,n,sets,segment_index,seq,direction,start_us,end_us,gap_us";

        public const string RoundsHeader =
            "recorded_at,round_id,entry,firmware_version,firmware_params,gap_ms,n,sets,total_count,segments," +
            "total_sent,total_drops,warmup_reading,end_reading,any_possibly_clamped,any_report_short,mean_gap_us,max_gap_us," +
            "up_planned,down_planned,up_sent,down_sent,warmup_up,warmup_down,status,void_reason";

        private readonly TextWriter _commands;
        private readonly SegmentCsvWriter _segments;
        private readonly TextWriter _rounds;
        private readonly Func<DateTime> _clock;

        public RoundCsvLog(TextWriter commands, TextWriter segments, TextWriter rounds, bool writeHeaders,
            Func<DateTime> clock = null)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _rounds = rounds ?? throw new ArgumentNullException(nameof(rounds));
            _segments = new SegmentCsvWriter(segments, writeHeaders);
            _clock = clock ?? (() => DateTime.Now);

            if (writeHeaders)
            {
                _commands.WriteLine(CommandsHeader);
                _commands.Flush();
                _rounds.WriteLine(RoundsHeader);
                _rounds.Flush();
            }
        }

        public void WriteSegment(string roundId, RoundConfig config, FirmwareInfo firmware, int segmentIndex, SegmentResult result)
        {
            var c = CultureInfo.InvariantCulture;
            var s = result.Segment;
            long? previousEnd = null;

            foreach (var t in s.Timestamps)
            {
                var gap = previousEnd.HasValue
                    ? unchecked((uint)(t.StartMicros - previousEnd.Value)).ToString(c)
                    : "";
                _commands.WriteLine(string.Join(",",
                    roundId,
                    CsvFormat.Entry(firmware.Entry),
                    config.GapMs.ToString(c),
                    config.N.ToString(c),
                    config.Sets.ToString(c),
                    segmentIndex.ToString(c),
                    t.Index.ToString(c),
                    CsvFormat.Direction(s.Direction),
                    t.StartMicros.ToString(c),
                    t.EndMicros.ToString(c),
                    gap));
                previousEnd = t.EndMicros;
            }
            _commands.Flush();

            _segments.Write(result, firmware.Entry, firmware.Version, _clock(), roundId, segmentIndex);
        }

        public void WriteRound(string roundId, RoundConfig config, FirmwareInfo firmware, RoundSummary summary, string status,
            string voidReason = null)
        {
            var c = CultureInfo.InvariantCulture;
            var parameters = string.Join(";", firmware.Parameters.Select(kv => $"{kv.Key}={kv.Value}"));

            _rounds.WriteLine(string.Join(",",
                CsvFormat.Time(_clock()),
                roundId,
                CsvFormat.Entry(firmware.Entry),
                firmware.Version,
                parameters,
                config.GapMs.ToString(c),
                config.N.ToString(c),
                config.Sets.ToString(c),
                config.TotalCount.ToString(c),
                summary.SegmentCount.ToString(c),
                summary.TotalSent.ToString(c),
                summary.TotalDrops.ToString(c),
                CsvFormat.Speed(summary.WarmupReading),
                CsvFormat.Speed(summary.EndReading),
                CsvFormat.Flag(summary.AnyPossiblyClamped),
                CsvFormat.Flag(summary.AnyReportShort),
                summary.MeanGapMicros.ToString("F1", c),
                summary.MaxGapMicros.ToString(c),
                summary.UpPlanned.ToString(c),
                summary.DownPlanned.ToString(c),
                summary.UpSent.ToString(c),
                summary.DownSent.ToString(c),
                summary.WarmupUpSent.ToString(c),
                summary.WarmupDownSent.ToString(c),
                status,
                CsvFormat.FreeText(voidReason)));
            _rounds.Flush();
        }
    }
}
