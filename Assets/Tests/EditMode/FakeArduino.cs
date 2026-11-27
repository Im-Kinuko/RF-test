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

        public FakeArduino(string entry = "OPTO", string version = "0.1.0", string parameters = "press_ms=120;action_ms=120;max_run=90")
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
