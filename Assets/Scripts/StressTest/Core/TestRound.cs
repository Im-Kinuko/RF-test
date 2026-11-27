using System;

namespace StressTest.Core
{
    /// <summary>
    /// 一輪完整測試：啟動 → 倒數 → 預熱 → 確認起點 → 多段 → 停止 → 總結。
    /// 由介面層每格呼叫 Tick（內含 session.Poll），研究人員的輸入以 EnterReading／Confirm 傳入。
    /// </summary>
    public sealed class TestRound
    {
        private readonly StressTestSession _session;
        private readonly RoundCsvLog _log;

        // 單發指令的等待
        private bool _awaitingAck;
        private int _expectedAckCount;
        private double _singleSentAt;
        private double _lastAckAt;

        private const double TimeEpsilon = 1e-6;

        /// <summary>逾時段之後等待遲到 DONE 的上限。</summary>
        public const double LateDoneWaitSeconds = 30.0;
        private double _lateDoneDeadline;
        private double _nextStartSpeed;

        private double _countdownEnd;
        private int _warmupSent;
        private double _nextWarmupAt;
        private SingleCommand _warmupDirection = SingleCommand.Up;
        private double _enteredReading;

        public TestRound(StressTestSession session, RoundConfig config, string roundId, RoundCsvLog log = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            RoundId = roundId;
            _log = log;
            Plan = RoundPlan.Create(config.TotalCount, config.SegmentSize, config.BaseSpeed);
        }

        public RoundConfig Config { get; }
        public string RoundId { get; }
        public RoundPlan Plan { get; }
        public RoundState State { get; private set; } = RoundState.Idle;
        public string FailureReason { get; private set; }

        public double CountdownRemaining { get; private set; }

        /// <summary>本次預熱中 Arduino 已回覆完成的次數：從 0 開始，每完成一次加 1。</summary>
        public int WarmupDone { get; private set; }

        /// <summary>預熱與重新預熱累計送出的單發加速／減速次數（不計分）。</summary>
        public int WarmupUpSent { get; private set; }
        public int WarmupDownSent { get; private set; }

        /// <summary>已確認的計分段中，Arduino 回報實際送出的加速／減速次數。</summary>
        public int UpSent => RoundSummary.SentCount(_results, SegmentDirection.Up);
        public int DownSent => RoundSummary.SentCount(_results, SegmentDirection.Down);

        private RoundSummary NewSummary() =>
            new RoundSummary(_results, WarmupReading, Plan, WarmupUpSent, WarmupDownSent, _run);

        /// <summary>目前要研究人員核對的面板速度。</summary>
        public double ExpectedPanelSpeed { get; private set; }

        /// <summary>輸入後、確認前的本段判定；其他時候為 null。</summary>
        public SegmentResult PendingResult { get; private set; }

        /// <summary>目前的韌體跑不了這個條件時回傳原因，否則為 null。</summary>
        public static string CheckFirmware(FirmwareInfo firmware, RoundConfig config) =>
            firmware?.CheckSegment(config.Mode, Math.Min(config.SegmentSize, config.TotalCount));

        public void Start(double now)
        {
            if (State != RoundState.Idle) return;

            var unsupported = CheckFirmware(_session.Firmware, Config);
            if (unsupported != null)
            {
                Fail(unsupported);
                return;
            }

            State = RoundState.Starting;
            CountdownRemaining = Config.StartCountdownSeconds;
            _countdownEnd = double.MaxValue;
            SendSingle(SingleCommand.Start, now);
        }

