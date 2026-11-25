using System.Linq;
using StressTest.Core;

namespace StressTest.Tests
{
    /// <summary>建立已連線的 session 與一輪測試，並以固定時間步推進。</summary>
    public sealed class RoundHarness
    {
        /// <param name="bootSpeed">跑步機開機速度；null 時用 RoundConfig 預設值（1.0，不需預熱）。</param>
        public RoundHarness(int gapMs = 5, int totalCount = 40, FakeArduino arduino = null, RoundCsvLog log = null,
            double? bootSpeed = null)
        {
            Arduino = arduino ?? new FakeArduino();
            Session = new StressTestSession(Arduino, EntryType.Opto);
            Session.BeginHandshake(0.0);
            Session.Poll(0.1);
            var config = bootSpeed.HasValue
                ? new RoundConfig(gapMs, totalCount, bootSpeed: bootSpeed.Value)
                : new RoundConfig(gapMs, totalCount);
            Round = new TestRound(Session, config, "R1", log);
            Now = 1.0;
        }

        public FakeArduino Arduino { get; }
        public StressTestSession Session { get; }
        public TestRound Round { get; }
        public double Now { get; private set; }

        public void Start() => Round.Start(Now);

        /// <summary>每 0.1 秒推進一次，直到狀態符合或超過 maxSeconds。</summary>
        public void AdvanceUntil(RoundState state, double maxSeconds = 120)
        {
            var end = Now + maxSeconds;
            while (Round.State != state && Now < end)
            {
                Now += 0.1;
                Round.Tick(Now);
            }
        }

        public int Sent(string line) => Arduino.Received.Count(l => l == line);

        /// <summary>從開始一路推進到輸入預熱讀數並確認為 1.0。</summary>
        public void StartAndWarmUp()
        {
            Start();
            AdvanceUntil(RoundState.AwaitingWarmupReading);
            Round.EnterReading(1.0);
            Round.Confirm(Now);
        }

        /// <summary>完成目前這段：等韌體回報、輸入讀數並確認。</summary>
        public SegmentResult CompleteSegment(double panelReading)
        {
            AdvanceUntil(RoundState.AwaitingSegmentReading);
            Round.EnterReading(panelReading);
            var result = Round.PendingResult;
            Round.Confirm(Now);
            return result;
        }
    }
}
