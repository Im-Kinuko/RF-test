using System.Collections.Generic;
using System.Linq;

namespace StressTest.Core
{
    /// <summary>排程中的一項：某入口在某條件下的第幾輪。</summary>
    public sealed class ScheduledRound
    {
        public ScheduledRound(EntryType entry, int gapMs, int totalCount, int repetition)
        {
            Entry = entry;
            GapMs = gapMs;
            TotalCount = totalCount;
            Repetition = repetition;
        }

        public EntryType Entry { get; }
        public int GapMs { get; }
        public int TotalCount { get; }

        /// <summary>第幾輪，從 1 起算。</summary>
        public int Repetition { get; }
    }

    /// <summary>
    /// 間隔 × 總次數 × 入口的完成進度與排程。只計入 completed 的輪次，作廢不算。
    /// 每個送法的進度分開記；查詢與排程看目前的 Mode。
    /// 排程：先完成每個條件的第 1 輪再進入第 2 輪；條件依間隔、再依次數排序；
    /// 每個條件中，奇數輪光耦先、偶數輪 RF 先。
    /// </summary>
    public sealed class ConditionMatrix
    {
        public static readonly int[] Gaps = { 0, 1, 5, 10 };
        public static readonly int[] Counts = { 40, 80, 120 };

        private readonly Dictionary<(SendMode, EntryType, int, int), int> _completed =
            new Dictionary<(SendMode, EntryType, int, int), int>();

        public ConditionMatrix(int repetitions)
        {
            Repetitions = repetitions;
        }

        public int Repetitions { get; set; }

        /// <summary>目前查詢與排程的送法。</summary>
        public SendMode Mode { get; set; } = SendMode.Ack;

        /// <summary>mode 省略時記在目前的送法。</summary>
        public void Record(EntryType entry, int gapMs, int totalCount, bool voided, SendMode? mode = null)
        {
            if (voided) return;
            var key = (mode ?? Mode, entry, gapMs, totalCount);
            _completed.TryGetValue(key, out var done);
            _completed[key] = done + 1;
        }

        /// <summary>
        /// 讀入一份每輪 CSV 的所有行，累加已完成的輪次；以標題名稱找欄位。
        /// 有 sets 欄的檔案是「每段 n 筆」那版設計的紀錄，整份不計入。沒有 send_mode 欄的檔案是 RUN。
        /// 格式不符的列略過。回傳讀入的完成列數（含其他送法）。
        /// </summary>
        public int LoadRoundsCsv(IEnumerable<string> lines)
        {
            int entryCol = -1, gapCol = -1, countCol = -1, statusCol = -1, modeCol = -1;
            var headerSeen = false;
            var loaded = 0;

            foreach (var raw in lines)
            {
                var line = (raw ?? "").TrimStart('\uFEFF');
                if (line.Length == 0) continue;
                var fields = line.Split(',');

                if (!headerSeen)
                {
                    headerSeen = true;
                    entryCol = System.Array.IndexOf(fields, "entry");
                    gapCol = System.Array.IndexOf(fields, "gap_ms");
                    countCol = System.Array.IndexOf(fields, "total_count");
                    statusCol = System.Array.IndexOf(fields, "status");
                    modeCol = System.Array.IndexOf(fields, "send_mode");
                    if (entryCol < 0 || gapCol < 0 || countCol < 0 || statusCol < 0) return 0;
                    if (System.Array.IndexOf(fields, "sets") >= 0) return 0;
                    continue;
                }

                var needed = new[] { entryCol, gapCol, countCol, statusCol, modeCol }.Max();
                if (fields.Length <= needed) continue;
                if (fields[statusCol] != "completed") continue;

                EntryType entry;
                if (fields[entryCol] == "OPTO") entry = EntryType.Opto;
                else if (fields[entryCol] == "RF") entry = EntryType.Rf;
                else continue;

                if (!int.TryParse(fields[gapCol], out var gap) || !int.TryParse(fields[countCol], out var count)) continue;
                var mode = SendMode.Burst;
                if (modeCol >= 0 && !SendModeCodes.TryParse(fields[modeCol], out mode)) continue;

                Record(entry, gap, count, voided: false, mode);
                loaded++;
            }
            return loaded;
        }

        public int Completed(EntryType entry, int gapMs, int totalCount) =>
            _completed.TryGetValue((Mode, entry, gapMs, totalCount), out var done) ? done : 0;

        public ScheduledRound Next() => FirstPending(null);

        /// <summary>只看目前連線入口時，排程中下一個尚未完成的輪次；全部完成時為 null。</summary>
        public ScheduledRound NextFor(EntryType entry) => FirstPending(entry);

        private ScheduledRound FirstPending(EntryType? only)
        {
            for (var rep = 1; rep <= Repetitions; rep++)
            {
                var first = rep % 2 == 1 ? EntryType.Opto : EntryType.Rf;
                var second = first == EntryType.Opto ? EntryType.Rf : EntryType.Opto;

                foreach (var gap in Gaps)
                foreach (var count in Counts)
                foreach (var entry in new[] { first, second })
                {
                    if (only.HasValue && entry != only.Value) continue;
                    if (Completed(entry, gap, count) < rep) return new ScheduledRound(entry, gap, count, rep);
                }
            }
            return null;
        }
    }
}
