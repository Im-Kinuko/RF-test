using System.Collections.Generic;

namespace StressTest.Core
{
    public enum SegmentDirection
    {
        Up,
        Down
    }

    /// <summary>韌體回報的一筆輸出時間（micros()，單位 µs）。</summary>
    public readonly struct CommandTimestamp
    {
        public CommandTimestamp(int index, long startMicros, long endMicros)
        {
            Index = index;
            StartMicros = startMicros;
            EndMicros = endMicros;
        }

        public int Index { get; }
        public long StartMicros { get; }
        public long EndMicros { get; }
    }

    /// <summary>一段連續指令（例如加速 20 筆）的設定與韌體回報。</summary>
    public sealed class SegmentRun
    {
        /// <summary>跑步機每次按鍵的速度變化（km/h）。</summary>
        public const double SpeedStep = 0.1;

        private readonly List<CommandTimestamp> _timestamps = new List<CommandTimestamp>();

        public SegmentRun(SegmentDirection direction, int count, int gapMs, double startSpeed)
        {
            Direction = direction;
            Count = count;
            GapMs = gapMs;
            StartSpeed = startSpeed;
        }

        public SegmentDirection Direction { get; }
        public int Count { get; }
        public int GapMs { get; }
        public double StartSpeed { get; }

        public double ExpectedChange => (Direction == SegmentDirection.Up ? 1 : -1) * Count * SpeedStep;
        public double ExpectedEndSpeed => StartSpeed + ExpectedChange;

        public IReadOnlyList<CommandTimestamp> Timestamps => _timestamps;

        /// <summary>DONE 行回報的實際輸出筆數；尚未收到 DONE 時為 null。</summary>
        public int? ReportedCount { get; internal set; }

        /// <summary>在期限內沒有收到 DONE。</summary>
        public bool TimedOut { get; internal set; }

        /// <summary>「Arduino 回報不足」：逾時、DONE 筆數少於應送筆數，或時間戳不足。</summary>
        public bool ReportShort => TimedOut || ReportedCount < Count || _timestamps.Count < Count;

        /// <summary>上一筆結束到下一筆開始的平均間隔（µs）；少於兩筆時為 0。</summary>
        public double MeanGapMicros
        {
            get
            {
                if (_timestamps.Count < 2) return 0;
                long sum = 0;
                for (var i = 1; i < _timestamps.Count; i++) sum += Gap(i);
                return (double)sum / (_timestamps.Count - 1);
            }
        }

        /// <summary>上一筆結束到下一筆開始的最大間隔（µs）；少於兩筆時為 0。</summary>
        public long MaxGapMicros
        {
            get
            {
                long max = 0;
                for (var i = 1; i < _timestamps.Count; i++) if (Gap(i) > max) max = Gap(i);
                return max;
            }
        }

        /// <summary>每筆與上一筆之間的實際間隔（µs），共 Timestamps.Count − 1 個。</summary>
        public IEnumerable<long> Gaps
        {
            get
            {
                for (var i = 1; i < _timestamps.Count; i++) yield return Gap(i);
            }
        }

        internal void AddTimestamp(CommandTimestamp timestamp) => _timestamps.Add(timestamp);

        /// <summary>
        /// micros() 是 32 位元計數器，約 71.6 分鐘溢位歸零；以 2^32 取模相減，跨越溢位仍正確。
        /// </summary>
        private long Gap(int i) => unchecked((uint)(_timestamps[i].StartMicros - _timestamps[i - 1].EndMicros));
    }
}
