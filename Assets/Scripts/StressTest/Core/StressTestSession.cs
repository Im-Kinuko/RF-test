using System;
using System.Collections.Generic;

namespace StressTest.Core
{
    /// <summary>
    /// 壓力測試的核心：透過 ISerialLink 與測試韌體交談，維護連線狀態。
    /// 不依賴 UnityEngine；由介面層每格呼叫 Poll。A 送法執行中另有高頻執行緒呼叫 Poll，
    /// 所以會寫序列埠或改狀態的方法都以同一把鎖保護。
    /// </summary>
    public sealed class StressTestSession
    {
        private readonly ISerialLink _link;
        private readonly EntryType _expectedEntry;
        private double _handshakeDeadline;

        /// <summary>送出 HELLO 後等待 READY 的秒數。Nano 開埠重設約需 2 秒。</summary>
        public const double HandshakeTimeoutSeconds = 4.0;

        private const int DefaultActionMs = 500;
        private double _segmentDeadline;

        private readonly object _sync = new object();
        private PacedSender _paced;
        private long _lastSequence;
        private double _now;

        public StressTestSession(ISerialLink link, EntryType expectedEntry)
        {
            _link = link ?? throw new ArgumentNullException(nameof(link));
            _expectedEntry = expectedEntry;
        }

        public SessionState State { get; private set; } = SessionState.Disconnected;
        public FirmwareInfo Firmware { get; private set; }

        /// <summary>韌體最後一次 ACK 的原指令，例如 "ONE,UP"。</summary>
        public string LastAck { get; private set; }

        /// <summary>韌體已 ACK 的單發指令總數，用來確認每次單發都被執行。</summary>
        public int SingleAckCount { get; private set; }

        /// <summary>韌體最後一次 ERR 的原因；送出新指令時清除。</summary>
        public string LastError { get; private set; }

        public void BeginHandshake(double now)
        {
            lock (_sync)
            {
                State = SessionState.Handshaking;
                _handshakeDeadline = now + HandshakeTimeoutSeconds;
                _link.WriteLine("HELLO");
            }
        }

        /// <summary>僅在 Ready 狀態送出單發指令；其他狀態拒絕並回傳 false。</summary>
        public bool SendSingle(SingleCommand command)
        {
            lock (_sync)
            {
                if (State != SessionState.Ready) return false;

                LastError = null;
                _link.WriteLine("ONE," + ToProtocol(command));
                return true;
            }
        }

        public SegmentRun CurrentSegment { get; private set; }

        /// <summary>A 送法的一段正在傳送中；介面層此時應密集呼叫 Poll。</summary>
        public bool IsPacing
        {
            get { lock (_sync) return _paced != null; }
        }

        /// <summary>
        /// 上一段逾時後，韌體可能還在執行並稍後送出 T／DONE。
        /// 收到遲到的 DONE（或韌體重新開機的 READY）之前不可開始下一段，以免舊回報混入新段。
        /// </summary>
        public bool WaitingForLateDone { get; private set; }

        /// <summary>依送法開始一段；目前無法開始時回傳 false。</summary>
        public bool StartSegment(SendMode mode, SegmentDirection direction, int count, int gapMs, double startSpeed,
            double now) =>
            mode == SendMode.Ack
                ? RunPacedSegment(direction, count, gapMs, startSpeed, now)
                : RunSegment(direction, count, gapMs, startSpeed, now);

        /// <summary>
        /// 僅在 Ready 狀態要求韌體連續送出 count 筆；每筆動作完成後休息 gapMs 再送下一筆。
        /// </summary>
        public bool RunSegment(SegmentDirection direction, int count, int gapMs, double startSpeed, double now)
        {
            lock (_sync)
            {
                if (State != SessionState.Ready || WaitingForLateDone) return false;

                LastError = null;
                CurrentSegment = new SegmentRun(direction, count, gapMs, startSpeed);
                State = SessionState.RunningSegment;
                _segmentDeadline = now + SegmentTimeoutSeconds(count, gapMs);
                _link.WriteLine($"RUN,{(direction == SegmentDirection.Up ? "UP" : "DOWN")},{count},{gapMs}");
                return true;
            }
        }

