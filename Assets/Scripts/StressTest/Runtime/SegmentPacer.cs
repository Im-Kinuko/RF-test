using System;
using System.Diagnostics;
using System.Threading;
using StressTest.Core;

namespace StressTest.Runtime
{
    /// <summary>介面與背景執行緒共用的時鐘（秒）。Unity 的 Time API 只能在主執行緒用。</summary>
    internal static class MonotonicClock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();

        public static double Now => Watch.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// A 送法執行中以背景執行緒密集呼叫 session.Poll，讓下一筆的送出不受畫面更新率（約 16 ms）限制。
    /// 段結束、中止或 Dispose 時執行緒結束。執行期間會占滿一個 CPU 核心。
    /// </summary>
    internal sealed class SegmentPacer : IDisposable
    {
        private readonly Thread _thread;
        private volatile bool _stop;

        public SegmentPacer(StressTestSession session)
        {
            _thread = new Thread(() =>
            {
                while (!_stop && session.IsPacing)
                {
                    session.Poll(MonotonicClock.Now);
                    Thread.Sleep(0);
                }
            })
            {
                IsBackground = true,
                Name = "StressTest segment pacer",
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }

        public bool IsRunning => _thread.IsAlive;

        public void Dispose()
        {
            _stop = true;
            _thread.Join(500);
        }
    }
}
