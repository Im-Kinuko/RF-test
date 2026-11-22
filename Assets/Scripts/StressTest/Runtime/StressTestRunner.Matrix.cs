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
        private const string PrefSegmentSize = "StressTest.SegmentSize";

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
            _segmentSizeText = PlayerPrefs.GetInt(PrefSegmentSize, 20).ToString();
            if (int.TryParse(_repetitionsText, out var reps)) _matrix.Repetitions = reps;
        }

        private void SavePreferences()
        {
            if (_portIndex >= 0 && _portIndex < _ports.Length) PlayerPrefs.SetString(PrefPort, _ports[_portIndex]);
            PlayerPrefs.SetInt(PrefEntry, _entry == EntryType.Rf ? 1 : 0);
            if (int.TryParse(_repetitionsText, out var reps)) PlayerPrefs.SetInt(PrefRepetitions, reps);
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
            if (int.TryParse(_segmentSizeText, out segmentSize) && segmentSize >= 1 && segmentSize <= 32) return true;
            _roundMessage = "每段筆數請輸入 1～32。";
            return false;
        }

        private void DrawRoundSetup()
        {
            GUILayout.Label("完整一輪：條件矩陣", _title);
            if (_restoreMessage != null) GUILayout.Label(_restoreMessage, _body);
            if (_roundMessage != null) GUILayout.Label(_roundMessage, _body);

            GUILayout.BeginHorizontal();
            GUILayout.Label("每條件輪數", _body, GUILayout.Width(110));
            _repetitionsText = GUILayout.TextField(_repetitionsText, _input, GUILayout.Width(80));
            if (int.TryParse(_repetitionsText, out var reps) && reps >= 1 && reps <= 20) _matrix.Repetitions = reps;
            GUILayout.Space(24);
            GUILayout.Label("每段筆數", _body, GUILayout.Width(90));
            _segmentSizeText = GUILayout.TextField(_segmentSizeText, _input, GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("開啟紀錄資料夾", _big, GUILayout.Width(200))) OpenLogFolder();
            GUILayout.EndHorizontal();

            DrawMatrixGrid();
            DrawNextRound();
        }

        private bool CanStartRound =>
            _session != null && _session.State == SessionState.Ready && !_session.WaitingForLateDone;

        private void DrawMatrixGrid()
        {
            GUILayout.Label("點格子可單獨跑該條件（使用目前連線的入口）。格內為 光耦／RF 已完成輪數。", _body);

            GUILayout.BeginHorizontal();
            GUILayout.Label("", _body, GUILayout.Width(90));
            foreach (var count in ConditionMatrix.Counts) GUILayout.Label($"{count} 次", _title, GUILayout.Width(200));
            GUILayout.EndHorizontal();

            foreach (var gap in ConditionMatrix.Gaps)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{gap} ms", _title, GUILayout.Width(90));
                foreach (var count in ConditionMatrix.Counts)
                {
                    var opto = _matrix.Completed(EntryType.Opto, gap, count);
                    var rf = _matrix.Completed(EntryType.Rf, gap, count);
                    var done = opto >= _matrix.Repetitions && rf >= _matrix.Repetitions;
                    var label = $"光耦 {opto}/{_matrix.Repetitions}　RF {rf}/{_matrix.Repetitions}" + (done ? "（完成）" : "");

                    GUI.enabled = CanStartRound;
                    if (GUILayout.Button(label, _big, GUILayout.Width(200))) StartRound(gap, count);
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawNextRound()
        {
            var scheduled = _matrix.Next();
            if (scheduled == null)
            {
                GUILayout.Label("所有條件都已完成設定的輪數。", _title);
                return;
            }

            GUILayout.Label(
                $"排程建議：{EntryLabels[(int)scheduled.Entry]}、間隔 {scheduled.GapMs} ms、{scheduled.TotalCount} 次（第 {scheduled.Repetition} 輪）",
                _body);

            if (_session?.Firmware == null) return;

            var connected = _session.Firmware.Entry;
            var next = _matrix.NextFor(connected);
            if (next == null)
            {
                GUILayout.Label($"{EntryLabels[(int)connected]}已完成所有條件，請換另一個入口。", _title);
                return;
            }
            if (scheduled.Entry != connected)
            {
                GUILayout.Label($"依排程下一輪輪到{EntryLabels[(int)scheduled.Entry]}；也可以先繼續用{EntryLabels[(int)connected]}。", _body);
            }

            GUI.enabled = CanStartRound;
            if (GUILayout.Button(
                    $"自動跑下一輪：{EntryLabels[(int)connected]}、{next.GapMs} ms、{next.TotalCount} 次（第 {next.Repetition} 輪）",
                    _big))
            {
                StartRound(next.GapMs, next.TotalCount);
            }
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