        public void Tick(double now)
        {
            _session.Poll(now);
            if (!CheckSingleAck(now)) return;

            switch (State)
            {
                case RoundState.Starting:
                    if (_countdownEnd == double.MaxValue) _countdownEnd = _lastAckAt + Config.StartCountdownSeconds;
                    CountdownRemaining = Math.Max(0, _countdownEnd - now);
                    if (now >= _countdownEnd)
                    {
                        BeginWarmup(SingleCommand.Up, Config.WarmupCount, now);
                    }
                    break;

                case RoundState.WarmingUp:
                    // 每次按壓（含最後一次）都等滿一個間隔：面板跟得上，最後的計數也看得到
                    if (now < _nextWarmupAt - TimeEpsilon) break;
                    if (_warmupSent < WarmupTarget)
                    {
                        SendWarmupPress(now);
                    }
                    else
                    {
                        ExpectedPanelSpeed = Config.BaseSpeed;
                        State = RoundState.AwaitingWarmupReading;
                    }
                    break;

                case RoundState.RunningSegment:
                    if (_session.State == SessionState.AwaitingReading && Config.IsContinuous)
                    {
                        FinishContinuousRun();
                    }
                    else if (_session.State == SessionState.AwaitingReading)
                    {
                        ExpectedPanelSpeed = _session.CurrentSegment.ExpectedEndSpeed;
                        State = RoundState.AwaitingSegmentReading;
                    }
                    break;

                case RoundState.WaitingForLateDone:
                    if (!_session.WaitingForLateDone)
                    {
                        StartSegment(SegmentIndex + 1, _nextStartSpeed, now);
                    }
                    else if (now > _lateDoneDeadline)
                    {
                        Fail($"上一段逾時後，Arduino 在 {LateDoneWaitSeconds:0} 秒內仍未回報完成。");
                    }
                    break;

                case RoundState.Aborting:
                    if (_session.State != SessionState.RunningSegment && !_session.WaitingForLateDone)
                    {
                        _session.DiscardSegment();
                        BeginStop(now);
                    }
                    else if (now > _abortDeadline)
                    {
                        Fail("Arduino 沒有回應中止指令。請立即按跑步機上的停止鍵。");
                    }
                    break;

                case RoundState.Stopping:
                    // 停止指令已 ACK
                    if (_finalReading.HasValue)
                    {
                        AskNextTurnOrReview();
                    }
                    else
                    {
                        Summary = NewSummary();
                        State = RoundState.Completed;
                    }
                    break;
            }
        }

        /// <summary>等待韌體回 ABORTED 的上限。光耦一筆 120 ms、RF 一筆約 340 ms，2 秒足夠。</summary>
        public const double AbortWaitSeconds = 2.0;
        private double _abortDeadline;

        /// <summary>本輪是否作廢（緊急停止或研究人員手動作廢）。</summary>
        public bool IsVoided { get; private set; }
        public string VoidReason { get; private set; }

        /// <summary>
        /// 緊急停止：段執行中先送 ABORT 等韌體中止，再送停止；其他狀態直接送停止。本輪標記作廢。
        /// 尚未開始或已結束時不做事。
        /// </summary>
        public void EmergencyStop(double now)
        {
            if (State == RoundState.Idle || State == RoundState.Completed || State == RoundState.Failed
                || State == RoundState.Aborting)
            {
                return;
            }

            if (TreadmillStopped)
            {
                // 傳送已結束、最後速度已確認：再送一次停止就好，不丟掉這一輪
                _session.SendSingle(SingleCommand.Stop);
                return;
            }

            IsVoided = true;
            VoidReason = "緊急停止";
            if (State == RoundState.Stopping) return; // 停止指令已送出

            PendingResult = null;
            if (_session.Abort())
            {
                _abortDeadline = now + AbortWaitSeconds;
                State = RoundState.Aborting;
            }
            else
            {
                _session.DiscardSegment();
                BeginStop(now);
            }

            // 停止指令送出之後才寫檔：寫檔出錯不能擋住停止。中止的整輪仍保留逐筆紀錄
            LogRunCommandsOnce();
        }

