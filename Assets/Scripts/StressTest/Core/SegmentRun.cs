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

    /// <summary>一段連續指令（例如加速 30 筆）的設定與韌體回報。</summary>
    public sealed class SegmentRun
    {
        /// <summary>跑步機每次按鍵的速度變化（km/h）。</summary>
        public const double SpeedStep = 0.1;

        /// <summary>A 送法一段的筆數上限（由電腦逐筆送，不受韌體緩衝限制）。</summary>
        public const int MaxPacedCount = 300;

        private readonly List<CommandTimestamp> _timestamps = new List<CommandTimestamp>();

        private readonly List<CommandRecord> _commands = new List<CommandRecord>();

        public SegmentRun(SegmentDirection direction, int count, int gapMs, double startSpeed,
            SendMode mode = SendMode.Burst)
        {
            Mode = mode;
            Direction = direction;
            Count = count;
            GapMs = gapMs;
            StartSpeed = startSpeed;
        }

        public SegmentDirection Direction { get; }
        public int Count { get; }
        public int GapMs { get; }

        /// <summary>本段起點速度。連續傳送時先放理論值，輸入讀數後改為上一段的實際終點。</summary>
        public double StartSpeed { get; internal set; }

        /// <summary>屬於中間不停的整輪傳送。</summary>
        public bool Continuous { get; internal set; }

        /// <summary>連續傳送中的前一段；換方向那一次的休息時間要跨段計算。</summary>
        public SegmentRun Predecessor { get; internal set; }

        public double ExpectedChange => (Direction == SegmentDirection.Up ? 1 : -1) * Count * SpeedStep;
        public double ExpectedEndSpeed => StartSpeed + ExpectedChange;

        public SendMode Mode { get; }

        /// <summary>依段內順序排列；缺回覆的筆沒有時間戳。</summary>
        public IReadOnlyList<CommandTimestamp> Timestamps => _timestamps;

        /// <summary>A 送法中電腦送出的每一筆；RUN 沒有逐筆紀錄。</summary>
        public IReadOnlyList<CommandRecord> Commands => _commands;

        public int CountWithStatus(CommandStatus status)
        {
            var n = 0;
            foreach (var c in _commands) if (c.Status == status) n++;
            return n;
        }

        /// <summary>電腦送出的筆數（A 送法）。</summary>
        public int PcSent => _commands.Count;

        /// <summary>收到完成回覆的筆數（含遲到的）。</summary>
        public int Replies { get; internal set; }

        /// <summary>段末 STAT 回報的收到／執行／毀損筆數（A 送法）；問不到時為 null。</summary>
        public int? ArduinoReceived { get; internal set; }
        public int? ArduinoExecuted { get; internal set; }
        public int? ArduinoBad { get; internal set; }

        /// <summary>這一段執行中 Arduino 重新開機。</summary>
        public bool Rebooted { get; internal set; }

        /// <summary>
        /// Arduino 回報實際執行的筆數：RUN 來自 DONE；A 來自 STAT，問不到時用完成回覆數。
        /// 尚未知道時為 null。
        /// </summary>
        public int? ReportedCount { get; internal set; }

        /// <summary>被 ABORT 中止；ReportedCount 為中止前已送出的筆數。</summary>
        public bool Aborted { get; internal set; }

        /// <summary>在期限內沒有收到 DONE。</summary>
        public bool TimedOut { get; internal set; }

        /// <summary>「Arduino 回報不足」：逾時、DONE 筆數少於應送筆數，或時間戳不足。</summary>
        public bool ReportShort => TimedOut || Rebooted || ReportedCount < Count || _timestamps.Count < Count;

        /// <summary>上一筆結束到下一筆開始的平均間隔（µs）；沒有相鄰的兩筆時為 0。</summary>
        public double MeanGapMicros
        {
            get
            {
                long sum = 0;
                var n = 0;
                foreach (var gap in Gaps)
                {
                    sum += gap;
                    n++;
                }
                return n > 0 ? (double)sum / n : 0;
            }
        }

        /// <summary>上一筆結束到下一筆開始的最大間隔（µs）；沒有相鄰的兩筆時為 0。</summary>
        public long MaxGapMicros
        {
            get
            {
                long max = 0;
                foreach (var gap in Gaps) if (gap > max) max = gap;
                return max;
            }
        }

        /// <summary>
        /// 換方向那一次的休息時間（µs）：前一段最後一筆到本段第一筆；任一筆沒有時間戳時為 null。
        /// </summary>
        public long? GapFromPredecessor
        {
            get
            {
                if (Predecessor == null || _timestamps.Count == 0 || _timestamps[0].Index != 0) return null;
                var previous = Predecessor.Timestamps;
                if (previous.Count == 0 || previous[previous.Count - 1].Index != Predecessor.Count - 1) return null;
                return unchecked((uint)(_timestamps[0].StartMicros - previous[previous.Count - 1].EndMicros));
            }
        }

        /// <summary>
        /// 相鄰兩筆之間的實際休息時間（µs），含換方向那一次；中間缺一筆時不計。
        /// </summary>
        public IEnumerable<long> Gaps
        {
            get
            {
                if (GapFromPredecessor is long first) yield return first;
                for (var i = 1; i < _timestamps.Count; i++)
                {
                    if (_timestamps[i].Index == _timestamps[i - 1].Index + 1) yield return Gap(i);
                }
            }
        }

        internal void AddTimestamp(CommandTimestamp timestamp)
        {
            // 遲到的回覆要插回原本的位置
            var at = _timestamps.Count;
            while (at > 0 && _timestamps[at - 1].Index > timestamp.Index) at--;
            _timestamps.Insert(at, timestamp);
        }

        internal void AddCommand(CommandRecord command) => _commands.Add(command);

        /// <summary>
        /// micros() 是 32 位元計數器，約 71.6 分鐘溢位歸零；以 2^32 取模相減，跨越溢位仍正確。
        /// </summary>
        private long Gap(int i) => unchecked((uint)(_timestamps[i].StartMicros - _timestamps[i - 1].EndMicros));
    }
}
