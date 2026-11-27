using System;
using System.Collections;
using System.IO;
using StressTest.Core;
using UnityEngine;

namespace StressTest.Runtime
{
    /// <summary>
    /// 版面導覽：以 -stresstest-tour &lt;資料夾&gt; 啟動執行檔時，接上 SimulatedArduino，
    /// 自動走過連線、一輪測試、各種異常與手動工具的每一個畫面並逐一截圖，完成後結束。
    /// 只用於產生說明文件的截圖；畫面上的數字是模擬資料。
    /// </summary>
    public sealed partial class StressTestRunner
    {
        private string _tourFolder;
        private int _tourShot;

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-stresstest-tour");
            if (i < 0 || i + 1 >= args.Length) return;

            _tourFolder = args[i + 1];
            Directory.CreateDirectory(_tourFolder);
            StartCoroutine(RunTour());
        }

        private IEnumerator RunTour()
        {
            // 導覽畫面沿用 RUN 對照的流程（模擬韌體可以扣住整段回報）
            _mode = SendMode.Burst;
            _matrix.Mode = _mode;

            yield return Shot("disconnected");

            // ---- 連線異常 ----
            _entry = EntryType.Opto;
            ConnectSimulated(new SimulatedArduino { Entry = "RF", Parameters = "bits=24;repeat=2;release_ms=250;action_ms=340;max_run=90" });
            yield return Until(() => _session.State == SessionState.EntryMismatch, 3);
            yield return Shot("entry_mismatch");
            Disconnect();

            ConnectSimulated(new SimulatedArduino { RespondToHello = false });
            yield return Shot("handshaking");
            yield return Until(() => _session.State == SessionState.HandshakeTimeout, 6);
            yield return Shot("handshake_timeout");
            Disconnect();

            // ---- 連線成功、閒置 ----
            var sim = new SimulatedArduino();
            ConnectSimulated(sim);
            yield return Until(() => _session.State == SessionState.Ready, 3);
            foreach (var (gap, count) in new[] { (0, 40), (0, 80), (0, 120), (1, 40) })
            {
                _matrix.Record(EntryType.Opto, gap, count, voided: false);
                _matrix.Record(EntryType.Rf, gap, count, voided: false);
            }
            _matrix.Record(EntryType.Opto, 1, 80, voided: false);
            _restoreMessage = "已從 2 個紀錄檔還原 9 輪完成紀錄。（導覽畫面，數字為模擬資料）";
            yield return Shot("idle");

            _showManualTools = true;
            yield return Shot("idle_manual_tools");
            _showManualTools = false;

            // ---- 一輪：啟動、預熱、各段、總結 ----
            StartRound(1, 80);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("round_starting");

            yield return Until(() => _round.State == RoundState.WarmingUp, 8);
            yield return new WaitForSeconds(3.2f);
            yield return Shot("round_warming_up");

            yield return Until(() => _round.State == RoundState.AwaitingWarmupReading, 12);
            _readingText = "2.0";
            yield return Shot("round_warmup_reading");

            _round.EnterReading(2.0);
            yield return Shot("round_warmup_confirm");

            sim.HoldRun = true;
            ConfirmRound();
            yield return new WaitForSeconds(1.2f);
            yield return Shot("round_running_segment");

            sim.ReleaseRun();
            yield return Until(() => _round.State == RoundState.AwaitingSegmentReading, 3);
            _readingText = "3.7";
            yield return Shot("round_segment_reading");

            _round.EnterReading(3.7);
            yield return Shot("round_segment_confirm");

            sim.HoldRun = false;
            foreach (var reading in new[] { 1.7, 3.7, 1.7 }) // 第 2～4 段（減、加、減）
            {
                if (_round.State == RoundState.ConfirmingSegmentReading) ConfirmRound();
                yield return Until(() => _round.State == RoundState.AwaitingSegmentReading
                                         || _round.State == RoundState.Completed, 3);
                if (_round.State == RoundState.Completed) break;
                _round.EnterReading(reading);
            }
            if (_round.State == RoundState.ConfirmingSegmentReading) ConfirmRound();
            yield return Until(() => _round.State == RoundState.Completed, 3);
            yield return Shot("round_summary");

            _voidReasonText = "面板讀數有疑慮，重做";
            yield return Shot("round_void_reason");
            LeaveRound(_voidReasonText);

            // ---- 預熱讀數不符、回報不足、可能觸底 ----
            StartRound(0, 40);
            yield return Until(() => _round.State == RoundState.AwaitingWarmupReading, 20);
            _round.EnterReading(1.8);
            ConfirmRound();
            yield return Shot("round_warmup_mismatch");

            _round.ContinueFromReading(MonotonicClock.Now);
            yield return Until(() => _round.State == RoundState.AwaitingSegmentReading, 3);
            _round.EnterReading(2.3);
            sim.ReportOnly = 17;
            ConfirmRound();
            yield return Until(() => _round.State == RoundState.AwaitingSegmentReading, 3);
            _readingText = "1.0";
            yield return Shot("round_report_short");

            _round.EnterReading(1.0);
            yield return Shot("round_clamped_confirm");
            sim.ReportOnly = null;
            ConfirmRound();
            yield return Until(() => _round.State == RoundState.Completed, 3);
            LeaveRound(null);

            // ---- 段逾時、等待遲到的 DONE、緊急停止沒有回應、失敗 ----
            StartRound(5, 40);
            yield return Until(() => _round.State == RoundState.AwaitingWarmupReading, 20);
            _round.EnterReading(2.0);
            sim.HoldRun = true;
            ConfirmRound();
            yield return Until(() => _round.State == RoundState.AwaitingSegmentReading, 12);
            _readingText = "4.0";
            yield return Shot("round_segment_timeout");

            _round.EnterReading(4.0);
            ConfirmRound();
            yield return Shot("round_waiting_late_done");

            sim.RespondToAbort = false;
            EmergencyStop();
            yield return Shot("round_aborting");

            yield return Until(() => _round.State == RoundState.Failed, 4);
            yield return Shot("round_failed");
            LeaveRound(null);
            Disconnect();

            // ---- 手動單段 ----
            sim = new SimulatedArduino { HoldRun = true };
            ConnectSimulated(sim);
            yield return Until(() => _session.State == SessionState.Ready, 3);
            _showManualTools = true;
            _direction = SegmentDirection.Up;
            _gapIndex = 2;
            _startText = "2.0";
            StartSegment();
            yield return new WaitForSeconds(1.0f);
            yield return Shot("manual_running");

            sim.ReleaseRun();
            yield return Until(() => _session.State == SessionState.AwaitingReading, 3);
            _readingText = "3.9";
            yield return Shot("manual_reading");

            SubmitReading(3.9);
            yield return Shot("manual_result");

            Disconnect();
            Application.Quit();
        }

        private void ConnectSimulated(SimulatedArduino sim)
        {
            _connectError = null;
            _link = sim;
            _session = new StressTestSession(_link, _entry);
            _session.BeginHandshake(MonotonicClock.Now);
        }

        private IEnumerator Shot(string name)
        {
            // 等幾格讓 OnGUI 依新狀態重畫，再於畫格結束時截圖
            for (var i = 0; i < 4; i++) yield return null;
            _tourShot++;
            ScreenCapture.CaptureScreenshot(Path.Combine(_tourFolder, $"{_tourShot:00}_{name}.png"));
            for (var i = 0; i < 3; i++) yield return null;
        }

        private static IEnumerator Until(Func<bool> condition, float timeoutSeconds)
        {
            var end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