        /// <summary>
        /// 介面層即將中斷連線或關閉程式：盡量停止跑步機、保留逐筆紀錄，本輪記為失敗（作廢）。
        /// </summary>
        public void Abandon(string reason)
        {
            if (State == RoundState.Idle || State == RoundState.Completed || State == RoundState.Failed) return;

            if (!TreadmillStopped)
            {
                _session.Abort();
                _session.DiscardSegment();
                _session.SendSingle(SingleCommand.Stop);
            }
            PendingResult = null;
            _pendingResults = null;
            Fail(reason, keepReadings: false);
            LogRunCommandsOnce();
        }

        /// <summary>每輪 CSV 的那一列是否已寫入。</summary>
        public bool IsFinished { get; private set; }

        /// <summary>
        /// 研究人員離開總結畫面時呼叫，寫入每輪 CSV 的一列。
        /// voidReason 不為 null 時手動作廢；緊急停止或失敗的輪次一律記為作廢。
        /// 只在 Completed 或 Failed 時有效，且只寫一次。
        /// </summary>
        public bool Finish(string voidReason = null)
        {
            if (IsFinished || (State != RoundState.Completed && State != RoundState.Failed)) return false;

            if (voidReason != null)
            {
                IsVoided = true;
                VoidReason = voidReason;
            }
            else if (State == RoundState.Failed && !IsVoided)
            {
                IsVoided = true;
                VoidReason = FailureReason;
            }

            Summary = Summary ?? NewSummary();
            _log?.WriteRound(RoundId, Config, _session.Firmware, Summary, IsVoided ? "voided" : "completed", VoidReason);
            IsFinished = true;
            return true;
        }

        private void BeginStop(double now)
        {
            State = RoundState.Stopping;
            SendSingle(SingleCommand.Stop, now);
        }

        /// <summary>本輪總結；完成前為 null。</summary>
        public RoundSummary Summary { get; private set; }

        private readonly System.Collections.Generic.List<SegmentResult> _results = new System.Collections.Generic.List<SegmentResult>();

        /// <summary>已確認的各段結果，依段順序。</summary>
        public System.Collections.Generic.IReadOnlyList<SegmentResult> Results => _results;

        /// <summary>研究人員最近一次輸入、尚未確認的面板速度。</summary>
        public double EnteredReading => _enteredReading;

        /// <summary>本次預熱（或重新預熱）的方向。</summary>
        public SingleCommand WarmupDirection => _warmupDirection;

        /// <summary>本次預熱（或重新預熱）應送的次數。</summary>
        public int WarmupTarget { get; private set; }

        /// <summary>研究人員確認的預熱讀數；尚未確認時為 null。</summary>
        public double? WarmupReading { get; private set; }

        // ===== A 送法：整輪送完後的讀數 =====

        private PacedRun _run;
        private double _runStartSpeed;
        private double? _finalReading;
        private readonly System.Collections.Generic.List<double> _turns = new System.Collections.Generic.List<double>();
        private System.Collections.Generic.List<SegmentResult> _pendingResults;

        /// <summary>整輪送完後的逐筆傳送紀錄；還沒送完時為 null。</summary>
        public PacedRun Run => _run;

        /// <summary>要看錄影讀的轉向點數（有送出指令的段與段交界）。</summary>
        public int TurnCount => !Config.IsContinuous ? 0
            : _run == null ? Plan.Segments.Count - 1
            : Math.Max(0, _run.SentBlockCount - 1);

        /// <summary>A 送法：最後速度已確認，跑步機已送出停止。</summary>
        public bool TreadmillStopped => _finalReading.HasValue;

        /// <summary>停止指令沒有回覆的原因；正常時為 null。</summary>
        public string StopWarning { get; private set; }

        /// <summary>目前的步驟是否還需要序列連線。輸入轉向點與總結畫面不需要，拔線不該丟掉讀數。</summary>
        public bool NeedsLink => State != RoundState.Completed && State != RoundState.Failed && !TreadmillStopped;

        /// <summary>目前要輸入第幾個轉向點，從 0 起算。</summary>
        public int TurnIndex => _turns.Count;

        public System.Collections.Generic.IReadOnlyList<double> TurnReadings => _turns;

