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
        public const string CommandsHeader = CommandCsvWriter.Header;

        public const string RoundsHeader =
            "recorded_at,round_id,entry,firmware_version,firmware_params,gap_ms,n,sets,total_count,segments," +
            "total_sent,total_drops,warmup_reading,end_reading,any_possibly_clamped,any_report_short,mean_gap_us,max_gap_us," +
            "up_planned,down_planned,up_sent,down_sent,warmup_up,warmup_down," +
            "send_mode,pc_sent,arduino_received,arduino_executed,link_lost,treadmill_missing,any_rebooted," +
            "status,void_reason";

        private readonly CommandCsvWriter _commands;
        private readonly SegmentCsvWriter _segments;
        private readonly TextWriter _rounds;
        private readonly Func<DateTime> _clock;

        public RoundCsvLog(TextWriter commands, TextWriter segments, TextWriter rounds, bool writeHeaders,
            Func<DateTime> clock = null)
        {
            _commands = new CommandCsvWriter(commands, writeHeaders);
            _rounds = rounds ?? throw new ArgumentNullException(nameof(rounds));
            _segments = new SegmentCsvWriter(segments, writeHeaders);
            _clock = clock ?? (() => DateTime.Now);

            if (writeHeaders)
            {
                _rounds.WriteLine(RoundsHeader);
                _rounds.Flush();
            }
        }

        public void WriteSegment(string roundId, RoundConfig config, FirmwareInfo firmware, int segmentIndex, SegmentResult result)
        {
            _commands.Write(result.Segment, firmware.Entry, roundId, config.N, config.Sets, segmentIndex);
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
                config.Mode.Code(),
                CsvFormat.Count(summary.PcSent),
                CsvFormat.Count(summary.ArduinoReceived),
                CsvFormat.Count(summary.ArduinoExecuted),
                CsvFormat.Count(summary.LinkLost),
                CsvFormat.Count(summary.TreadmillMissing),
                CsvFormat.Flag(summary.AnyRebooted),
                status,
                CsvFormat.FreeText(voidReason)));
            _rounds.Flush();
        }
    }
}
