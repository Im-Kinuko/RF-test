using System.Collections.Generic;
using StressTest.Core;

namespace StressTest.Tests
{
    /// <summary>
    /// 以 C# 模擬測試韌體的序列協定，接在 ISerialLink 接縫上。
    /// </summary>
    public sealed class FakeArduino : ISerialLink
    {
        private readonly Queue<string> _outgoing = new Queue<string>();

        public FakeArduino(string entry = "OPTO", string version = "0.1.0", string parameters = "press_ms=120;action_ms=120;max_run=90;ack=1")
        {
            Entry = entry;
            Version = version;
            Parameters = parameters;
        }

        public string Entry { get; set; }
        public string Version { get; set; }
        public string Parameters { get; set; }
        public bool RespondToHello { get; set; } = true;
        /// <summary>非 null 時，所有 ONE 指令都回 ERR,&lt;此值&gt;。</summary>
        public string ErrorReply { get; set; }

        /// <summary>RUN 時單筆動作耗時（µs），用來產生時間戳。</summary>
        public long ActionMicros { get; set; } = 120_000;
        /// <summary>RUN 時實際多出的間隔誤差（µs）。</summary>
        public long GapJitterMicros { get; set; } = 8;
        /// <summary>非 null 時，RUN 只回報這麼多筆（模擬遺漏）。</summary>
        public int? ReportOnly { get; set; }
        /// <summary>true 時 RUN 只回 ACK，不送時間戳與 DONE（模擬卡住）。</summary>
        public bool HangOnRun { get; set; }
        /// <summary>RUN 第一筆的 micros() 值，用來模擬計數器溢位。</summary>
        public long StartMicros { get; set; } = 1_000_000;

        private const long MicrosRange = 1L << 32;

        /// <summary>false 時收到 ABORT 不回應（模擬韌體卡死）。</summary>
        public bool RespondToAbort { get; set; } = true;
        /// <summary>卡住的 RUN 被中止時回報已送出的筆數。</summary>
        public int AbortReportCount { get; set; } = 3;
        private bool _hanging;

        // ===== A 送法（CMD／ZERO／STAT）。集合裡放「第幾筆 CMD」，從 1 起算 =====
        /// <summary>這幾筆在序列線上遺失：韌體沒收到。</summary>
        public HashSet<int> LoseCommands { get; } = new HashSet<int>();
        /// <summary>這幾筆到達時內容毀損：韌體回 NAK。</summary>
        public HashSet<int> CorruptCommands { get; } = new HashSet<int>();
        /// <summary>這幾筆有執行，但完成回覆遺失。</summary>
        public HashSet<int> LoseReplies { get; } = new HashSet<int>();
        /// <summary>這幾筆的完成回覆扣住，等 ReleaseDelayedReplies() 才送出。</summary>
        public HashSet<int> DelayReplies { get; } = new HashSet<int>();
        /// <summary>接下來要漏掉幾次 STAT／ZERO 的回覆。</summary>
        public int LoseStatReplies { get; set; }
        public int LoseZeroReplies { get; set; }

        private readonly List<string> _delayed = new List<string>();
        private int _commandOrdinal, _received, _executed, _bad;
        private long _clockMicros = 999_990;

        /// <summary>模擬重新開機但 READY 在路上遺失：只有計數歸零。</summary>
        public void ResetCounters() => _received = _executed = _bad = 0;

        public void ReleaseOldestDelayedReply()
        {
            Emit(_delayed[0]);
            _delayed.RemoveAt(0);
        }

        public void ReleaseDelayedReplies()
        {
            foreach (var line in _delayed) Emit(line);
            _delayed.Clear();
        }

        public List<string> Received { get; } = new List<string>();
        public bool IsOpen { get; set; } = true;

        public void WriteLine(string line)
        {
            Received.Add(line);

            if (line == "HELLO")
            {
                if (RespondToHello) Boot();
            }
            else if (line.StartsWith("ONE,"))
            {
                Emit(ErrorReply != null ? "ERR," + ErrorReply : "ACK," + line);
            }
            else if (line == "ZERO")
            {
                _received = _executed = _bad = 0;
                if (LoseZeroReplies > 0) LoseZeroReplies--;
                else Emit("ACK,ZERO");
            }
            else if (line == "STAT")
            {
                if (LoseStatReplies > 0) LoseStatReplies--;
                else Emit(Sign($"STAT,{_received},{_executed},{_bad}"));
            }
            else if (line.StartsWith("CMD,"))
            {
                Command(line);
            }
            else if (line.StartsWith("RUN,"))
            {
                Run(line);
            }
            else if (line == "ABORT")
            {
                if (!RespondToAbort) return;
                Emit(_hanging ? $"ABORTED,{AbortReportCount}" : "ABORTED,0");
                _hanging = false;
            }
            else
            {
                Emit("ERR,UNKNOWN");
            }
        }

        private void Command(string line)
        {
            var ordinal = ++_commandOrdinal;
            if (LoseCommands.Contains(ordinal)) return;

            var star = line.LastIndexOf('*');
            if (CorruptCommands.Contains(ordinal) || star < 0 || Sign(line.Substring(0, star)) != line)
            {
                _bad++;
                Emit(Sign("NAK,BAD_LINE"));
                return;
            }

            _received++;
            _executed++;
            var seq = line.Split(',')[1];
            // 第一筆在 1000000 µs 開始；之後每筆相隔 gap 誤差 5000 + GapJitterMicros
            var rx = _clockMicros;
            var start = rx + 10;
            var end = start + ActionMicros;
            _clockMicros = end + 5000 + GapJitterMicros - 10;

            var reply = Sign($"X,{seq},{rx % MicrosRange},{start % MicrosRange},{end % MicrosRange}");
            if (LoseReplies.Contains(ordinal)) return;
            if (DelayReplies.Contains(ordinal)) _delayed.Add(reply);
            else Emit(reply);
        }

        /// <summary>假韌體自己的 XOR 檢查碼，刻意不重用受測程式。</summary>
        private static string Sign(string payload)
        {
            var x = 0;
            foreach (var ch in payload) x ^= ch;
            return payload + "*" + x.ToString("X2");
        }

        private void Run(string line)
        {
            var fields = line.Split(',');
            var count = int.Parse(fields[2]);
            var gapMs = int.Parse(fields[3]);

            Emit("ACK," + line);
            if (HangOnRun)
            {
                _hanging = true;
                return;
            }

            var sent = ReportOnly ?? count;
            var t = StartMicros;
            for (var i = 0; i < sent; i++)
            {
                var start = t;
                var end = start + ActionMicros;
                // micros() 是 32 位元計數器，約 71.6 分鐘溢位歸零
                Emit($"T,{i},{start % MicrosRange},{end % MicrosRange}");
                t = end + gapMs * 1000L + GapJitterMicros;
            }
            Emit($"DONE,{sent}");
        }

        public bool TryReadLine(out string line)
        {
            if (_outgoing.Count > 0)
            {
                line = _outgoing.Dequeue();
                return true;
            }
            line = null;
            return false;
        }

        /// <summary>模擬開機（Nano 在開埠時重設）主動送出 READY。</summary>
        public void Boot() => Emit($"READY,{Entry},{Version},{Parameters}");

        public void Emit(string line) => _outgoing.Enqueue(line);
    }
}
