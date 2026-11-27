namespace StressTest.Core
{
    public enum CommandStatus
    {
        /// <summary>已送出，還在等完成回覆。</summary>
        Pending,
        /// <summary>期限內收到完成回覆。</summary>
        Done,
        /// <summary>逾時之後才收到完成回覆。</summary>
        Late,
        /// <summary>Arduino 回報收到的行毀損，沒有執行。</summary>
        Rejected,
        /// <summary>沒有收到完成回覆；是否執行要看 Arduino 的計數。</summary>
        NoReply
    }

    /// <summary>A 送法中一筆指令在各檢查點的紀錄。電腦端時間是呼叫端時鐘的秒數。</summary>
    public sealed class CommandRecord
    {
        public CommandRecord(long sequence, int position, double pcSendSeconds)
        {
            Sequence = sequence;
            Position = position;
            PcSendSeconds = pcSendSeconds;
        }

        /// <summary>協定上的序號，連線期間一直遞增。</summary>
        public long Sequence { get; }
        /// <summary>段內第幾筆，從 0 起算。</summary>
        public int Position { get; }
        public CommandStatus Status { get; internal set; } = CommandStatus.Pending;

        public double PcSendSeconds { get; }
        public double? PcReplySeconds { get; internal set; }

        /// <summary>Arduino 收到、動作開始、動作結束的 micros()；沒有回覆時為 null。</summary>
        public long? RxMicros { get; internal set; }
        public long? StartMicros { get; internal set; }
        public long? EndMicros { get; internal set; }
    }
}
