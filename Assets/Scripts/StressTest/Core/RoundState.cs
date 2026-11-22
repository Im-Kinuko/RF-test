namespace StressTest.Core
{
    public enum RoundState
    {
        Idle,
        /// <summary>已送啟動，等待 ACK 並倒數讓跑帶穩定。</summary>
        Starting,
        /// <summary>以固定間隔單發加速，把速度從開機速度帶到起點。</summary>
        WarmingUp,
        AwaitingWarmupReading,
        ConfirmingWarmupReading,
        /// <summary>預熱讀數與起點不符，等研究人員選擇重新預熱或以實際速度繼續。</summary>
        WarmupMismatch,
        RunningSegment,
        AwaitingSegmentReading,
        ConfirmingSegmentReading,
        /// <summary>上一段逾時，等韌體交出遲到的 DONE 才開始下一段。</summary>
        WaitingForLateDone,
        Stopping,
        Completed,
        Failed
    }
}
