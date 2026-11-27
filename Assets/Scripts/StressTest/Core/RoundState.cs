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
        /// <summary>A 送法：整輪送完，跑步機還在轉，等研究人員輸入最後速度。</summary>
        AwaitingFinalReading,
        ConfirmingFinalReading,
        /// <summary>A 送法：跑步機已停止，看錄影輸入各轉向點讀數。</summary>
        AwaitingTurnReading,
        /// <summary>A 送法：所有讀數輸入完，等研究人員核對各段結果。</summary>
        ConfirmingReadings,
        /// <summary>上一段逾時，等韌體交出遲到的 DONE 才開始下一段。</summary>
        WaitingForLateDone,
        /// <summary>緊急停止：已送 ABORT，等韌體回 ABORTED 再送停止。</summary>
        Aborting,
        Stopping,
        Completed,
        Failed
    }
}
