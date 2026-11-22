using System;
using System.IO;
using System.Text;
using StressTest.Core;
using UnityEngine;

namespace StressTest.Runtime
{
    /// <summary>完整一輪的介面：設定、逐步引導、本輪總結與三份 CSV。</summary>
    public sealed partial class StressTestRunner
    {
        private TestRound _round;
        private string _roundMessage;

        private RoundCsvLog _roundLog;
        private StreamWriter _commandsFile, _roundSegmentsFile, _roundsFile;
        private string _roundLogFolder;

        private bool RoundIsActive => _round != null
            && _round.State != RoundState.Completed
            && _round.State != RoundState.Failed;

        private void StartRound(int gap, int total)
        {
            _roundMessage = null;
            if (!TryReadSegmentSize(out var segmentSize)) return;
            SavePreferences();

            var entry = _session.Firmware.Entry == EntryType.Opto ? "OPTO" : "RF";
            var roundId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{entry}-G{gap}-N{total}";

            try
            {
                EnsureRoundLog();
            }
            catch (IOException e)
            {
                _roundMessage = $"無法建立紀錄檔：{e.Message}";
                return;
            }

            _readingText = "";
            _inputError = null;
            _round = new TestRound(_session, new RoundConfig(gap, total, segmentSize), roundId, _roundLog);
            _round.Start(Time.realtimeSinceStartupAsDouble);
        }

        private void EnsureRoundLog()
        {
            if (_roundLog != null) return;

            var folder = LogFolder();
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var utf8Bom = new UTF8Encoding(true);
            _commandsFile = new StreamWriter(Path.Combine(folder, $"commands_{stamp}.csv"), false, utf8Bom);
            _roundSegmentsFile = new StreamWriter(Path.Combine(folder, $"segments_{stamp}.csv"), false, utf8Bom);
            _roundsFile = new StreamWriter(Path.Combine(folder, $"rounds_{stamp}.csv"), false, utf8Bom);
            _roundLog = new RoundCsvLog(_commandsFile, _roundSegmentsFile, _roundsFile, writeHeaders: true);
            _roundLogFolder = folder;
        }

        private void CloseRoundFiles()
        {

            _commandsFile?.Dispose();
            _roundSegmentsFile?.Dispose();
            _roundsFile?.Dispose();
            _commandsFile = _roundSegmentsFile = _roundsFile = null;
            _roundLog = null;
        }