        /// <summary>
        /// A 送法：僅在 Ready 狀態開始逐筆送出 count 筆；每筆收到完成回覆後等 gapMs 再送下一筆。
        /// 之後的每一筆由 Poll 送出，呼叫越頻繁，實際間隔越接近 gapMs。
        /// </summary>
        public bool RunPacedSegment(SegmentDirection direction, int count, int gapMs, double startSpeed, double now)
        {
            lock (_sync)
            {
                if (State != SessionState.Ready || WaitingForLateDone) return false;

                LastError = null;
                CurrentSegment = new SegmentRun(direction, count, gapMs, startSpeed, SendMode.Ack);
                State = SessionState.RunningSegment;
                _now = Math.Max(_now, now);
                _paced = new PacedSender(_link, CurrentSegment, ActionMs, _lastSequence);
                _paced.Start(_now);
                return true;
            }
        }

        /// <summary>
        /// 在 AwaitingReading 狀態輸入面板速度，完成本段並回到 Ready；其他狀態回傳 null。
        /// </summary>
        public SegmentResult SubmitReading(double panelSpeed)
        {
            lock (_sync)
            {
                if (State != SessionState.AwaitingReading) return null;

                State = SessionState.Ready;
                return new SegmentResult(CurrentSegment, panelSpeed);
            }
        }

        /// <summary>
        /// 要求韌體立即中止執行中的段；韌體回 ABORTED 後回到 Ready。
        /// 逾時後仍在等遲到 DONE 的段也會中止（韌體可能還在跑）。都不是時回傳 false。
        /// A 送法由電腦控制節奏：停止送出即可，立刻回到 Ready。
        /// </summary>
        public bool Abort()
        {
            lock (_sync)
            {
                if (_paced != null)
                {
                    CurrentSegment.Aborted = true;
                    CurrentSegment.ReportedCount = CurrentSegment.Replies;
                    EndPaced();
                    State = SessionState.Ready;
                    return true;
                }
                if (State != SessionState.RunningSegment && !WaitingForLateDone) return false;
                _link.WriteLine("ABORT");
                return true;
            }
        }

        /// <summary>放棄等待輸入的這一段（緊急停止時用），回到 Ready。</summary>
        public void DiscardSegment()
        {
            lock (_sync)
            {
                if (State == SessionState.AwaitingReading) State = SessionState.Ready;
            }
        }

        public void Poll(double now)
        {
            lock (_sync)
            {
                // 兩個執行緒各自取時間，晚進來的可能帶著比較舊的 now
                now = _now = Math.Max(_now, now);

                while (_link.TryReadLine(out var line))
                {
                    HandleLine(line, now);
                }

                if (State == SessionState.Handshaking && now > _handshakeDeadline)
                {
                    State = SessionState.HandshakeTimeout;
                }

                if (_paced != null)
                {
                    _paced.Advance(now);
                    if (_paced.IsFinished) FinishPaced();
                }
                else if (State == SessionState.RunningSegment && now > _segmentDeadline)
                {
                    CurrentSegment.TimedOut = true;
                    WaitingForLateDone = true;
                    State = SessionState.AwaitingReading;
                }
            }
        }

        private void FinishPaced()
        {
            CurrentSegment.ReportedCount = CurrentSegment.ArduinoExecuted ?? CurrentSegment.Replies;
            EndPaced();
            State = SessionState.AwaitingReading;
        }

        private void EndPaced()
        {
            _lastSequence = _paced.LastSequence;
            _paced = null;
        }

        /// <summary>
        /// 段逾時 = 應送筆數 ×（單筆動作時間 + 間隔）× 2 + 2 秒。
        /// 單筆動作時間取韌體回報的 action_ms，未回報時保守採 500 ms。
        /// </summary>
        private double SegmentTimeoutSeconds(int count, int gapMs) =>
            count * (ActionMs + gapMs) * 2 / 1000.0 + 2.0;

