using System;

namespace StressTest.Core
{
    /// <summary>一輪測試的條件與流程參數。</summary>
    public sealed class RoundConfig
    {
        public RoundConfig(int gapMs, int totalCount, int segmentSize = 20, double baseSpeed = 2.0, double bootSpeed = 1.0)
        {
            GapMs = gapMs;
            TotalCount = totalCount;
            SegmentSize = segmentSize;
            BaseSpeed = baseSpeed;
            BootSpeed = bootSpeed;
        }

        public int GapMs { get; }
        public int TotalCount { get; }
        public int SegmentSize { get; }

        /// <summary>
        /// 計分段的起點速度（km/h）。預設 2.0：與最低速度 1.0 保留 10 筆的餘裕，
        /// 上一段漏掉幾筆加速時，減速段才不會先撞到底。
        /// </summary>
        public double BaseSpeed { get; }

        /// <summary>跑步機開機速度（km/h）。XTERRA DT168 開機即為 1.0，也是最低速度。</summary>
        public double BootSpeed { get; }

        /// <summary>預熱加速次數：從開機速度帶到起點速度。</summary>
        public int WarmupCount => (int)Math.Round((BaseSpeed - BootSpeed) / SegmentRun.SpeedStep);

        public double StartCountdownSeconds { get; set; } = 5.0;
        public double WarmupIntervalSeconds { get; set; } = 1.0;

        /// <summary>單發指令等待 ACK 的上限。RF 一筆約 340 ms，留足餘裕。</summary>
        public double AckTimeoutSeconds { get; set; } = 3.0;
    }
}