        private void DrawRound()
        {
            var r = _round;
            var segmentText = r.State == RoundState.RunningSegment || r.State == RoundState.AwaitingSegmentReading
                || r.State == RoundState.ConfirmingSegmentReading
                ? $"　段 {r.SegmentIndex + 1}／{r.Plan.Segments.Count}"
                : "";
            GUILayout.Label($"本輪：間隔 {r.Config.GapMs} ms、{r.Config.TotalCount} 次{segmentText}", _title);
            GUILayout.Label("輪次 ID：" + r.RoundId, _small);
            GUILayout.Space(Px(12));

            switch (r.State)
            {
                case RoundState.Starting:
                    GUILayout.Label($"已送出啟動，等跑帶穩定：{Mathf.CeilToInt((float)r.CountdownRemaining)} 秒", _huge);
                    break;

                case RoundState.WarmingUp:
                    var direction = r.WarmupDirection == SingleCommand.Down ? "減速" : "加速";
                    GUILayout.Label($"預熱{direction}：{r.WarmupSent}／{r.WarmupTarget}", _huge);
                    GUILayout.Label($"每秒一次，把速度帶到 {r.Config.BaseSpeed:F1} km/h。不計分。", _body);
                    break;

                case RoundState.AwaitingWarmupReading:
                case RoundState.AwaitingSegmentReading:
                    if (r.State == RoundState.AwaitingSegmentReading) DrawReportWarning(_session.CurrentSegment);
                    DrawReadingInput(r.ExpectedPanelSpeed, speed => r.EnterReading(speed));
                    break;

                case RoundState.ConfirmingWarmupReading:
                    GUILayout.Label($"你輸入的面板速度：{r.EnteredReading:F1} km/h", _huge);
                    DrawConfirmKeys("確認，開始第一段");
                    break;

                case RoundState.ConfirmingSegmentReading:
                    DrawPendingResult(r.PendingResult);
                    DrawConfirmKeys(r.SegmentIndex + 1 < r.Plan.Segments.Count ? "確認，開始下一段" : "確認，結束本輪");
                    break;

                case RoundState.WarmupMismatch:
                    var reading = r.WarmupReading ?? 0;
                    GUILayout.Label($"面板是 {reading:F1} km/h，預期 {r.Config.BaseSpeed:F1} km/h", _huge);
                    GUILayout.BeginHorizontal();
                    PrimaryButton("重新預熱（補足差額）", () => r.Rewarm(Time.realtimeSinceStartupAsDouble));
                    if (GUILayout.Button($"以 {reading:F1} 為起點繼續", _big)) r.ContinueFromReading(Time.realtimeSinceStartupAsDouble);
                    GUILayout.EndHorizontal();
                    break;

                case RoundState.RunningSegment:
                    var planned = r.Plan.Segments[r.SegmentIndex];
                    GUILayout.Label($"執行中：{DirectionLabels[(int)planned.Direction]} {planned.Count} 筆", _huge);
                    if (_session.CurrentSegment != null) DrawSegmentProgress(_session.CurrentSegment);
                    GUILayout.Label("執行期間請勿操作，完成後會請你輸入面板速度。", _body);
                    break;

                case RoundState.WaitingForLateDone:
                    GUILayout.Label("上一段逾時，等 Arduino 回報完成後自動開始下一段…", _title);
                    break;

                case RoundState.Aborting:
                    GUILayout.Label("緊急停止：等 Arduino 中止…", _huge);
                    GUILayout.Label($"{TestRound.AbortWaitSeconds:0} 秒內沒有回應時，請直接按跑步機上的停止鍵。", _body);
                    break;

                case RoundState.Stopping:
                    GUILayout.Label("送出停止中…", _huge);
                    break;

                case RoundState.Completed:
                    if (r.IsVoided) GUILayout.Label($"本輪已作廢：{r.VoidReason}", _title);
                    DrawRoundSummary(r.Summary);
                    GUILayout.Label("請確認跑帶已完全停止，再開始下一輪。", _title);
                    DrawFinishButtons(r, "跑帶已停止，回到設定");
                    break;

                case RoundState.Failed:
                    GUILayout.Label("本輪中斷：" + r.FailureReason, _title);
                    GUILayout.Label("已確認的段已寫入紀錄，本輪會記為作廢。請確認跑步機狀態後重新開始。", _body);
                    DrawFinishButtons(r, "回到設定");
                    break;
            }
        }

        private string _voidReasonText = "";
        private const string VoidReasonControl = "VoidReason";

        /// <summary>總結畫面的出口：寫入每輪 CSV 後回到設定；可選擇填原因作廢。</summary>
        private void DrawFinishButtons(TestRound r, string leaveLabel)
        {
            if (r.State == RoundState.Completed && !r.IsVoided)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("作廢原因", _body, GUILayout.Width(Px(90)));
                GUI.SetNextControlName(VoidReasonControl);
                _voidReasonText = GUILayout.TextField(_voidReasonText, _input);
                GUI.enabled = _voidReasonText.Trim().Length > 0;
                if (GUILayout.Button("作廢此輪", _big, GUILayout.Width(Px(160)))) LeaveRound(_voidReasonText.Trim());
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Label("作廢的輪次原始資料保留，但不計入條件進度。", _small);
            }

            PrimaryButton(leaveLabel, () => LeaveRound(null));
        }

        private void LeaveRound(string voidReason)
        {
            FinishRound(voidReason);
            _voidReasonText = "";
            _round = null;
        }

        /// <summary>寫入每輪那一列並計入矩陣進度（作廢不計）。只在 Completed／Failed 時有效。</summary>
        private void FinishRound(string voidReason)
        {
            if (_round == null) return;
            try
            {
                if (_round.Finish(voidReason))
                {
                    _matrix.Record(_session?.Firmware?.Entry ?? _entry, _round.Config.GapMs, _round.Config.TotalCount, _round.IsVoided);
                }
            }
            catch (IOException e)
            {
                _roundMessage = $"寫入每輪紀錄失敗：{e.Message}";
            }
        }

