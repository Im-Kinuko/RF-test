using System.Collections.Generic;

namespace StressTest.Core
{
    /// <summary>
    /// 一次連續的逐筆傳送：由一個或多個段（方向相同的一串指令）組成，段與段之間不停。
    /// Arduino 的計數由段末的 STAT 取得，是整次傳送的合計。
    /// </summary>
    public sealed class PacedRun
    {
        /// <summary>連續這麼多筆沒有準時回覆就提早結束整次傳送。</summary>
        public const int MaxConsecutiveNoReply = 3;

        public bool AnyRebooted => Any(b => b.Rebooted);
        public bool AnyTimedOut => Any(b => b.TimedOut);

        /// <summary>有送出指令的段數（段依序傳送，所以是前面這幾段）。</summary>
        public int SentBlockCount
        {
            get
            {
                var n = 0;
                foreach (var b in Blocks) if (b.PcSent > 0) n++;
                return n;
            }
        }

        private bool Any(System.Func<SegmentRun, bool> test)
        {
            foreach (var b in Blocks) if (test(b)) return true;
            return false;
        }

        public PacedRun(IReadOnlyList<SegmentRun> blocks)
        {
            Blocks = blocks;
        }

        public IReadOnlyList<SegmentRun> Blocks { get; }

        public int Count => Sum(b => b.Count);
        public int PcSent => Sum(b => b.PcSent);
        public int Replies => Sum(b => b.Replies);

        /// <summary>STAT 回報的合計；問不到或不可信時為 null。</summary>
        public int? ArduinoReceived { get; internal set; }
        public int? ArduinoExecuted { get; internal set; }
        public int? ArduinoBad { get; internal set; }

        private int Sum(System.Func<SegmentRun, int> value)
        {
            var total = 0;
            foreach (var b in Blocks) total += value(b);
            return total;
        }
    }
}
