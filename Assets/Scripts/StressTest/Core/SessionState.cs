namespace StressTest.Core
{
    public enum SessionState
    {
        Disconnected,
        Handshaking,
        Ready,
        EntryMismatch,
        HandshakeTimeout,
        /// <summary>韌體正在執行一段 RUN，期間不輸出序列資料。</summary>
        RunningSegment,
        /// <summary>一段已結束，等待研究人員輸入面板速度。</summary>
        AwaitingReading
    }
}
