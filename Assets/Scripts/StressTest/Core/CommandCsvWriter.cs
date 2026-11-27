using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace StressTest.Core
{
    /// <summary>
    /// 逐筆指令 CSV。A 送法每送出一筆就有一列（沒有回覆的也有）；RUN 只有韌體回報時間戳的筆。
    /// 手動段沒有 round_id、sets、segment_index，三欄留空。每段寫完即 flush。
    /// </summary>
    public sealed class CommandCsvWriter
    {
        public const string Header =
            "round_id,entry,gap_ms,n,sets,segment_index,seq,direction,start_us,end_us,gap_us," +
            "send_mode,cmd_seq,status,pc_send_us,pc_reply_us,rx_us";

        private readonly TextWriter _output;

        public CommandCsvWriter(TextWriter output, bool writeHeader)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            if (writeHeader)
            {
                _output.WriteLine(Header);
                _output.Flush();
            }
        }

        /// <summary>n 省略時用這一段的筆數。</summary>
        public void Write(SegmentRun segment, EntryType entry, string roundId = null, int? n = null, int? sets = null,
            int? segmentIndex = null)
        {
            var c = CultureInfo.InvariantCulture;
            var prefix = string.Join(",",
                roundId ?? "",
                CsvFormat.Entry(entry),
                segment.GapMs.ToString(c),
                (n ?? segment.Count).ToString(c),
                CsvFormat.Count(sets),
                CsvFormat.Count(segmentIndex));
            var timestamps = segment.Timestamps.ToDictionary(t => t.Index);

            if (segment.Mode == SendMode.Ack)
            {
                foreach (var command in segment.Commands)
                {
                    var t = timestamps.TryGetValue(command.Position, out var found) ? found : (CommandTimestamp?)null;
                    WriteRow(prefix, segment, command.Position, t, command, timestamps);
                }
            }
            else
            {
                foreach (var t in segment.Timestamps) WriteRow(prefix, segment, t.Index, t, null, timestamps);
            }
            _output.Flush();
        }

        private void WriteRow(string prefix, SegmentRun segment, int position, CommandTimestamp? t,
            CommandRecord command, IReadOnlyDictionary<int, CommandTimestamp> timestamps)
        {
            var c = CultureInfo.InvariantCulture;

            // 相鄰的前一筆也有時間戳時，才算得出休息時間
            var gap = t.HasValue && timestamps.TryGetValue(position - 1, out var previous)
                ? unchecked((uint)(t.Value.StartMicros - previous.EndMicros)).ToString(c)
                : "";

            _output.WriteLine(string.Join(",",
                prefix,
                position.ToString(c),
                CsvFormat.Direction(segment.Direction),
                t?.StartMicros.ToString(c) ?? "",
                t?.EndMicros.ToString(c) ?? "",
                gap,
                segment.Mode.Code(),
                command?.Sequence.ToString(c) ?? "",
                command != null ? CsvFormat.Status(command.Status) : "",
                command != null ? Micros(command.PcSendSeconds) : "",
                command?.PcReplySeconds != null ? Micros(command.PcReplySeconds.Value) : "",
                command?.RxMicros?.ToString(c) ?? ""));
        }

        private static string Micros(double seconds) =>
            ((long)Math.Round(seconds * 1e6)).ToString(CultureInfo.InvariantCulture);
    }
}
