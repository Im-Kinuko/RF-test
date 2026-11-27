using System.Collections.Generic;
using StressTest.Core;

namespace StressTest.Runtime
{
    /// <summary>
    /// 依測試韌體協定回應的模擬 Arduino，只供版面導覽（-stresstest-tour）截圖使用，
    /// 讓不接硬體也能走到連線後的每一個畫面。正式測試一律走 SerialPortLink。
    /// </summary>
    internal sealed class SimulatedArduino : ISerialLink
    {
        private readonly Queue<string> _outgoing = new Queue<string>();
        private long _micros = 1_000_000;
        private string _heldRun;

        public string Entry { get; set; } = "OPTO";
        public string Parameters { get; set; } = "press_ms=120;action_ms=120;max_run=90";
        public bool RespondToHello { get; set; } = true;
        public bool RespondToAbort { get; set; } = true;

        /// <summary>true 時 RUN 只回 ACK，等 ReleaseRun() 才送出時間戳與 DONE（用來停在「執行中」畫面）。</summary>
        public bool HoldRun { get; set; }

        /// <summary>非 null 時 RUN 只回報這麼多筆（模擬回報不足）。</summary>
        public int? ReportOnly { get; set; }

        public bool IsOpen => true;

        public void WriteLine(string line)
        {
            if (line == "HELLO")
            {
                if (RespondToHello) _outgoing.Enqueue($"READY,{Entry},0.4.0,{Parameters}");
            }
            else if (line.StartsWith("ONE,"))
            {
                _outgoing.Enqueue("ACK," + line);
            }
            else if (line.StartsWith("RUN,"))
            {
                _outgoing.Enqueue("ACK," + line);
                if (HoldRun) _heldRun = line;
                else EmitRun(line, null);
            }
            else if (line == "ABORT")
            {
                if (!RespondToAbort) return;
                if (_heldRun != null) EmitRun(_heldRun, 3);
                else _outgoing.Enqueue("ABORTED,0");
                _heldRun = null;
            }
            else
            {
                _outgoing.Enqueue("ERR,UNKNOWN");
            }
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

        /// <summary>放行被 HoldRun 擋住的 RUN。</summary>
        public void ReleaseRun()
        {
            if (_heldRun == null) return;
            var run = _heldRun;
            _heldRun = null;
            EmitRun(run, null);
        }

        private void EmitRun(string line, int? abortedAfter)
        {
            var fields = line.Split(',');
            var count = int.Parse(fields[2]);
            var gapMs = int.Parse(fields[3]);
            var sent = abortedAfter ?? ReportOnly ?? count;

            for (var i = 0; i < sent; i++)
            {
                var start = _micros;
                var end = start + 120_000;
                _outgoing.Enqueue($"T,{i},{start},{end}");
                _micros = end + gapMs * 1000L + 12;
            }
            _outgoing.Enqueue(abortedAfter.HasValue ? $"ABORTED,{sent}" : $"DONE,{sent}");
        }
    }
}
