using System.Collections.Generic;
using System.Linq;

namespace StressTest.Core
{
    /// <summary>一輪結束後的彙總，對應每輪 CSV 的一列。</summary>
    public sealed class RoundSummary
    {
        public RoundSummary(IReadOnlyList<SegmentResult> results, double? warmupReading)
        {
            Results = results;
            WarmupReading = warmupReading;

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
    }
}
