using System;
using System.Globalization;
using System.IO;

namespace StressTest.Core
{
    /// <summary>
    /// 每段結果 CSV。每寫一列即 flush，Unity 當掉也不會遺失已完成的段。
    /// 編碼（UTF-8 BOM）由呼叫端建立 TextWriter 時決定。
    /// 單獨手動跑的段沒有 round_id 與 segment_index，兩欄留空。
    /// </summary>
    public sealed class SegmentCsvWriter
    {
        public const string Header =
            "recorded_at,round_id,segment_index,entry,firmware_version,direction,count,gap_ms,start_speed,expected_end,actual_end," +
            "expected_change,actual_change,drops,reported_count,report_short,timed_out,possibly_clamped,mean_gap_us,max_gap_us";

        private readonly TextWriter _output;

        public SegmentCsvWriter(TextWriter output, bool writeHeader)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            if (writeHeader)
            {
                _output.WriteLine(Header);
                _output.Flush();
            }
        }

        public void Write(SegmentResult result, EntryType entry, string firmwareVersion, DateTime recordedAt,
            string roundId = null, int? segmentIndex = null)
        {
            var s = result.Segment;
            var c = CultureInfo.InvariantCulture;
            var fields = new[]
            {
                CsvFormat.Time(recordedAt),
                roundId ?? "",
                segmentIndex?.ToString(c) ?? "",
                CsvFormat.Entry(entry),
                firmwareVersion,
                CsvFormat.Direction(s.Direction),
                s.Count.ToString(c),
                s.GapMs.ToString(c),
                CsvFormat.Speed(s.StartSpeed),
                CsvFormat.Speed(s.ExpectedEndSpeed),
                CsvFormat.Speed(result.ActualEndSpeed),
                CsvFormat.Speed(result.ExpectedChange),
                CsvFormat.Speed(result.ActualChange),
                result.Drops.ToString(c),
                s.ReportedCount?.ToString(c) ?? "",
                CsvFormat.Flag(s.ReportShort),
                CsvFormat.Flag(s.TimedOut),
                CsvFormat.Flag(result.PossiblyClamped),
                s.MeanGapMicros.ToString("F1", c),
                s.MaxGapMicros.ToString(c)
            };
            _output.WriteLine(string.Join(",", fields));
            _output.Flush();
        }
    }

    /// <summary>三份 CSV 共用的欄位格式。</summary>
    internal static class CsvFormat
    {
        public static string Time(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        public static string Entry(EntryType entry) => entry == EntryType.Opto ? "OPTO" : "RF";
        public static string Direction(SegmentDirection d) => d == SegmentDirection.Up ? "UP" : "DOWN";
        public static string Speed(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
        public static string Speed(double? v) => v.HasValue ? Speed(v.Value) : "";
        public static string Flag(bool b) => b ? "1" : "0";

        /// <summary>研究人員輸入的文字：半形逗號換成全形、換行換成空白，避免破壞 CSV 欄位。</summary>
        public static string FreeText(string text) =>
            (text ?? "").Replace(',', '，').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
