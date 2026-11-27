using System.Linq;
using StressTest.Core;

namespace StressTest.Tests
{
    /// <summary>建立已連線的 session 與一輪測試，並以固定時間步推進。</summary>
    public sealed class RoundHarness
    {
        /// <param name="baseSpeed">計分起點；流程測試固定用 1.0（各段 1.0↔3.0），與正式預設值無關。</param>
        /// <param name="bootSpeed">跑步機開機速度；預設與起點相同（不需預熱）。</param>
        /// <param name="n">每段筆數；流程測試固定用 20，與正式的 30／60／90 無關。</param>
        /// <param name="sets">組數；預設 1 組（加速、減速各一段）讓流程測試短一點。</param>
        public RoundHarness(int gapMs = 5, int n = 20, int sets = 1, FakeArduino arduino = null, RoundCsvLog log = null,
            double? bootSpeed = null, double baseSpeed = 1.0)
        {
            Arduino = arduino ?? new FakeArduino();
            Session = new StressTestSession(Arduino, EntryType.Opto);
            Session.BeginHandshake(0.0);
            Session.Poll(0.1);
            var config = new RoundConfig(gapMs, n, sets, baseSpeed, bootSpeed ?? baseSpeed)
            {
                IgnoredWarmupPresses = 0 // 多補的那一下另有專門測試
            };
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
