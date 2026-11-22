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
    /// 把總次數切成每段 segmentSize 筆、加減交替的多段，速度在 baseSpeed ↔ baseSpeed + segmentSize × 0.1 之間來回。
    /// </summary>
    public sealed class RoundPlan
    {
        private RoundPlan(IReadOnlyList<PlannedSegment> segments)
        {
            Segments = segments;
        }

        public IReadOnlyList<PlannedSegment> Segments { get; }

        public static RoundPlan Create(int totalCount, int segmentSize, double baseSpeed)
        {
            if (totalCount < 1) throw new ArgumentOutOfRangeException(nameof(totalCount));
            if (segmentSize < 1) throw new ArgumentOutOfRangeException(nameof(segmentSize));

            var segments = new List<PlannedSegment>();
            var remaining = totalCount;
            var speed = baseSpeed;
            var direction = SegmentDirection.Up;

            while (remaining > 0)
            {
                var count = Math.Min(segmentSize, remaining);
                var segment = new PlannedSegment(segments.Count, direction, count, speed);
                segments.Add(segment);

                speed = segment.ExpectedEndSpeed;
                remaining -= count;
                direction = direction == SegmentDirection.Up ? SegmentDirection.Down : SegmentDirection.Up;
            }

            return new RoundPlan(segments);
        }
    }
}
