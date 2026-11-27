using System;
using System.IO;
using StressTest.Core;
using UnityEngine;

namespace StressTest.Runtime
{
    /// <summary>條件矩陣、自動排程、跨日續測（從每輪 CSV 還原）與偏好設定。</summary>
    public sealed partial class StressTestRunner
    {
        private const string PrefPort = "StressTest.Port";
        private const string PrefEntry = "StressTest.Entry";
        private const string PrefRepetitions = "StressTest.Repetitions";
        private const string PrefMode = "StressTest.SendMode";
        private const string PrefSegmentSize = "StressTest.SegmentSize";
        private static readonly string[] ModeLabels = { "A 等回覆", "RUN 對照" };
        private SendMode _mode = SendMode.Ack;

        private ConditionMatrix _matrix = new ConditionMatrix(3);
        private string _repetitionsText = "3";
        private string _segmentSizeText = "20";
        private string _preferredPort;
        private string _restoreMessage;

        private void LoadPreferences()
        {
            _preferredPort = PlayerPrefs.GetString(PrefPort, null);
            _entry = PlayerPrefs.GetInt(PrefEntry, 0) == 1 ? EntryType.Rf : EntryType.Opto;
            _repetitionsText = PlayerPrefs.GetInt(PrefRepetitions, 3).ToString();
            if (int.TryParse(_repetitionsText, out var reps)) _matrix.Repetitions = reps;
            _mode = PlayerPrefs.GetInt(PrefMode, 0) == 1 ? SendMode.Burst : SendMode.Ack;
            _matrix.Mode = _mode;
            // 舊版偏好可能超過現在的上限，超過就回到預設
            var size = PlayerPrefs.GetInt(PrefSegmentSize, RoundConfig.DefaultSegmentSize);
            if (size < 1 || size > RoundConfig.MaxSegmentSizeFor(_mode)) size = RoundConfig.DefaultSegmentSize;
            _segmentSizeText = size.ToString();
        }

        private void SavePreferences()
        {
            if (_portIndex >= 0 && _portIndex < _ports.Length) PlayerPrefs.SetString(PrefPort, _ports[_portIndex]);
            PlayerPrefs.SetInt(PrefEntry, _entry == EntryType.Rf ? 1 : 0);
            if (int.TryParse(_repetitionsText, out var reps)) PlayerPrefs.SetInt(PrefRepetitions, reps);
            PlayerPrefs.SetInt(PrefMode, _mode == SendMode.Burst ? 1 : 0);
            if (int.TryParse(_segmentSizeText, out var size)) PlayerPrefs.SetInt(PrefSegmentSize, size);
            PlayerPrefs.Save();
        }

        /// <summary>讀入紀錄資料夾內所有 rounds_*.csv，還原各條件已完成的輪數。</summary>
        private void RestoreMatrix()
        {
            try
            {
                var files = Directory.GetFiles(LogFolder(), "rounds_*.csv");
                var loaded = 0;
                foreach (var file in files)
                {
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        loaded += _matrix.LoadRoundsCsv(ReadLines(reader));
                    }
                }
                _restoreMessage = files.Length == 0
                    ? "尚無先前紀錄，從頭開始。"
                    : $"已從 {files.Length} 個紀錄檔還原 {loaded} 輪完成紀錄。";
            }
            catch (IOException e)
            {
                _restoreMessage = $"讀取先前紀錄失敗：{e.Message}";
            }
        }