        /// <summary>整輪送完當下的面板速度；還沒確認時為 null。</summary>
        public double? FinalReading => _finalReading;

        /// <summary>讀數全部輸入完、尚未確認時的各段結果；其他時候為 null。</summary>
        public System.Collections.Generic.IReadOnlyList<SegmentResult> PendingResults => _pendingResults;

        /// <summary>轉向點讀不到時放棄：不猜數字，整輪作廢。逐筆紀錄與電腦端計數仍保留。</summary>
        public void GiveUpReadings()
        {
            if (State != RoundState.AwaitingTurnReading && State != RoundState.ConfirmingReadings) return;

            IsVoided = true;
            VoidReason = "轉向點讀數無法判讀";
            _pendingResults = null;
            Summary = NewSummary();
            State = RoundState.Completed;
        }

        private bool _runStarted;
        private bool _commandsLogged;

        private void FinishContinuousRun()
        {
            _run = _session.CurrentRun;
            // session 停在 AwaitingReading 直到最後速度確認，期間重開機才標記得到
            ExpectedPanelSpeed = ExpectedFinalSpeed();
            State = RoundState.AwaitingFinalReading;
            if (_run.PcSent < _run.Count)
            {
                IsVoided = true;
                VoidReason = "傳送提早結束，沒有送完全部指令";
            }
            LogRunCommandsOnce();
        }

        private double ExpectedFinalSpeed()
        {
            var expected = _runStartSpeed;
            for (var i = 0; i < _run.SentBlockCount; i++) expected += _run.Blocks[i].ExpectedChange;
            return expected;
        }

        private void LogRunCommandsOnce()
        {
            if (!_runStarted || _commandsLogged) return;
            _run = _session.CurrentRun;
            _commandsLogged = true;
            _log?.WriteCommands(RoundId, Config, _session.Firmware, _run);
        }

        private void AskNextTurnOrReview()
        {
            if (_run.SentBlockCount == 0)
            {
                Summary = NewSummary();
                State = RoundState.Completed;
                return;
            }
            if (_turns.Count < TurnCount)
            {
                var from = _turns.Count == 0 ? _runStartSpeed : _turns[_turns.Count - 1];
                ExpectedPanelSpeed = from + _run.Blocks[_turns.Count].ExpectedChange;
                State = RoundState.AwaitingTurnReading;
                return;
            }

            _pendingResults = new System.Collections.Generic.List<SegmentResult>();
            var start = _runStartSpeed;
            for (var i = 0; i < _run.SentBlockCount; i++)
            {
                var end = i < _turns.Count ? _turns[i] : _finalReading.Value;
                _run.Blocks[i].StartSpeed = start;
                _pendingResults.Add(new SegmentResult(_run.Blocks[i], end));
                start = end;
            }
            State = RoundState.ConfirmingReadings;
        }

        private void ConfirmReadings()
        {
            for (var i = 0; i < _pendingResults.Count; i++)
            {
                _results.Add(_pendingResults[i]);
                _log?.WriteSegmentResult(RoundId, _session.Firmware, i, _pendingResults[i]);
            }
            _pendingResults = null;
            Summary = NewSummary();
            State = RoundState.Completed;
        }

        /// <summary>研究人員輸入面板速度（尚未確認）。</summary>
        public void EnterReading(double panelSpeed)
        {
            switch (State)
            {
                case RoundState.AwaitingWarmupReading:
                    _enteredReading = panelSpeed;
                    State = RoundState.ConfirmingWarmupReading;
                    break;

                case RoundState.AwaitingFinalReading:
                    _enteredReading = panelSpeed;
                    State = RoundState.ConfirmingFinalReading;
                    break;

                case RoundState.AwaitingTurnReading:
                    _turns.Add(panelSpeed);
                    AskNextTurnOrReview();
                    break;

                case RoundState.AwaitingSegmentReading:
                    _enteredReading = panelSpeed;
                    PendingResult = new SegmentResult(_session.CurrentSegment, panelSpeed);
                    State = RoundState.ConfirmingSegmentReading;
                    break;
            }
        }

