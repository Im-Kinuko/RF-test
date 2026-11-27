using System;
using System.Globalization;

namespace StressTest.Core
{
    /// <summary>
    /// A 送法的一段：ZERO →（CMD → 完成回覆 → 等 gap）× n → STAT。
    /// 指令不重送，重送會改變次數；ZERO 與 STAT 不影響跑步機，沒有回覆時重問。
    /// </summary>
    internal sealed class PacedSender
    {
        private enum Phase { Zeroing, Resting, AwaitingReply, Querying, Finished }

        /// <summary>連續這麼多筆沒有回覆就結束這一段。</summary>
        public const int MaxConsecutiveNoReply = 3;
        private const int MaxControlAttempts = 3;
        private const double ControlTimeoutSeconds = 1.0;
        /// <summary>
        /// 回報毀損後至少等這麼久再送下一筆：同一行壞掉可能回兩次錯誤，
        /// 太快送出會讓第二個錯誤算到下一筆。
        /// </summary>
        private const double RejectSettleSeconds = 0.05;
        private const double TimeEpsilon = 1e-9;

        private readonly ISerialLink _link;
        private readonly SegmentRun _segment;
        private readonly double _replyTimeout;
        private long _sequence;

        private Phase _phase;
        private double _deadline;
        private double _sendAt;
        private int _controlAttempts;
        private int _consecutiveNoReply;
        private bool _countersZeroed;

        public PacedSender(ISerialLink link, SegmentRun segment, int actionMs, long lastSequence)
        {
            _link = link;
            _segment = segment;
            _replyTimeout = actionMs * 2 / 1000.0 + 1.0;
            _sequence = lastSequence;
        }

        /// <summary>目前用到的最後一個序號。</summary>
        public long LastSequence => _sequence;

        public bool IsFinished => _phase == Phase.Finished;

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
                    if (_segment.PcSent < _segment.Count) SendCommand(now);
                    else BeginQuery(now);
                    break;

                case Phase.AwaitingReply:
                    if (now <= _deadline) return;
                    Current.Status = CommandStatus.NoReply;
                    if (++_consecutiveNoReply >= MaxConsecutiveNoReply)
                    {
                        _segment.TimedOut = true;
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

        private CommandRecord Current => _segment.Commands[_segment.Commands.Count - 1];

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
            var record = new CommandRecord(++_sequence, _segment.PcSent, now);
            _segment.AddCommand(record);
            _phase = Phase.AwaitingReply;
            _deadline = now + _replyTimeout;
            var direction = _segment.Direction == SegmentDirection.Up ? "UP" : "DOWN";
            _link.WriteLine(LineChecksum.Wrap(
                $"CMD,{record.Sequence.ToString(CultureInfo.InvariantCulture)},{direction}"));
        }

        private void Reject(double now)
        {
            if (_phase != Phase.AwaitingReply) return;
            Current.Status = CommandStatus.Rejected;
            _consecutiveNoReply = 0; // Arduino 還活著
            RestUntil(now + Math.Max(RejectSettleSeconds, _segment.GapMs / 1000.0));
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
            foreach (var c in _segment.Commands)
            {
                if (c.Sequence == sequence) record = c;
            }
            if (record == null || record.PcReplySeconds != null) return; // 別段的遲到回覆或重複

            var waitingForIt = _phase == Phase.AwaitingReply && record == Current;
            record.Status = waitingForIt ? CommandStatus.Done : CommandStatus.Late;
            record.PcReplySeconds = now;
            record.RxMicros = rx;
            record.StartMicros = start;
            record.EndMicros = end;
            _segment.AddTimestamp(new CommandTimestamp(record.Position, start, end));
            _segment.Replies++;

            if (!waitingForIt) return; // 遲到的回覆不算恢復正常
            _consecutiveNoReply = 0;
            RestUntil(now + _segment.GapMs / 1000.0);
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
            if (_countersZeroed && received >= _segment.Replies && executed >= _segment.Replies)
            {
                _segment.ArduinoReceived = received;
                _segment.ArduinoExecuted = executed;
                _segment.ArduinoBad = bad;
            }
            _phase = Phase.Finished;
        }
    }
}