        /// <summary>任何狀態都可用：輪次進行中交給 TestRound；手動單段則中止並送停止。</summary>
        private void EmergencyStop()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (RoundIsActive)
            {
                _round.EmergencyStop(now);
                return;
            }
            if (_session == null) return;

            if (_session.Abort())
            {
                _manualStopPending = true; // 等 ABORTED 回到 Ready 後送停止（見 Update）
                return;
            }
            _session.DiscardSegment();
            _session.SendSingle(SingleCommand.Stop);
        }

        private bool _manualStopPending;

        /// <summary>手動單段被中止後，回到 Ready 就補送停止。由 Update 呼叫。</summary>
        private void FlushManualStop()
        {
            if (!_manualStopPending || _session == null || _session.State == SessionState.RunningSegment) return;
            _manualStopPending = false;
            _session.DiscardSegment();
            _session.SendSingle(SingleCommand.Stop);
        }

        private void DrawConfirmKeys(string confirmLabel)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    ConfirmRound();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    UndoRound();
                    e.Use();
                }
            }

            GUILayout.BeginHorizontal();
            PrimaryButton(confirmLabel + "（Enter）", ConfirmRound);
            if (GUILayout.Button("修改輸入（Esc）", _big, GUILayout.Width(Px(220)))) UndoRound();
            GUILayout.EndHorizontal();
        }

        private void ConfirmRound()
        {
            _readingText = "";
            _round.Confirm(Time.realtimeSinceStartupAsDouble);
        }

        private void UndoRound()
        {
            _round.Undo();
            GUI.FocusControl(ReadingControl);
        }

        private void DrawReportWarning(SegmentRun segment)
        {
            if (segment == null || !segment.ReportShort) return;
            var reported = segment.TimedOut ? "逾時未回報" : $"只回報 {segment.ReportedCount} 筆";
            GUILayout.Label($"注意：Arduino {reported}（應送 {segment.Count} 筆）。仍請輸入面板速度。", _body);
        }

        private void DrawPendingResult(SegmentResult result)
        {
            GUILayout.Label($"面板 {result.ActualEndSpeed:F1} km/h → 推算掉包 {result.Drops} 筆", _huge);
            GUILayout.Label(
                $"預期變化 {result.ExpectedChange:+0.0;-0.0} km/h，實際 {result.ActualChange:+0.0;-0.0;0.0} km/h；" +
                $"Arduino 回報 {result.Segment.ReportedCount?.ToString() ?? "無"} 筆",
                _body);
            if (result.PossiblyClamped)
                GUILayout.Label("可能觸底／觸頂：理論終點超出 0.1～12.0，這段的掉包數不可信。", _title);
            if (result.Segment.ReportShort)
                GUILayout.Label("Arduino 回報不足：可能是序列埠或韌體問題，不一定是跑步機掉包。", _title);
        }

        private void DrawRoundSummary(RoundSummary summary)
        {
            GUILayout.Label($"本輪完成：總掉包 {summary.TotalDrops}／{summary.TotalSent} 筆", _huge);
            for (var i = 0; i < summary.Results.Count; i++)
            {
                var result = summary.Results[i];
                var flags = (result.PossiblyClamped ? "　可能觸底／觸頂" : "") + (result.Segment.ReportShort ? "　回報不足" : "");
                GUILayout.Label(
                    $"段 {i + 1}：{DirectionLabels[(int)result.Segment.Direction]} {result.Segment.Count} 筆，" +
                    $"{result.Segment.StartSpeed:F1} → {result.ActualEndSpeed:F1}，掉包 {result.Drops}{flags}",
                    _small);
            }
            GUILayout.Label(
                $"實際間隔：平均 {summary.MeanGapMicros / 1000.0:F3} ms、最大 {summary.MaxGapMicros / 1000.0:F3} ms", _small);
            if (_roundLogFolder != null) GUILayout.Label("紀錄位置：" + _roundLogFolder, _small);
        }
    }
}
