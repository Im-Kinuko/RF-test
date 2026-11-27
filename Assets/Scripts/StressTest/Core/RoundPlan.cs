using System;
using System.Collections.Generic;

namespace StressTest.Core
{
    /// <summary>一輪中預計的一段：方向、筆數與理論起訖速度。</summary>
    public sealed class PlannedSegment
    {
        public PlannedSegment(int index, SegmentDirection direction, int count, double expectedStartSpeed)
        {
            Index = index;
            Direction = direction;
            Count = count;
            ExpectedStartSpeed = expectedStartSpeed;
        }

        public int Index { get; }
        public SegmentDirection Direction { get; }
        public int Count { get; }
        public double ExpectedStartSpeed { get; }
        public double ExpectedEndSpeed =>
            ExpectedStartSpeed + (Direction == SegmentDirection.Up ? 1 : -1) * Count * SegmentRun.SpeedStep;
    }

    /// <summary>
    /// 每組一段加速 n 筆、一段減速 n 筆，重複 sets 組；速度在 baseSpeed ↔ baseSpeed + n × 0.1 之間來回。
    /// </summary>
    public sealed class RoundPlan
    {
        private RoundPlan(IReadOnlyList<PlannedSegment> segments)
        {
            Segments = segments;
        }

        public IReadOnlyList<PlannedSegment> Segments { get; }

        /// <summary>計分段預計送出的加速次數。</summary>
        public int UpCount => CountOf(SegmentDirection.Up);

        /// <summary>計分段預計送出的減速次數。</summary>
        public int DownCount => CountOf(SegmentDirection.Down);

        private int CountOf(SegmentDirection direction)
        {
            var total = 0;
            foreach (var s in Segments) if (s.Direction == direction) total += s.Count;
            return total;
        }

        public static RoundPlan Create(int n, int sets, double baseSpeed)
        {
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
            if (sets < 1) throw new ArgumentOutOfRangeException(nameof(sets));

            var segments = new List<PlannedSegment>();
            var speed = baseSpeed;

            for (var i = 0; i < sets * 2; i++)
            {
                var direction = i % 2 == 0 ? SegmentDirection.Up : SegmentDirection.Down;
                var segment = new PlannedSegment(i, direction, n, speed);
                segments.Add(segment);
                speed = segment.ExpectedEndSpeed;
            }

            return new RoundPlan(segments);
        }
    }
}