        private static System.Collections.Generic.IEnumerable<string> ReadLines(StreamReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null) yield return line;
        }

        private bool TryReadSegmentSize(out int segmentSize)
        {
            if (int.TryParse(_segmentSizeText, out segmentSize) && segmentSize >= 1
                && segmentSize <= RoundConfig.MaxSegmentSizeFor(_mode)) return true;
            _roundMessage = $"每段筆數請輸入 1～{RoundConfig.MaxSegmentSizeFor(_mode)}。";
            return false;
        }

        /// <summary>右側：條件矩陣、輪數設定、排程建議。格子是次要按鈕，只有閒置時可按。</summary>
        private void DrawMatrixPanel()
        {
            GUILayout.Label("條件矩陣", _title);
            if (_restoreMessage != null) GUILayout.Label(_restoreMessage, _small);

            // 輪次或手動段進行中不能換送法
            GUI.enabled = _round == null
                          && _uiState != SessionState.RunningSegment && _uiState != SessionState.AwaitingReading;
            _mode = GUILayout.Toolbar(_mode == SendMode.Ack ? 0 : 1, ModeLabels, _big) == 0 ? SendMode.Ack : SendMode.Burst;
            _matrix.Mode = _mode;
            GUILayout.Label(_mode == SendMode.Ack
                ? "電腦逐筆送，整輪中間不停；送完看錄影輸入轉向點讀數。"
                : "對照組：Arduino 一次送一段，每段停下來讀面板。", _small);
            GUILayout.BeginHorizontal();
            GUILayout.Label("每條件輪數", _body, GUILayout.Width(Px(110)));
            _repetitionsText = GUILayout.TextField(_repetitionsText, _input, GUILayout.Width(Px(70)));
            if (int.TryParse(_repetitionsText, out var reps) && reps >= 1 && reps <= 20) _matrix.Repetitions = reps;
            GUILayout.Space(Px(16));
            GUILayout.Label("每段筆數", _body, GUILayout.Width(Px(90)));
            _segmentSizeText = GUILayout.TextField(_segmentSizeText, _input, GUILayout.Width(Px(70)));
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            DrawMatrixGrid();

            var scheduled = _matrix.Next();
            GUILayout.Label(scheduled == null
                ? "所有條件都已完成設定的輪數。"
                : $"整體排程下一輪：{EntryLabels[(int)scheduled.Entry]}、{scheduled.GapMs} ms、{scheduled.TotalCount} 次（第 {scheduled.Repetition} 輪）",
                _small);

            if (GUILayout.Button("開啟紀錄資料夾", _big)) OpenLogFolder();
        }

        private bool CanStartRound =>
            _round == null && _session != null && _session.State == SessionState.Ready && !_session.WaitingForLateDone;

        private void DrawMatrixGrid()
        {
            GUILayout.Label("點格子可單獨跑該條件（用目前連線的入口）。", _small);
            var cellWidth = GUILayout.Width(Px(150));

            GUILayout.BeginHorizontal();
            GUILayout.Label("", _body, GUILayout.Width(Px(70)));
            foreach (var count in ConditionMatrix.Counts) GUILayout.Label($"{count} 次", _title, cellWidth);
            GUILayout.EndHorizontal();

            foreach (var gap in ConditionMatrix.Gaps)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{gap} ms", _title, GUILayout.Width(Px(70)));
                foreach (var count in ConditionMatrix.Counts)
                {
                    var opto = _matrix.Completed(EntryType.Opto, gap, count);
                    var rf = _matrix.Completed(EntryType.Rf, gap, count);
                    var done = opto >= _matrix.Repetitions && rf >= _matrix.Repetitions;
                    var label = $"光耦 {opto}/{_matrix.Repetitions}\nRF {rf}/{_matrix.Repetitions}" + (done ? "（完成）" : "");

                    GUI.enabled = CanStartRound;
                    if (GUILayout.Button(label, _cell, cellWidth)) StartRound(gap, count);
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>左側閒置卡片的主要動作：依排程跑目前連線入口的下一輪。</summary>
        private void DrawNextRound()
        {
            var connected = _session.Firmware.Entry;
            var next = _matrix.NextFor(connected);
            if (next == null)
            {
                GUILayout.Label($"{EntryLabels[(int)connected]}已完成所有條件。", _huge);
                GUILayout.Label("請換另一個入口的板子，或在右側點格子補跑。", _body);
                return;
            }

            GUILayout.Label($"下一輪（{_mode.Code()}）：{EntryLabels[(int)connected]}、間隔 {next.GapMs} ms、{next.TotalCount} 次", _huge);
            var maxSize = RoundConfig.MaxSegmentSizeFor(_mode);
            var sizeValid = int.TryParse(_segmentSizeText, out var size) && size >= 1 && size <= maxSize;
            var config = new RoundConfig(next.GapMs, next.TotalCount, sizeValid ? size : RoundConfig.DefaultSegmentSize)
                { Mode = _mode };
            var plan = RoundPlan.Create(config.TotalCount, config.SegmentSize, config.BaseSpeed);
            if (sizeValid)
            {
                var how = config.IsContinuous ? "，每段換方向、中間不停" : "";
                GUILayout.Label(
                    $"加速 {plan.UpCount} 次、減速 {plan.DownCount} 次，分 {plan.Segments.Count} 段（每段 {size} 筆）{how}", _title);
            }
            else
            {
                GUILayout.Label($"每段筆數請輸入 1～{maxSize}。", _title);
            }
            GUILayout.Label(
                $"第 {next.Repetition} 輪。開始後會自動啟動跑步機（開機 {config.BootSpeed:F1} km/h），" +
                $"預熱加速 {config.WarmupCount} 次到 {config.BaseSpeed:F1} 後開始計分" +
                $"（{config.BaseSpeed:F1}↔{plan.Segments[0].ExpectedEndSpeed:F1}）。跑帶上請不要站人。", _body);
            if (config.IsContinuous)
            {
                GUILayout.Label(
                    $"請先架好錄影。送完後要輸入最後速度，再看影片輸入 {plan.Segments.Count - 1} 個轉向點。", _title);
            }
            var unsupported = TestRound.CheckFirmware(_session.Firmware, config);
            if (unsupported != null) GUILayout.Label(unsupported, _title);

            var scheduled = _matrix.Next();
            if (scheduled != null && scheduled.Entry != connected)
            {
                GUILayout.Label($"依整體排程下一輪輪到{EntryLabels[(int)scheduled.Entry]}；也可以先繼續用{EntryLabels[(int)connected]}。", _small);
            }

            GUI.enabled = CanStartRound && unsupported == null && sizeValid;
            PrimaryButton("開始下一輪", () => StartRound(next.GapMs, next.TotalCount));
            GUI.enabled = true;
        }

        private void OpenLogFolder()
        {
            try
            {
                Application.OpenURL(new Uri(LogFolder()).AbsoluteUri);
            }
            catch (Exception e) when (e is IOException || e is UriFormatException)
            {
                _roundMessage = $"無法開啟資料夾：{e.Message}";
            }
        }
    }
}
