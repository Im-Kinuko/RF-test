using System;
using System.Globalization;

namespace StressTest.Core
{
    /// <summary>
    /// A 送法的一次傳送：ZERO →（CMD → 完成回覆 → 等 gap）× 總筆數 → STAT；換段（換方向）不停。
    /// 指令不重送，重送會改變次數；ZERO 與 STAT 不影響跑步機，沒有回覆時重問。
    /// </summary>
    internal sealed class PacedSender
    {
        private enum Phase { Zeroing, Resting, AwaitingReply, Querying, Finished }

        private const int MaxControlAttempts = 3;
        private const double ControlTimeoutSeconds = 1.0;
        /// <summary>
        /// 回報毀損後至少等這麼久再送下一筆：同一行壞掉可能回兩次錯誤，
        /// 太快送出會讓第二個錯誤算到下一筆。
        /// </summary>
        private const double RejectSettleSeconds = 0.05;
        private const double TimeEpsilon = 1e-9;

        private readonly ISerialLink _link;
        private readonly PacedRun _run;
        private readonly int _gapMs;
        private readonly double _replyTimeout;
        private long _sequence;

        private Phase _phase;
        private double _deadline;
        private double _sendAt;
        private int _controlAttempts;
        private int _consecutiveNoReply;
        private bool _countersZeroed;
        private int _blockIndex;
        private CommandRecord _current;

        public PacedSender(ISerialLink link, PacedRun run, int actionMs, long lastSequence)
        {
            _link = link;
            _run = run;
            _gapMs = run.Blocks[0].GapMs;
            _replyTimeout = actionMs * 2 / 1000.0 + 1.0;
            _sequence = lastSequence;
        }

        /// <summary>目前用到的最後一個序號。</summary>
        public long LastSequence => _sequence;

        public bool IsFinished => _phase == Phase.Finished;

        /// <summary>正在傳送（或最後傳送）的段。</summary>
        public SegmentRun CurrentBlock => _run.Blocks[_blockIndex];

        /// <summary>目前段與之後的段標記同一個狀況（這些段沒有完整跑完）。</summary>
        public void MarkRemaining(Action<SegmentRun> mark)
        {
            for (var i = _blockIndex; i < _run.Blocks.Count; i++) mark(_run.Blocks[i]);
        }

        public void Start(double now)
        {
            _phase = Phase.Zeroing;
            SendControl("ZERO", now, _replyTimeout);
        }

        /// <summary>處理韌體的一行；不屬於逐筆協定的行回傳 false。</summary>
        public bool HandleLine(string line, double now)
        {
            if (line == "ACK,ZERO")
            {
                if (_phase == Phase.Zeroing)
                {
                    _countersZeroed = true;
                    RestUntil(now);
                }
                return true;
            }

            if (line.StartsWith("ERR,"))
            {
                Reject(now);
                return false; // 仍交給 session 記錄 LastError
            }

            var head = line.Split(',')[0];
            if (head != "X" && head != "STAT" && head != "NAK") return false;
            if (!LineChecksum.TryUnwrap(line, out var payload)) return true; // 回覆毀損：當作沒收到

            var fields = payload.Split(',');
            if (head == "NAK") Reject(now);
            else if (head == "X") Complete(fields, now);
            else Count(fields);
            return true;
        }

        /// <summary>處理到期的等待與該送的下一行。</summary>
        public void Advance(double now)
        {
            switch (_phase)
            {
                case Phase.Zeroing:
                    if (now <= _deadline) return;
                    if (_controlAttempts < MaxControlAttempts) SendControl("ZERO", now, _replyTimeout);
                    else RestUntil(now); // 照常執行，但不採信計數
                    break;

                case Phase.Resting:
                    if (now < _sendAt - TimeEpsilon) return;
                    if (MoveToUnfinishedBlock()) SendCommand(now);
                    else BeginQuery(now);
                    break;

                case Phase.AwaitingReply:
                    if (now <= _deadline) return;
                    _current.Status = CommandStatus.NoReply;
                    if (++_consecutiveNoReply >= PacedRun.MaxConsecutiveNoReply)
                    {
                        MarkRemaining(block => block.TimedOut = true);
                        BeginQuery(now);
                    }
                    else
                    {
                        RestUntil(now);
                        Advance(now);
                    }
                    break;

                case Phase.Querying:
                    if (now <= _deadline) return;
                    if (_controlAttempts < MaxControlAttempts) SendControl("STAT", now, _replyTimeout);
                    else _phase = Phase.Finished;
                    break;
            }
        }

        /// <summary>目前這段送完就換下一段；全部送完回傳 false。換段不增加等待。</summary>
        private bool MoveToUnfinishedBlock()
        {
            while (CurrentBlock.PcSent >= CurrentBlock.Count)
            {
                if (_blockIndex + 1 >= _run.Blocks.Count) return false;
                _blockIndex++;
            }
            return true;
        }