        /// <summary>韌體回報的單筆動作時間，未回報時保守採 500 ms。</summary>
        private int ActionMs =>
            Firmware != null && Firmware.Parameters.TryGetValue("action_ms", out var text)
            && int.TryParse(text, out var reported)
                ? reported
                : DefaultActionMs;

        private void HandleLine(string line, double now)
        {
            if (_paced != null && _paced.HandleLine(line, now)) return;

            var fields = line.Split(',');
            if (WaitingForLateDone && (fields[0] == "T" || fields[0] == "DONE" || fields[0] == "ABORTED"))
            {
                // 逾時那一段的遲到回報：丟棄，DONE／ABORTED 代表韌體已回到待命
                if (fields[0] != "T") WaitingForLateDone = false;
                return;
            }
            if (State == SessionState.RunningSegment)
            {
                if (fields[0] == "T" && fields.Length == 4
                    && int.TryParse(fields[1], out var index)
                    && long.TryParse(fields[2], out var start)
                    && long.TryParse(fields[3], out var end))
                {
                    CurrentSegment.AddTimestamp(new CommandTimestamp(index, start, end));
                    return;
                }
                if (fields[0] == "DONE" && fields.Length == 2 && int.TryParse(fields[1], out var sent))
                {
                    CurrentSegment.ReportedCount = sent;
                    State = SessionState.AwaitingReading;
                    return;
                }
                if (fields[0] == "ABORTED" && fields.Length == 2 && int.TryParse(fields[1], out var sentBeforeAbort))
                {
                    CurrentSegment.ReportedCount = sentBeforeAbort;
                    CurrentSegment.Aborted = true;
                    State = SessionState.Ready;
                    return;
                }
            }
            if (fields[0] == "ACK" && fields.Length >= 2)
            {
                LastAck = line.Substring("ACK,".Length);
                if (fields[1] == "ONE") SingleAckCount++;
                return;
            }
            if (fields[0] == "ERR")
            {
                LastError = fields.Length >= 2 ? line.Substring("ERR,".Length) : "UNKNOWN";
                return;
            }
            if (fields[0] == "READY" && fields.Length >= 3 && TryParseEntry(fields[1], out var entry))
            {
                var parameters = new Dictionary<string, string>();
                if (fields.Length >= 4)
                {
                    foreach (var pair in fields[3].Split(';'))
                    {
                        var kv = pair.Split('=');
                        if (kv.Length == 2) parameters[kv[0]] = kv[1];
                    }
                }
                Firmware = new FirmwareInfo(entry, fields[2], parameters);
                WaitingForLateDone = false; // 韌體重新開機，不會再有舊段的回報
                if (State == SessionState.RunningSegment && entry == _expectedEntry)
                {
                    // 段執行中重開機：這一段到此為止，仍讓研究人員記下面板讀數
                    CurrentSegment.Rebooted = true;
                    if (_paced != null)
                    {
                        CurrentSegment.ReportedCount = CurrentSegment.Replies;
                        EndPaced();
                    }
                    State = SessionState.AwaitingReading;
                    return;
                }
                if (State == SessionState.AwaitingReading && entry == _expectedEntry)
                {
                    // 等讀數時重開機：段的資料已經收齊，保留讓研究人員輸入，並留下標記
                    CurrentSegment.Rebooted = true;
                    return;
                }
                if (_paced != null) EndPaced();
                State = entry == _expectedEntry ? SessionState.Ready : SessionState.EntryMismatch;
            }
        }

        private static string ToProtocol(SingleCommand command)
        {
            switch (command)
            {
                case SingleCommand.Start: return "START";
                case SingleCommand.Up: return "UP";
                case SingleCommand.Down: return "DOWN";
                case SingleCommand.Stop: return "STOP";
                default: throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }
        }

        private static bool TryParseEntry(string text, out EntryType entry)
        {
            switch (text)
            {
                case "OPTO": entry = EntryType.Opto; return true;
                case "RF": entry = EntryType.Rf; return true;
                default: entry = default; return false;
            }
        }
    }
}
