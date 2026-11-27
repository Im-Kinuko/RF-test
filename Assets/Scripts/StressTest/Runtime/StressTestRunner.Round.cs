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
            if (!TryReadSegmentSize(out var segmentSize)) return;
            var config = new RoundConfig(gap, total, segmentSize) { Mode = _mode };
            _roundMessage = TestRound.CheckFirmware(_session.Firmware, config);
            if (_roundMessage != null) return;
            SavePreferences();

            var entry = _session.Firmware.Entry == EntryType.Opto ? "OPTO" : "RF";
            var roundId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{entry}-{config.Mode.Code()}-G{gap}-N{total}";

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
            _round = new TestRound(_session, config, roundId, _roundLog);
            _round.Start(MonotonicClock.Now);
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
            var continuous = r.Config.IsContinuous;
            var segmentText = r.State == RoundState.RunningSegment || r.State == RoundState.AwaitingSegmentReading
                || r.State == RoundState.ConfirmingSegmentReading
                ? $"　段 {r.SegmentIndex + 1}／{r.Plan.Segments.Count}"
                : "";
            var flow = continuous ? "，中間不停" : "";
            GUILayout.Label(
                $"本輪（{r.Config.Mode.Code()}）：間隔 {r.Config.GapMs} ms、{r.Config.TotalCount} 次{flow}{segmentText}", _title);
            var warmup = r.WarmupUpSent + r.WarmupDownSent > 0
                ? $"　預熱 加速 {r.WarmupUpSent}、減速 {r.WarmupDownSent}"
                : "";
            GUILayout.Label(continuous
                    ? $"預計加速 {r.Plan.UpCount}、減速 {r.Plan.DownCount}，共 {r.Plan.Segments.Count} 段{warmup}"
                    : $"加速 已送 {r.UpSent}／預計 {r.Plan.UpCount}　減速 已送 {r.DownSent}／預計 {r.Plan.DownCount}{warmup}",
                _body);
            GUILayout.Label("輪次 ID：" + r.RoundId, _small);
            GUILayout.Space(Px(12));

            switch (r.State)
            {
                case RoundState.Starting:
                    GUILayout.Label($"已送出啟動，等跑帶穩定：{Mathf.CeilToInt((float)r.CountdownRemaining)} 秒", _huge);
                    break;

                case RoundState.WarmingUp:
                    var direction = r.WarmupDirection == SingleCommand.Down ? "減速" : "加速";
                    GUILayout.Label($"預熱{direction}：{r.WarmupDone}／{r.WarmupTarget}", _huge);
                    GUILayout.Label($"每秒一次，把速度帶到 {r.Config.BaseSpeed:F1} km/h。不計分。", _body);
                    break;

                case RoundState.AwaitingWarmupReading:
                case RoundState.AwaitingSegmentReading:
                    if (r.State == RoundState.AwaitingSegmentReading) DrawReportWarning(_session.CurrentSegment);
                    DrawReadingInput(r.ExpectedPanelSpeed, speed => r.EnterReading(speed));
                    break;

                case RoundState.ConfirmingWarmupReading:
                    GUILayout.Label($"你輸入的面板速度：{r.EnteredReading:F1} km/h", _huge);
                    if (continuous)
                    {
                        GUILayout.Label($"確認後會連續送出 {r.Config.TotalCount} 筆，中間不停。請先開始錄影，畫面要拍到跑步機面板。", _title);
                    }
                    DrawConfirmKeys(continuous ? "已開始錄影，開始送出" : "確認，開始第一段");
                    break;

                case RoundState.ConfirmingSegmentReading:
                    DrawPendingResult(r.PendingResult);
                    DrawConfirmKeys(r.SegmentIndex + 1 < r.Plan.Segments.Count ? "確認，開始下一段" : "確認，結束本輪");
                    break;

                case RoundState.WarmupMismatch:
                    var reading = r.WarmupReading ?? 0;
                    GUILayout.Label($"面板是 {reading:F1} km/h，預期 {r.Config.BaseSpeed:F1} km/h", _huge);
                    if (continuous)
                    {
                        GUILayout.Label("選「以此為起點繼續」會立刻連續送出，請先開始錄影。", _title);
                    }
                    GUILayout.BeginHorizontal();
                    PrimaryButton("重新預熱（補足差額）", () => r.Rewarm(MonotonicClock.Now));
                    if (GUILayout.Button($"以 {reading:F1} 為起點繼續", _big)) r.ContinueFromReading(MonotonicClock.Now);
                    GUILayout.EndHorizontal();
                    break;

                case RoundState.RunningSegment:
                    var planned = r.Plan.Segments[r.SegmentIndex];
                    if (continuous)
                    {
                        DrawContinuousProgress(r, planned);
                        break;
                    }
                    GUILayout.Label($"執行中：{DirectionLabels[(int)planned.Direction]} {planned.Count} 筆", _huge);
                    if (_session.CurrentSegment != null) DrawSegmentProgress(_session.CurrentSegment);
                    GUILayout.Label("執行期間請勿操作，完成後會請你輸入面板速度。", _body);
                    break;

                case RoundState.AwaitingFinalReading:
                    GUILayout.Label(r.TreadmillStopped ? "修改最後速度（跑步機已停止）" : "整輪送完了，跑步機還在轉。", _title);
                    DrawRunWarning(r.Run);
                    DrawReadingInput(
                        r.TreadmillStopped
                            ? $"送完當下的速度（預期 {r.ExpectedPanelSpeed:F1}）"
                            : $"請看面板，現在的速度（預期 {r.ExpectedPanelSpeed:F1}）",
                        r.ExpectedPanelSpeed, speed => r.EnterReading(speed));
                    if (!r.TreadmillStopped) GUILayout.Label("確認後程式會停止跑步機。", _body);
                    break;

                case RoundState.ConfirmingFinalReading:
                    GUILayout.Label($"最後速度：{r.EnteredReading:F1} km/h", _huge);
                    DrawConfirmKeys(r.TreadmillStopped ? "確認" : "確認，停止跑步機");
                    break;

                case RoundState.AwaitingTurnReading:
                    DrawTurnReading(r);
                    break;

                case RoundState.ConfirmingReadings:
                    DrawReadingsReview(r);
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
                    GUILayout.Label(continuous
                        ? "本輪會記為作廢。整輪已送完或按過緊急停止時，逐筆紀錄有保存。請確認跑步機狀態後重新開始。"
                        : "已確認的段已寫入紀錄，本輪會記為作廢。請確認跑步機狀態後重新開始。", _body);
                    DrawFinishButtons(r, "回到設定");
                    break;
            }
        }

        // ===== A 送法的畫面 =====

        private void DrawContinuousProgress(TestRound r, PlannedSegment planned)
        {
            var run = _session.CurrentRun;
            GUILayout.Label(
                $"連續送出中：第 {r.SegmentIndex + 1}／{r.Plan.Segments.Count} 段（{DirectionLabels[(int)planned.Direction]}）", _huge);
            if (run != null)
            {
                GUILayout.Label($"已送 {run.PcSent}／{r.Config.TotalCount} 筆、已回覆 {run.Replies} 筆", _title);
                DrawProgressBar((float)run.Replies / r.Config.TotalCount);
            }
            GUILayout.Label(
                $"速度在 {r.Plan.Segments[0].ExpectedStartSpeed:F1} 和 {r.Plan.Segments[0].ExpectedEndSpeed:F1} 之間來回。" +
                "現在不用操作，確認手機正在錄影。", _body);
        }

        private void DrawRunWarning(PacedRun run)
        {
            if (run == null) return;
            if (run.AnyRebooted) GUILayout.Label("注意：Arduino 重新開機過。仍請照實輸入面板速度，這一輪之後要作廢。", _title);
            if (run.AnyTimedOut)
            {
                GUILayout.Label(
                    $"注意：連續 {PacedRun.MaxConsecutiveNoReply} 筆沒有回覆，傳送提早結束。仍請輸入面板速度。", _title);
            }
            if (run.PcSent < run.Count)
                GUILayout.Label("沒有送完全部指令，這一輪會自動作廢。只需要輸入有送出的段。", _title);
            if (run.Replies < run.Count)
            {
                GUILayout.Label(
                    $"電腦送出 {run.PcSent}／{run.Count} 筆，收到回覆 {run.Replies} 筆，Arduino 執行 {Show(run.ArduinoExecuted)} 筆。" +
                    "指令遺失是要收集的資料。", _body);
            }
        }

        private void DrawTurnReading(TestRound r)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _readingText = "";
                r.Undo();
                e.Use();
                return;
            }

            var index = r.TurnIndex;
            var block = r.Plan.Segments[index];
            var turnName = block.Direction == SegmentDirection.Up ? "最高點" : "最低點";
            GUILayout.Label($"看錄影：轉向點 {index + 1}／{r.TurnCount}", _huge);
            DrawStopWarning(r);
            if (r.IsVoided) GUILayout.Label($"本輪會記為作廢：{r.VoidReason}", _body);
            GUILayout.Label(
                $"找第 {index + 1} 段（{DirectionLabels[(int)block.Direction]}）結束、換方向時面板的{turnName}。跑步機已停，可以慢慢看。", _title);
            GUILayout.Label($"最後速度 {r.FinalReading:F1}　已輸入：{ReadingList(r)}", _body);
            DrawReadingInput($"{turnName}（預期 {r.ExpectedPanelSpeed:F1}）", r.ExpectedPanelSpeed, speed =>
            {
                r.EnterReading(speed);
                _readingText = "";
            });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("回上一個（Esc）", _big, GUILayout.Width(Px(220))))
            {
                _readingText = "";
                r.Undo();
            }
            DrawGiveUpButton(r);
            GUILayout.EndHorizontal();
            GUILayout.Label("看不清楚時不要猜。輸入完之前不要關程式。", _small);
        }

        private void DrawStopWarning(TestRound r)
        {
            if (r.StopWarning == null) return;
            GUILayout.Label($"停止指令沒有回覆（{r.StopWarning}）。請確認跑帶已停，沒停就按跑步機上的停止鍵。", _title);
        }

        private void DrawGiveUpButton(TestRound r)
        {
            if (GUILayout.Button("讀不到，作廢此輪", _big, GUILayout.Width(Px(240)))) r.GiveUpReadings();
        }

        private static string ReadingList(TestRound r)
        {
            if (r.TurnReadings.Count == 0) return "（還沒有）";
            var parts = new string[r.TurnReadings.Count];
            for (var i = 0; i < parts.Length; i++) parts[i] = r.TurnReadings[i].ToString("F1");
            return string.Join("、", parts);
        }

        private void DrawReadingsReview(TestRound r)
        {
            var results = r.PendingResults;
            if (results == null) return;
            GUILayout.Label("請核對各段結果", _huge);
            DrawStopWarning(r);
            if (r.IsVoided) GUILayout.Label($"本輪會記為作廢：{r.VoidReason}", _title);
            for (var i = 0; i < results.Count; i++) GUILayout.Label(SegmentLine(i, results[i]), _body);
            DrawConfirmKeys("確認，完成本輪");
            DrawGiveUpButton(r);
        }

        private string SegmentLine(int index, SegmentResult result)
        {
            var flags = (result.PossiblyClamped ? "　可能觸底／觸頂" : "") + (result.Segment.ReportShort ? "　回報不足" : "")
                        + (result.Segment.Rebooted ? "　重新開機" : "");
            var hops = result.Segment.Mode == SendMode.Ack
                ? $"（序列端少 {Show(result.LinkLost)}、跑步機端少 {Show(result.TreadmillMissing)}）"
                : "";
            return $"段 {index + 1}：{DirectionLabels[(int)result.Segment.Direction]} {result.Segment.Count} 筆，" +
                   $"{result.Segment.StartSpeed:F1} → {result.ActualEndSpeed:F1}，掉包 {result.Drops}{hops}{flags}";
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
                    _matrix.Record(_session?.Firmware?.Entry ?? _entry, _round.Config.GapMs, _round.Config.TotalCount, _round.IsVoided,
                        _round.Config.Mode);
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
            var now = MonotonicClock.Now;
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
            _round.Confirm(MonotonicClock.Now);
        }

        private void UndoRound()
        {
            _round.Undo();
            GUI.FocusControl(ReadingControl);
        }

        private void DrawReportWarning(SegmentRun segment)
        {
            if (segment == null || !segment.ReportShort) return;
            if (segment.Rebooted)
                GUILayout.Label("注意：Arduino 在這段執行中重新開機。仍請輸入面板速度。", _title);
            if (segment.Mode == SendMode.Ack)
            {
                GUILayout.Label(
                    $"注意：電腦送出 {segment.PcSent}／{segment.Count} 筆，收到完成回覆 {segment.Replies} 筆，" +
                    $"Arduino 執行 {segment.ArduinoExecuted?.ToString() ?? "不明"} 筆。仍請輸入面板速度。", _body);
                return;
            }
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
            if (result.Segment.Mode == SendMode.Ack) GUILayout.Label(HopText(result), _title);
            if (result.PossiblyClamped)
                GUILayout.Label($"可能觸底／觸頂：理論終點超出 {PanelSpeedInput.Min:F1}～{PanelSpeedInput.Max:F1}，這段的掉包數不可信。", _title);
            if (result.Segment.Rebooted)
                GUILayout.Label("Arduino 在這段中重新開機。", _title);
            if (!result.Segment.ReportShort) return;
            GUILayout.Label(result.Segment.Mode == SendMode.Ack
                ? "有指令沒有完成往返。這是掉包資料，不需要因此作廢。"
                : "Arduino 回報不足：可能是序列埠或韌體問題，不一定是跑步機掉包。", _title);
        }

        private static string Show(int? count) => count?.ToString() ?? "不明";

        /// <summary>A 送法一段在各檢查點的筆數。</summary>
        private static string HopText(SegmentResult result)
        {
            var s = result.Segment;
            return $"電腦送 {s.PcSent} → Arduino 收 {Show(s.ArduinoReceived)} → 執行 {Show(s.ArduinoExecuted)}" +
                   $" → 面板 {result.PanelSteps} 格（序列端少 {Show(result.LinkLost)}、跑步機端少 {Show(result.TreadmillMissing)}）";
        }

        private void DrawRoundSummary(RoundSummary summary)
        {
            GUILayout.Label(summary.Results.Count > 0
                ? $"本輪完成：總掉包 {summary.TotalDrops}／{summary.TotalSent} 筆"
                : "本輪沒有各段結果", _huge);
            for (var i = 0; i < summary.Results.Count; i++) GUILayout.Label(SegmentLine(i, summary.Results[i]), _small);
            if (summary.PcSent != null)
            {
                GUILayout.Label(
                    $"各段合計：電腦送 {Show(summary.PcSent)}、Arduino 收 {Show(summary.ArduinoReceived)}、執行 {Show(summary.ArduinoExecuted)}；" +
                    $"序列端少 {Show(summary.LinkLost)}、跑步機端少 {Show(summary.TreadmillMissing)}", _title);
            }
            GUILayout.Label(
                $"實際間隔：平均 {summary.MeanGapMicros / 1000.0:F3} ms、最大 {summary.MaxGapMicros / 1000.0:F3} ms", _small);
            GUILayout.Label(
                $"加速：預計 {summary.UpPlanned}、Arduino 實送 {summary.UpSent}　減速：預計 {summary.DownPlanned}、Arduino 實送 {summary.DownSent}" +
                $"　預熱（不計分）：加速 {summary.WarmupUpSent}、減速 {summary.WarmupDownSent}",
                _small);
            if (_roundLogFolder != null) GUILayout.Label("紀錄位置：" + _roundLogFolder, _small);
        }
    }
}