        private void RestUntil(double time)
        {
            _phase = Phase.Resting;
            _sendAt = time;
        }

        private void SendControl(string line, double now, double timeout)
        {
            _controlAttempts++;
            _deadline = now + timeout;
            _link.WriteLine(line);
        }

        private void BeginQuery(double now)
        {
            _phase = Phase.Querying;
            _controlAttempts = 0;
            // Arduino 可能還在做遲到的那一筆，STAT 要等它做完才會回
            SendControl("STAT", now, ControlTimeoutSeconds + _replyTimeout);
        }

        private void SendCommand(double now)
        {
            var block = CurrentBlock;
            var record = new CommandRecord(++_sequence, block.PcSent, now);
            block.AddCommand(record);
            _current = record;
            _phase = Phase.AwaitingReply;
            _deadline = now + _replyTimeout;
            var direction = block.Direction == SegmentDirection.Up ? "UP" : "DOWN";
            _link.WriteLine(LineChecksum.Wrap(
                $"CMD,{record.Sequence.ToString(CultureInfo.InvariantCulture)},{direction}"));
        }

        private void Reject(double now)
        {
            if (_phase != Phase.AwaitingReply) return;
            _current.Status = CommandStatus.Rejected;
            _consecutiveNoReply = 0; // Arduino 還活著
            RestUntil(now + Math.Max(RejectSettleSeconds, _gapMs / 1000.0));
        }

        // X,<seq>,<rx_us>,<start_us>,<end_us>
        private void Complete(string[] fields, double now)
        {
            if (fields.Length != 5 || !long.TryParse(fields[1], out var sequence)
                || !long.TryParse(fields[2], out var rx) || !long.TryParse(fields[3], out var start)
                || !long.TryParse(fields[4], out var end))
            {
                return;
            }

            CommandRecord record = null;
            SegmentRun owner = null;
            foreach (var block in _run.Blocks)
            {
                foreach (var c in block.Commands)
                {
                    if (c.Sequence != sequence) continue;
                    record = c;
                    owner = block;
                }
            }
            if (record == null || record.PcReplySeconds != null) return; // 別次傳送的遲到回覆或重複

            var waitingForIt = _phase == Phase.AwaitingReply && record == _current;
            record.Status = waitingForIt ? CommandStatus.Done : CommandStatus.Late;
            record.PcReplySeconds = now;
            record.RxMicros = rx;
            record.StartMicros = start;
            record.EndMicros = end;
            owner.AddTimestamp(new CommandTimestamp(record.Position, start, end));
            owner.Replies++;

            if (!waitingForIt) return; // 遲到的回覆不算恢復正常
            _consecutiveNoReply = 0;
            RestUntil(now + _gapMs / 1000.0);
        }

        // STAT,<received>,<executed>,<bad>
        private void Count(string[] fields)
        {
            if (_phase != Phase.Querying) return;
            if (fields.Length != 4 || !int.TryParse(fields[1], out var received)
                || !int.TryParse(fields[2], out var executed) || !int.TryParse(fields[3], out var bad))
            {
                return;
            }

            // 計數比收到的回覆還少，代表中途歸零過（例如重開機但 READY 遺失），不能採信
            if (_countersZeroed && received >= _run.Replies && executed >= _run.Replies)
            {
                _run.ArduinoReceived = received;
                _run.ArduinoExecuted = executed;
                _run.ArduinoBad = bad;
                ShareAmongBlocks(received, executed, bad);
            }
            _phase = Phase.Finished;
        }

        /// <summary>
        /// STAT 只有合計。多段時只在能確定歸屬時分給各段：沒回覆的指令全都沒做，或全都做了。
        /// </summary>
        private void ShareAmongBlocks(int received, int executed, int bad)
        {
            if (_run.Blocks.Count == 1)
            {
                var only = _run.Blocks[0];
                only.ArduinoReceived = received;
                only.ArduinoExecuted = executed;
                only.ArduinoBad = bad;
                return;
            }

            var noReply = 0;
            var rejected = 0;
            foreach (var block in _run.Blocks)
            {
                noReply += block.CountWithStatus(CommandStatus.NoReply);
                rejected += block.CountWithStatus(CommandStatus.Rejected);
            }
            var replies = _run.Replies;

            int? Share(int total, SegmentRun block)
            {
                if (total == replies) return block.Replies;
                if (total == replies + noReply) return block.Replies + block.CountWithStatus(CommandStatus.NoReply);
                return null;
            }

            foreach (var block in _run.Blocks)
            {
                block.ArduinoReceived = Share(received, block);
                block.ArduinoExecuted = Share(executed, block);
                block.ArduinoBad = bad == rejected ? block.CountWithStatus(CommandStatus.Rejected) : (int?)null;
            }
        }
    }
}
