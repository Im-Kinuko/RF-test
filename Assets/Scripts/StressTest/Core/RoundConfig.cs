using System;

namespace StressTest.Core
{
    /// <summary>一輪測試的條件與流程參數。</summary>
    public sealed class RoundConfig
    {
        /// <summary>正式實驗每輪的組數：每組是一段加速 n 筆加一段減速 n 筆。</summary>
        public const int DefaultSets = 2;

        public RoundConfig(int gapMs, int n, int sets = DefaultSets, double baseSpeed = 2.0, double bootSpeed = 1.0)
        {
            GapMs = gapMs;
            N = n;
            Sets = sets;
            BaseSpeed = baseSpeed;
            BootSpeed = bootSpeed;
        }

        public int GapMs { get; }

        /// <summary>每段連續送出的筆數；加速段與減速段相同。</summary>
        public int N { get; }

        /// <summary>組數：加速 n、減速 n 為一組。</summary>
        public int Sets { get; }

        /// <summary>一輪計分段預計送出的總筆數＝2 × 組數 × n。</summary>
        public int TotalCount => 2 * Sets * N;

        /// <summary>
        /// 計分段的起點速度（km/h）。預設 2.0：與最低速度 1.0 保留 10 筆的餘裕，
        /// 上一段漏掉幾筆加速時，減速段才不會先撞到底。
        /// </summary>
        public double BaseSpeed { get; }

        /// <summary>跑步機開機速度（km/h）。XTERRA DT168 開機即為 1.0，也是最低速度。</summary>
        public double BootSpeed { get; }

        /// <summary>
        /// 啟動後跑步機不會反應的預熱按壓次數。實機上啟動後的 10 次加速只走到 1.9，
        /// 少了 1 次，所以預設多補 1 次。
        /// </summary>
        public int IgnoredWarmupPresses { get; set; } = 1;

        /// <summary>預熱加速次數：開機速度到起點速度的格數，再加上跑步機不反應的次數。不需預熱時為 0。</summary>
        public int WarmupCount
        {
            get
            {
                var steps = (int)Math.Round((BaseSpeed - BootSpeed) / SegmentRun.SpeedStep);
                return steps > 0 ? steps + IgnoredWarmupPresses : 0;
            }
        }

        public double StartCountdownSeconds { get; set; } = 5.0;
        public double WarmupIntervalSeconds { get; set; } = 1.0;

        /// <summary>單發指令等待 ACK 的上限。RF 一筆約 340 ms，留足餘裕。</summary>
        public double AckTimeoutSeconds { get; set; } = 3.0;
    }
}
