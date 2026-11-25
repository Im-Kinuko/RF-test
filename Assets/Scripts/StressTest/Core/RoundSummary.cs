using System.Collections.Generic;
using System.Linq;

namespace StressTest.Core
{
    /// <summary>一輪結束後的彙總，對應每輪 CSV 的一列。</summary>
    public sealed class RoundSummary
    {
        public RoundSummary(IReadOnlyList<SegmentResult> results, double? warmupReading, RoundPlan plan,
            int warmupUpSent, int warmupDownSent)
        {
            Results = results;
            WarmupReading = warmupReading;
            UpPlanned = plan.UpCount;
            DownPlanned = plan.DownCount;
            WarmupUpSent = warmupUpSent;
            WarmupDownSent = warmupDownSent;

            var gaps = results.SelectMany(r => r.Segment.Gaps).ToList();
            MeanGapMicros = gaps.Count > 0 ? gaps.Average() : 0;
            MaxGapMicros = gaps.Count > 0 ? gaps.Max() : 0;
        }

        public IReadOnlyList<SegmentResult> Results { get; }
        public int SegmentCount => Results.Count;
        public int TotalSent => Results.Sum(r => r.Segment.Count);
        public int TotalDrops => Results.Sum(r => r.Drops);
        public double? WarmupReading { get; }
        public double? EndReading => Results.Count > 0 ? Results[Results.Count - 1].ActualEndSpeed : (double?)null;
        public bool AnyPossiblyClamped => Results.Any(r => r.PossiblyClamped);
        public bool AnyReportShort => Results.Any(r => r.Segment.ReportShort);
        public double MeanGapMicros { get; }
        public long MaxGapMicros { get; }

        /// <summary>計分段預計的加速／減速次數（整輪計畫，含未完成的段）。</summary>
        public int UpPlanned { get; }
        public int DownPlanned { get; }

        /// <summary>已確認的計分段中，Arduino 回報實際送出的加速／減速次數。</summary>
        public int UpSent => SentCount(Results, SegmentDirection.Up);
        public int DownSent => SentCount(Results, SegmentDirection.Down);

        /// <summary>預熱與重新預熱送出的單發加速／減速次數（不計分）。</summary>
        public int WarmupUpSent { get; }
        public int WarmupDownSent { get; }

        internal static int SentCount(IEnumerable<SegmentResult> results, SegmentDirection direction) =>
            results.Where(r => r.Segment.Direction == direction).Sum(r => r.Segment.ReportedCount ?? 0);
    }
}
