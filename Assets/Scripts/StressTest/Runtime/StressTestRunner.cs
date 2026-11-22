using System;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using StressTest.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StressTest.Runtime
{
    /// <summary>
    /// 壓力測試的 OnGUI 介面。只呈現 StressTestSession 的狀態並轉發操作，不含判定邏輯。
    /// 在名為 StressTest 的場景啟動時自動建立。
    /// </summary>
    public sealed partial class StressTestRunner : MonoBehaviour
    {
        private const string SceneName = "StressTest";

        private static readonly string[] EntryLabels = { "光耦", "RF" };

        private string[] _ports = new string[0];
        private int _portIndex = -1;
        private EntryType _entry = EntryType.Opto;
        private SerialPortLink _link;
        private StressTestSession _session;
        private string _connectError;
        private GUIStyle _big, _title, _body, _huge, _input;

        // 單段測試
        private static readonly string[] DirectionLabels = { "加速", "減速" };
        private static readonly int[] GapOptions = { 0, 1, 5, 10 };
        private static readonly string[] GapLabels = { "0 ms", "1 ms", "5 ms", "10 ms" };
        private const string ReadingControl = "PanelReading";
        private SegmentDirection _direction = SegmentDirection.Up;
        private int _gapIndex;
        private string _countText = "20";
        private string _startText = "1.0";
        private string _readingText = "";
        private string _inputError;
        private double _segmentStartedAt;
        private SegmentResult _lastResult;
        private SegmentCsvWriter _segmentCsv;
        private StreamWriter _segmentFile;
        private string _segmentCsvPath;
        private Vector2 _scroll;
        private SegmentRun _progressSegment;

        private static string LogFolder()
        {
            var folder = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "TestLogs");
            Directory.CreateDirectory(folder);
            return folder;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInStressTestScene()
        {
            if (SceneManager.GetActiveScene().name != SceneName) return;
            if (FindAnyObjectByType<StressTestRunner>() != null) return;
            new GameObject(nameof(StressTestRunner)).AddComponent<StressTestRunner>();
        }

        private void Awake()
        {
            LoadPreferences();
            RefreshPorts();
            RestoreMatrix();
        }

        private void Update()
        {
            if (_session == null) return;

            if (!_link.IsOpen)
            {
                Disconnect();
                _connectError = "序列埠已中斷。請確認 USB 線後重新連線。";
                return;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            if (RoundIsActive) _round.Tick(now);
            else _session.Poll(now);
            FlushManualStop();

            // 新的一段開始時記下時間，給進度條估算用
            if (_session.CurrentSegment != _progressSegment)
            {
                _progressSegment = _session.CurrentSegment;
                _segmentStartedAt = now;
            }
        }

        private void OnDestroy()
        {
            Disconnect();
            _segmentFile?.Dispose();
            _segmentFile = null;
            CloseRoundFiles();
        }

        private void RefreshPorts()
        {
            var previous = _portIndex >= 0 && _portIndex < _ports.Length ? _ports[_portIndex] : _preferredPort;
            _ports = SerialPort.GetPortNames();
            Array.Sort(_ports);
            _portIndex = previous != null ? Array.IndexOf(_ports, previous) : -1;
            if (_portIndex < 0 && _ports.Length == 1) _portIndex = 0;
        }

        private void Connect()
        {
            var portName = _portIndex >= 0 ? _ports[_portIndex] : null;
            _link = SerialPortLink.TryOpen(portName, out _connectError);
            if (_link != null) SavePreferences();
            if (_link == null) return;

            _session = new StressTestSession(_link, _entry);
            _session.BeginHandshake(Time.realtimeSinceStartupAsDouble);
        }

        private void Disconnect()
        {
            if (RoundIsActive) _roundMessage = "連線中斷，本輪未完成。已確認的段已寫入紀錄。";
            // 停在總結畫面時中斷或關閉：先補寫每輪那一列（Finish 只在 Completed／Failed 時有效）
            FinishRound(null);
            _round = null;
            _manualStopPending = false;
            _session = null;
            _link?.Dispose();
            _link = null;
        }

        private void EnsureStyles()
        {
            if (_big != null) return;
            _big = new GUIStyle(GUI.skin.button) { fontSize = 22, fixedHeight = 48 };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            _huge = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold };
            _input = new GUIStyle(GUI.skin.textField) { fontSize = 32, fixedHeight = 52 };
        }

        private void OnGUI()
        {
            EnsureStyles();
            GUILayout.BeginArea(new Rect(24, 24, Mathf.Min(800, Screen.width - 48), Screen.height - 48));
            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawConnection();
            GUILayout.Space(16);
            DrawStatus();
            GUILayout.Space(8);
            DrawEmergencyStop();
            GUILayout.Space(16);

            if (_round != null)
            {
                DrawRound();
            }
            else
            {
                DrawRoundSetup();
                GUILayout.Space(24);
                DrawSingleCommands();
                GUILayout.Space(16);
                DrawSegment();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSegment()
        {
            GUILayout.Label("單段測試（手動）", _title);
            var state = _session?.State;

            if (state == SessionState.RunningSegment)
            {
                DrawRunningSegment();
                return;
            }
            if (state == SessionState.AwaitingReading)
            {
                DrawAwaitingReading();
                return;
            }

            var waitingForLateDone = _session != null && _session.WaitingForLateDone;
            if (waitingForLateDone)
            {
                GUILayout.Label("上一段逾時，Arduino 可能還在執行。請等它回報完成，或按「中斷連線」後重新連線。", _body);
            }

            GUI.enabled = state == SessionState.Ready && !waitingForLateDone;
            _direction = (SegmentDirection)GUILayout.Toolbar((int)_direction, DirectionLabels, _big);
            _gapIndex = GUILayout.Toolbar(_gapIndex, GapLabels, _big);

            GUILayout.BeginHorizontal();
            GUILayout.Label("筆數", _body, GUILayout.Width(60));
            _countText = GUILayout.TextField(_countText, _input, GUILayout.Width(100));
            GUILayout.Space(24);
            GUILayout.Label("目前面板速度", _body, GUILayout.Width(130));
            _startText = GUILayout.TextField(_startText, _input, GUILayout.Width(120));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("跑一段", _big)) StartSegment();
            GUI.enabled = true;

            if (_inputError != null) GUILayout.Label(_inputError, _body);
            if (_lastResult != null) DrawLastResult();
        }

        private void StartSegment()
        {
            _inputError = null;
            if (!int.TryParse(_countText, out var count) || count < 1 || count > 32)
            {
                _inputError = "筆數請輸入 1～32。";
                return;
            }
            if (!PanelSpeedInput.TryParse(_startText, out var startSpeed))
            {
                _inputError = "目前面板速度請輸入 0.1～12.0。";
                return;
            }

            _lastResult = null;
            _readingText = "";
            _session.RunSegment(_direction, count, GapOptions[_gapIndex], startSpeed, Time.realtimeSinceStartupAsDouble);
        }

        private void DrawRunningSegment()
        {
            DrawSegmentProgress(_session.CurrentSegment);
            GUILayout.Label("執行期間請勿操作，完成後會請你輸入面板速度。", _body);
        }

        private void DrawSegmentProgress(SegmentRun segment)
        {
            var actionMs = 500.0;
            if (_session.Firmware.Parameters.TryGetValue("action_ms", out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var reported)
                && reported > 0)
            {
                actionMs = reported;
            }
            var estimate = segment.Count * (actionMs + segment.GapMs) / 1000.0;
            var elapsed = Time.realtimeSinceStartupAsDouble - _segmentStartedAt;

            GUILayout.Label($"執行中：{DirectionLabels[(int)segment.Direction]} {segment.Count} 筆，間隔 {segment.GapMs} ms", _body);
            var rect = GUILayoutUtility.GetRect(100, 24, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            GUI.Box(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01((float)(elapsed / estimate)), rect.height), GUIContent.none);
        }

        private void DrawAwaitingReading()
        {
            var segment = _session.CurrentSegment;

            if (segment.ReportShort)
            {
                var reported = segment.TimedOut ? "逾時未回報" : $"只回報 {segment.ReportedCount} 筆";
                GUILayout.Label($"注意：Arduino {reported}（應送 {segment.Count} 筆）。仍請輸入面板速度。", _body);
            }

            DrawReadingInput(segment.ExpectedEndSpeed, SubmitReading);
        }

        /// <summary>大字顯示預期速度、自動聚焦的輸入框；Enter 送出，解析成功才呼叫 onSubmit。</summary>
        private void DrawReadingInput(double expectedSpeed, Action<double> onSubmit)
        {
            GUILayout.Label($"請看面板，預期 {expectedSpeed:F1} km/h", _huge);

            var submit = false;
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                submit = true;
                e.Use();
            }

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(ReadingControl);
            _readingText = GUILayout.TextField(_readingText, _input, GUILayout.Width(200));
            if (GUILayout.Button("確認（Enter）", _big)) submit = true;
            GUILayout.EndHorizontal();
            GUI.FocusControl(ReadingControl);

            if (submit)
            {
                if (PanelSpeedInput.TryParse(_readingText, out var speed))
                {
                    _inputError = null;
                    onSubmit(speed);
                }
                else
                {
                    _inputError = "請輸入 0.1～12.0 的面板速度，例如 2.7 或 27。";
                }
            }

            if (_inputError != null) GUILayout.Label(_inputError, _body);
        }

        private void SubmitReading(double speed)
        {
            _lastResult = _session.SubmitReading(speed);
            if (_lastResult == null) return;

            WriteSegmentCsv(_lastResult);
            _startText = speed.ToString("F1", CultureInfo.InvariantCulture);
        }

        private void WriteSegmentCsv(SegmentResult result)
        {
            try
            {
                if (_segmentCsv == null)
                {
                    _segmentCsvPath = Path.Combine(LogFolder(), $"manual_segments_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    _segmentFile = new StreamWriter(_segmentCsvPath, false, new UTF8Encoding(true));
                    _segmentCsv = new SegmentCsvWriter(_segmentFile, writeHeader: true);
                }
                _segmentCsv.Write(result, _session.Firmware.Entry, _session.Firmware.Version, DateTime.Now);
            }
            catch (IOException e)
            {
                _inputError = $"寫入紀錄失敗：{e.Message}";
            }
        }

        private void DrawLastResult()
        {
            var r = _lastResult;
            GUILayout.Label(
                $"上一段：預期變化 {r.ExpectedChange:+0.0;-0.0} km/h，實際 {r.ActualChange:+0.0;-0.0;0.0} km/h → 推算掉包 {r.Drops} 筆",
                _title);
            GUILayout.Label(
                $"Arduino 回報 {r.Segment.ReportedCount?.ToString() ?? "無"} 筆，實際間隔平均 {r.Segment.MeanGapMicros / 1000.0:F3} ms、最大 {r.Segment.MaxGapMicros / 1000.0:F3} ms",
                _body);
            if (_segmentCsvPath != null) GUILayout.Label("已寫入：" + _segmentCsvPath, _body);
        }

        private void DrawConnection()
        {
            var connected = _session != null;
            GUI.enabled = !connected;

            GUILayout.Label("1. 選擇入口", _title);
            _entry = (EntryType)GUILayout.Toolbar((int)_entry, EntryLabels, _big);

            GUILayout.Label("2. 選擇 COM port", _title);
            GUILayout.BeginHorizontal();
            if (_ports.Length == 0)
            {
                GUILayout.Label("找不到任何 COM port。", _body);
            }
            else
            {
                _portIndex = GUILayout.Toolbar(_portIndex, _ports, _big);
            }
            if (GUILayout.Button("重新整理", _big, GUILayout.Width(140))) RefreshPorts();
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            GUILayout.Label("3. 連線", _title);
            if (!connected)
            {
                GUI.enabled = _portIndex >= 0;
                if (GUILayout.Button("連線", _big)) Connect();
                GUI.enabled = true;
            }
            else if (GUILayout.Button("中斷連線", _big))
            {
                Disconnect();
            }
        }

        private void DrawStatus()
        {
            Color light;
            string text;

            if (_session == null)
            {
                light = Color.gray;
                text = _connectError ?? "尚未連線";
            }
            else
            {
                switch (_session.State)
                {
                    case SessionState.Handshaking:
                        light = Color.yellow;
                        text = "等待韌體回報身分…";
                        break;
                    case SessionState.Ready:
                        light = Color.green;
                        text = $"已連線：{EntryLabels[(int)_session.Firmware.Entry]}韌體 v{_session.Firmware.Version}";
                        break;
                    case SessionState.EntryMismatch:
                        light = Color.red;
                        text = $"入口不符：你選了「{EntryLabels[(int)_entry]}」，但板子上是「{EntryLabels[(int)_session.Firmware.Entry]}」韌體。請中斷連線後改選入口或換板子。";
                        break;
                    case SessionState.HandshakeTimeout:
                        light = Color.red;
                        text = "逾時：4 秒內沒有收到韌體回報。請確認板子燒的是測試韌體，baud 為 115200。";
                        break;
                    default:
                        light = Color.gray;
                        text = _session.State.ToString();
                        break;
                }
            }

            GUILayout.BeginHorizontal();
            var previous = GUI.color;
            GUI.color = light;
            GUILayout.Box(GUIContent.none, GUILayout.Width(32), GUILayout.Height(32));
            GUI.color = previous;
            GUILayout.Label(text, _title);
            GUILayout.EndHorizontal();

            if (_session?.Firmware != null)
            {
                var parameters = string.Join("　", _session.Firmware.Parameters.Select(kv => $"{kv.Key}={kv.Value}"));
                GUILayout.Label("韌體參數：" + parameters, _body);
            }
        }

        private void DrawSingleCommands()
        {
            GUILayout.Label("單發測試", _title);
            GUI.enabled = _session != null && _session.State == SessionState.Ready;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("啟動", _big)) _session.SendSingle(SingleCommand.Start);
            if (GUILayout.Button("加速", _big)) _session.SendSingle(SingleCommand.Up);
            if (GUILayout.Button("減速", _big)) _session.SendSingle(SingleCommand.Down);
            if (GUILayout.Button("停止", _big)) _session.SendSingle(SingleCommand.Stop);
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            if (_session?.LastError != null)
            {
                GUILayout.Label($"韌體回報錯誤：{_session.LastError}", _body);
            }
            else if (_session?.LastAck != null)
            {
                GUILayout.Label($"韌體已執行：{_session.LastAck}", _body);
            }
        }
    }
}
