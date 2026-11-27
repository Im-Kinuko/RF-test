using System;

namespace StressTest.Core
{
    /// <summary>一輪測試的條件與流程參數。</summary>
    public sealed class RoundConfig
    {
        public const int DefaultSegmentSize = 20;

        /// <summary>
        /// A 送法的起點速度。整輪不停，掉包造成的偏移會累積，2.5 讓上下各留 15 格（每段 20 筆時）。
        /// </summary>
        public const double ContinuousBaseSpeed = 2.5;

        /// <summary>RUN 對照的起點速度：與最低速度 1.0 保留 10 格。</summary>
        public const double SegmentedBaseSpeed = 2.0;

        private readonly double? _baseSpeed;

        public RoundConfig(int gapMs, int totalCount, int segmentSize = DefaultSegmentSize, double? baseSpeed = null,
            double bootSpeed = 1.0)
        {
            GapMs = gapMs;
            TotalCount = totalCount;
            SegmentSize = segmentSize;
            _baseSpeed = baseSpeed;
            BootSpeed = bootSpeed;
        }

        /// <summary>每段筆數上限：起點加上一整段不超過跑步機最高速度。</summary>
        public static int MaxSegmentSizeFor(SendMode mode) =>
            (int)Math.Round((PanelSpeedInput.Max - DefaultBaseSpeed(mode)) / SegmentRun.SpeedStep);

        private static double DefaultBaseSpeed(SendMode mode) =>
            mode == SendMode.Ack ? ContinuousBaseSpeed : SegmentedBaseSpeed;

        /// <summary>A 送法整輪不停；RUN 對照每段停下來讀面板。</summary>
        public bool IsContinuous => Mode == SendMode.Ack;

        public int GapMs { get; }

        /// <summary>送法。正式實驗用 A；RUN 是對照組。</summary>
        public SendMode Mode { get; set; } = SendMode.Ack;

        /// <summary>一輪計分段預計送出的總筆數。</summary>
        public int TotalCount { get; }

        /// <summary>每段筆數；總筆數切成加減交替的多段。</summary>
        public int SegmentSize { get; }

        /// <summary>計分起點速度（km/h）；未指定時依送法決定。</summary>
        public double BaseSpeed => _baseSpeed ?? DefaultBaseSpeed(Mode);

        /// <summary>跑步機開機速度（km/h）。XTERRA DT168 開機即為 1.0，也是最低速度。</summary>
        public double BootSpeed { get; }

        /// <summary>預熱加速次數：開機速度到起點速度的格數。</summary>
        public int WarmupCount => (int)Math.Round((BaseSpeed - BootSpeed) / SegmentRun.SpeedStep);

        public double StartCountdownSeconds { get; set; } = 5.0;
        public double WarmupIntervalSeconds { get; set; } = 1.0;

        /// <summary>單發指令等待 ACK 的上限。RF 一筆約 340 ms，留足餘裕。</summary>
        public double AckTimeoutSeconds { get; set; } = 3.0;
    }
}