        /// <summary>確認上一次輸入並往下一步。</summary>
        public void Confirm(double now)
        {
            switch (State)
            {
                case RoundState.ConfirmingWarmupReading:
                    WarmupReading = _enteredReading;
                    if (Math.Abs(_enteredReading - Config.BaseSpeed) < SegmentRun.SpeedStep / 2)
                    {
                        StartSegment(0, _enteredReading, now);
                    }
                    else
                    {
                        State = RoundState.WarmupMismatch;
                    }
                    break;

                case RoundState.ConfirmingFinalReading:
                    if (TreadmillStopped)
                    {
                        // 停止之後回來改最後速度，不用再停一次
                        _finalReading = _enteredReading;
                        AskNextTurnOrReview();
                        break;
                    }
                    _session.CompleteRun();
                    _finalReading = _enteredReading;
                    BeginStop(now);
                    break;

                case RoundState.ConfirmingReadings:
                    ConfirmReadings();
                    break;

                case RoundState.ConfirmingSegmentReading:
                    var result = _session.SubmitReading(_enteredReading);
                    PendingResult = null;
                    _results.Add(result);
                    _log?.WriteSegment(RoundId, Config, _session.Firmware, SegmentIndex, result);

                    var next = SegmentIndex + 1;
                    if (next < Plan.Segments.Count && _session.WaitingForLateDone)
                    {
                        // 上一段逾時，韌體可能還在跑：等它交出 DONE 再開始，避免整輪失敗
                        _nextStartSpeed = result.ActualEndSpeed;
                        _lateDoneDeadline = now + LateDoneWaitSeconds;
                        State = RoundState.WaitingForLateDone;
                    }
                    else if (next < Plan.Segments.Count)
                    {
                        // 下一段以實際讀數為起點，不以理論值，避免誤差累積
                        StartSegment(next, result.ActualEndSpeed, now);
                    }
                    else
                    {
                        BeginStop(now);
                    }
                    break;
            }
        }

        /// <summary>撤銷尚未確認的輸入，回到輸入畫面。</summary>
        public void Undo()
        {
            switch (State)
            {
                case RoundState.ConfirmingWarmupReading:
                    State = RoundState.AwaitingWarmupReading;
                    break;
                case RoundState.ConfirmingSegmentReading:
                    PendingResult = null;
                    State = RoundState.AwaitingSegmentReading;
                    break;
                case RoundState.ConfirmingFinalReading:
                    State = RoundState.AwaitingFinalReading;
                    break;
                case RoundState.AwaitingTurnReading:
                case RoundState.ConfirmingReadings:
                    _pendingResults = null;
                    if (_turns.Count == 0)
                    {
                        ExpectedPanelSpeed = ExpectedFinalSpeed();
                        State = RoundState.AwaitingFinalReading;
                        break;
                    }
                    _turns.RemoveAt(_turns.Count - 1);
                    AskNextTurnOrReview();
                    break;
            }
        }

        /// <summary>預熱讀數不符時：單發補足差額，再請研究人員確認一次。</summary>
        public void Rewarm(double now)
        {
            if (State != RoundState.WarmupMismatch || WarmupReading == null) return;

            var steps = (int)Math.Round((Config.BaseSpeed - WarmupReading.Value) / SegmentRun.SpeedStep);
            BeginWarmup(steps > 0 ? SingleCommand.Up : SingleCommand.Down, Math.Abs(steps), now);
        }

        /// <summary>預熱讀數不符時：以實際面板速度為第一段起點繼續。</summary>
        public void ContinueFromReading(double now)
        {
            if (State != RoundState.WarmupMismatch || WarmupReading == null) return;
            StartSegment(0, WarmupReading.Value, now);
        }

        private void BeginWarmup(SingleCommand direction, int count, double now)
        {
            State = RoundState.WarmingUp;
            _warmupDirection = direction;
            WarmupTarget = count;
            _warmupSent = 0;
            WarmupDone = 0;
            _nextWarmupAt = now;
            if (count > 0) SendWarmupPress(now);
        }

