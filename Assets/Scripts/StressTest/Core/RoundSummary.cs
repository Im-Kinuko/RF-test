using System;
using System.Collections.Generic;
using System.Linq;

namespace StressTest.Core
{
    /// <summary>一輪結束後的彙總，對應每輪 CSV 的一列。</summary>
    public sealed class RoundSummary
    {
        public RoundSummary(IReadOnlyList<SegmentResult> results, double? warmupReading, RoundPlan plan,
            int warmupUpSent, int warmupDownSent, PacedRun run = null)
        {
            _run = run;
            Results = results;
            WarmupReading = warmupReading;
            UpPlanned = plan.UpCount;
            DownPlanned = plan.DownCount;
            WarmupUpSent = warmupUpSent;
            WarmupDownSent = warmupDownSent;

            // 讀不到轉向點而沒有各段結果時，實際間隔仍可從傳送紀錄算出
            var segments = run != null ? run.Blocks : results.Select(r => r.Segment);
            var gaps = segments.SelectMany(s => s.Gaps).ToList();
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
        public bool AnyReportShort => _run != null
            ? _run.Blocks.Any(b => b.ReportShort)
            : Results.Any(r => r.Segment.ReportShort);
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

        /// <summary>A 送法各檢查點的整輪合計；沒有任何段、或任一段不明時為 null。</summary>
        public int? PcSent => _run != null
            ? _run.PcSent
            : Total(r => r.Segment.Mode == SendMode.Ack ? r.Segment.PcSent : (int?)null);
        public int? ArduinoReceived => _run != null ? _run.ArduinoReceived : Total(r => r.Segment.ArduinoReceived);
        public int? ArduinoExecuted => _run != null ? _run.ArduinoExecuted : Total(r => r.Segment.ArduinoExecuted);
        public int? ArduinoBad => _run != null ? _run.ArduinoBad : Total(r => r.Segment.ArduinoBad);
        public int? LinkLost => _run != null ? _run.PcSent - _run.ArduinoReceived : Total(r => r.LinkLost);
        public int? TreadmillMissing => _run == null
            ? Total(r => r.TreadmillMissing)
            : Results.Count == 0 ? null : _run.ArduinoExecuted - Results.Sum(r => r.PanelSteps);
        public bool AnyRebooted => _run != null ? _run.AnyRebooted : Results.Any(r => r.Segment.Rebooted);

        private readonly PacedRun _run;

        private int? Total(Func<SegmentResult, int?> value)
        {
            if (Results.Count == 0) return null;
            var total = 0;
            foreach (var r in Results)
            {
                var v = value(r);
                if (v == null) return null;
                total += v.Value;
            }
            return total;
        }

        internal static int SentCount(IEnumerable<SegmentResult> results, SegmentDirection direction) =>
            results.Where(r => r.Segment.Direction == direction).Sum(r => r.Segment.ReportedCount ?? 0);
    }
}
