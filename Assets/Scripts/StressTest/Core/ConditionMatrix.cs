using System.Collections.Generic;

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
    /// 排程：先完成每個條件的第 1 輪再進入第 2 輪；條件依間隔、再依次數排序；
    /// 每個條件中，奇數輪光耦先、偶數輪 RF 先。
    /// </summary>
    public sealed class ConditionMatrix
    {
        public static readonly int[] Gaps = { 0, 1, 5, 10 };
        public static readonly int[] Counts = { 40, 80, 120 };

        private readonly Dictionary<(EntryType, int, int), int> _completed = new Dictionary<(EntryType, int, int), int>();

        public ConditionMatrix(int repetitions)
        {
            Repetitions = repetitions;
        }

        public int Repetitions { get; set; }

        public void Record(EntryType entry, int gapMs, int totalCount, bool voided)
        {
            if (voided) return;
            var key = (entry, gapMs, totalCount);
            _completed.TryGetValue(key, out var n);
            _completed[key] = n + 1;
        }

        /// <summary>
        /// 讀入一份每輪 CSV 的所有行，累加已完成的輪次；以標題名稱找欄位，可讀舊版格式。
        /// 格式不符的列略過。回傳計入的列數。
        /// </summary>
        public int LoadRoundsCsv(IEnumerable<string> lines)
        {
            int entryCol = -1, gapCol = -1, countCol = -1, statusCol = -1;
            var headerSeen = false;
            var loaded = 0;

            foreach (var raw in lines)
            {
                var line = (raw ?? "").TrimStart('﻿');
                if (line.Length == 0) continue;
                var fields = line.Split(',');

                if (!headerSeen)
                {
                    headerSeen = true;
                    entryCol = System.Array.IndexOf(fields, "entry");
                    gapCol = System.Array.IndexOf(fields, "gap_ms");
                    countCol = System.Array.IndexOf(fields, "total_count");
                    statusCol = System.Array.IndexOf(fields, "status");
                    if (entryCol < 0 || gapCol < 0 || countCol < 0 || statusCol < 0) return 0;
                    continue;
                }

                var needed = System.Math.Max(System.Math.Max(entryCol, gapCol), System.Math.Max(countCol, statusCol));
                if (fields.Length <= needed) continue;
                if (fields[statusCol] != "completed") continue;

                EntryType entry;
                if (fields[entryCol] == "OPTO") entry = EntryType.Opto;
                else if (fields[entryCol] == "RF") entry = EntryType.Rf;
                else continue;

                if (!int.TryParse(fields[gapCol], out var gap) || !int.TryParse(fields[countCol], out var count)) continue;

                Record(entry, gap, count, voided: false);
                loaded++;
            }
            return loaded;
        }

        public int Completed(EntryType entry, int gapMs, int totalCount) =>
            _completed.TryGetValue((entry, gapMs, totalCount), out var n) ? n : 0;

        /// <summary>整體排程中下一個尚未完成的輪次；全部完成時為 null。</summary>
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