        private void SendWarmupPress(double now)
        {
            // 依固定時間軸排程（上一次預定時間 + 間隔），畫面更新延遲不會逐次累加
            _nextWarmupAt += Config.WarmupIntervalSeconds;
            _warmupSent++;
            SendSingle(_warmupDirection, now);
            // 收到 ACK 才計入預熱次數，被拒絕或沒回應的按壓不算
            if (_awaitingAck) _pendingWarmupPress = _warmupDirection;
        }

        private SingleCommand? _pendingWarmupPress;

        private void StartSegment(int index, double startSpeed, double now)
        {
            var planned = Plan.Segments[index];
            bool started;
            if (Config.IsContinuous)
            {
                _runStartSpeed = startSpeed;
                started = _session.RunContinuous(Plan.Segments, Config.GapMs, startSpeed, now);
                _runStarted = started;
            }
            else
            {
                started = _session.StartSegment(Config.Mode, planned.Direction, planned.Count, Config.GapMs, startSpeed, now);
            }
            if (!started)
            {
                Fail("無法開始這一段：Arduino 未連線或仍在處理上一段。");
                return;
            }
            _segmentIndex = index;
            State = RoundState.RunningSegment;
        }

        private int _segmentIndex;

        /// <summary>目前（或剛完成）的段索引，從 0 起算。連續傳送中是正在送的那一段。</summary>
        public int SegmentIndex
        {
            get
            {
                if (!Config.IsContinuous) return _segmentIndex;
                var run = _session.CurrentRun;
                if (run == null) return 0;
                for (var i = 0; i < run.Blocks.Count; i++)
                {
                    if (run.Blocks[i] == _session.CurrentSegment) return i;
                }
                return 0;
            }
        }

        private void SendSingle(SingleCommand command, double now)
        {
            if (!_session.SendSingle(command))
            {
                Fail("無法送出指令：Arduino 未連線或正在忙碌。");
                return;
            }
            // 以 session 的累計 ACK 數為基準（前幾輪與手動單發也會累加）；
            // 前一個單發還沒 ACK 時（例如啟動中按緊急停止），兩個 ACK 都要收到
            _expectedAckCount = (_awaitingAck ? _expectedAckCount : _session.SingleAckCount) + 1;
            _awaitingAck = true;
            _singleSentAt = now;
        }

        /// <summary>等待單發 ACK；回傳 true 表示目前沒有在等或已收到。</summary>
        private bool CheckSingleAck(double now)
        {
            if (!_awaitingAck) return State != RoundState.Failed;

            if (_session.SingleAckCount >= _expectedAckCount)
            {
                _awaitingAck = false;
                _lastAckAt = now;
                if (_pendingWarmupPress == SingleCommand.Up) WarmupUpSent++;
                else if (_pendingWarmupPress == SingleCommand.Down) WarmupDownSent++;
                if (_pendingWarmupPress != null) WarmupDone++;
                _pendingWarmupPress = null;
                return true;
            }
            if (_session.LastError != null)
            {
                Fail($"韌體回報錯誤：{_session.LastError}");
                return false;
            }
            if (now - _singleSentAt > Config.AckTimeoutSeconds)
            {
                Fail($"Arduino 在 {Config.AckTimeoutSeconds:0} 秒內沒有回應單發指令。");
                return false;
            }
            return false;
        }

        private void Fail(string reason, bool keepReadings = true)
        {
            if (keepReadings && State == RoundState.Stopping && _finalReading.HasValue)
            {
                // 讀數已在手上，只是停止指令沒有回覆：提醒後繼續輸入轉向點
                _awaitingAck = false;
                StopWarning = reason;
                AskNextTurnOrReview();
                return;
            }

            _awaitingAck = false;
            FailureReason = reason;
            State = RoundState.Failed;
        }
    }
}
