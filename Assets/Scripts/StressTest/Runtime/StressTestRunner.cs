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
    /// 壓力測試的 OnGUI 介面。只呈現 StressTestSession／TestRound 的狀態並轉發操作，不含判定邏輯。
    /// 版面分三區：上方狀態列、左側「現在要做的事」、右側條件矩陣。在名為 StressTest 的場景啟動時自動建立。
    /// </summary>
    public sealed partial class StressTestRunner : MonoBehaviour
    {
        private const string SceneName = "StressTest";

        private static readonly string[] EntryLabels = { "光耦", "RF" };

        private string[] _ports = new string[0];
        private int _portIndex = -1;
        private EntryType _entry = EntryType.Opto;
        private ISerialLink _link; // 正式為 SerialPortLink；版面導覽時為 SimulatedArduino
        private StressTestSession _session;
        private string _connectError;

        // 手動單段
        private static readonly string[] DirectionLabels = { "加速", "減速" };
        private static readonly int[] GapOptions = { 0, 1, 5, 10 };
        private static readonly string[] GapLabels = { "0 ms", "1 ms", "5 ms", "10 ms" };
        private const string ReadingControl = "PanelReading";
        private SegmentDirection _direction = SegmentDirection.Up;
        private int _gapIndex;
        private string _countText = "20";
        private string _startText = "2.0";
        private string _readingText = "";
        private string _inputError;
        private double _segmentStartedAt;
        private SegmentResult _lastResult;
        private SegmentCsvWriter _segmentCsv;
        private StreamWriter _segmentFile;
        private string _segmentCsvPath;
        private SegmentRun _progressSegment;
        private bool _showManualTools;

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
            ReadScreenshotArgument();
        }

        private void Update()
        {
            UpdateScreenshot();
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
            var serial = SerialPortLink.TryOpen(portName, out _connectError);
            if (serial == null) return;
            _link = serial;

            SavePreferences();
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
            (_link as IDisposable)?.Dispose();
            _link = null;
        }

        // ===== 版面 =====

        private void OnGUI()
        {
            EnsureStyles();
            HandleShortcuts();

            var m = Px(16);
            var w = Screen.width;
            var h = Screen.height;
            var top = new Rect(m, m, w - 2 * m, Px(96));
            var bodyY = top.yMax + m;
            var leftWidth = (w - 3 * m) * 0.56f;
            var left = new Rect(m, bodyY, leftWidth, h - bodyY - m);
            var right = new Rect(left.xMax + m, bodyY, w - left.xMax - 2 * m, h - bodyY - m);

            Panel(top, DrawStatusBar);
            Panel(left, () =>
            {
                _leftScroll = GUILayout.BeginScrollView(_leftScroll);
                DrawCurrentTask();
                GUILayout.EndScrollView();
            });
            Panel(right, () =>
            {
                _rightScroll = GUILayout.BeginScrollView(_rightScroll);
                DrawMatrixPanel();
                GUILayout.EndScrollView();
            });
        }

        private Vector2 _leftScroll, _rightScroll;

        private void Panel(Rect rect, Action draw)
        {
            GUI.Box(rect, GUIContent.none, _panel);
            var pad = Px(12);
            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + pad, rect.width - 2 * pad, rect.height - 2 * pad));
            draw();
            GUILayout.EndArea();
            GUI.enabled = true;
        }

        /// <summary>空白鍵＝緊急停止，任何狀態都有效；只有輸入作廢原因時讓空白鍵打字。</summary>
        private void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space
                && GUI.GetNameOfFocusedControl() != VoidReasonControl)
            {
                EmergencyStop();
                e.Use();
            }
        }

        private void DrawStatusBar()
        {
            GetStatus(out var light, out var text);

            GUILayout.BeginHorizontal();
            // 實心圓點：直接以顏色畫白色貼圖，不受深色 box 背景影響
            var lightRect = GUILayoutUtility.GetRect(Px(36), Px(36), GUILayout.Width(Px(36)), GUILayout.Height(Px(36)));
            GUI.DrawTexture(lightRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, light, 0, Px(18));

            GUILayout.BeginVertical();
            GUILayout.Label(text, _title);
            if (_session?.Firmware != null)
            {
                var parameters = string.Join("　", _session.Firmware.Parameters.Select(kv => $"{kv.Key}={kv.Value}"));
                GUILayout.Label($"{_ports.ElementAtOrDefault(_portIndex)}　韌體參數：{parameters}", _small);
            }
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            GUI.enabled = _session != null;
            if (GUILayout.Button("中斷連線", _big, GUILayout.Width(Px(160)))) Disconnect();
            GUI.enabled = true;
            GUILayout.Space(Px(12));
            DangerButton("緊急停止（空白鍵）", EmergencyStop, Px(320));
            GUILayout.EndHorizontal();
        }

        private void GetStatus(out Color light, out string text)
        {
            if (_session == null)
            {
                light = Color.gray;
                text = _connectError ?? "尚未連線";
                return;
            }

            var entry = _session.Firmware != null ? EntryLabels[(int)_session.Firmware.Entry] : "";
            switch (_session.State)
            {
                case SessionState.Handshaking:
                    light = Color.yellow;
                    text = "等待韌體回報身分…";
                    break;
                case SessionState.Ready:
                    light = Color.green;
                    text = $"已連線：{entry}韌體 v{_session.Firmware.Version}";
                    break;
                case SessionState.RunningSegment:
                    light = Color.green;
                    text = $"已連線：{entry}韌體，正在執行一段";
                    break;
                case SessionState.AwaitingReading:
                    light = Color.green;
                    text = $"已連線：{entry}韌體，等待輸入面板速度";
                    break;
                case SessionState.EntryMismatch:
                    light = Color.red;
                    text = $"入口不符：選了「{EntryLabels[(int)_entry]}」，板子是「{entry}」韌體";
                    break;
                case SessionState.HandshakeTimeout:
                    light = Color.red;
                    text = "逾時：4 秒內沒有收到韌體回報";
                    break;
                default:
                    light = Color.gray;
                    text = "尚未連線";
                    break;
            }
        }

        /// <summary>左側卡片：依目前狀態只顯示一件事，且只有一個主要按鈕。</summary>
        private void DrawCurrentTask()
        {
            GUILayout.Label("現在要做的事", _small);
            var state = _session?.State;

            if (_session == null || state == SessionState.EntryMismatch || state == SessionState.HandshakeTimeout)
            {
                DrawConnectionCard();
                return;
            }
            if (state == SessionState.Handshaking)
            {
                GUILayout.Label("等待韌體回報身分…", _huge);
                GUILayout.Label("Nano 開埠時會重新開機，約需 2 秒。", _body);
                return;
            }
            if (_round != null)
            {
                DrawRound();
                return;
            }
            if (state == SessionState.RunningSegment || state == SessionState.AwaitingReading)
            {
                GUILayout.Label("手動單段", _title);
                DrawSegment();
                return;
            }

            DrawIdleCard();
        }

        private void DrawConnectionCard()
        {
            var connected = _session != null;

            if (connected)
            {
                GUILayout.Label(_session.State == SessionState.EntryMismatch
                    ? "請中斷連線後，改選正確的入口或換一塊板子。"
                    : "請確認板子燒的是測試韌體、baud 為 115200，再重新連線。", _title);
                PrimaryButton("中斷連線，重新選擇", Disconnect);
                return;
            }

            GUILayout.Label("連線到 Arduino", _huge);
            if (_connectError != null) GUILayout.Label(_connectError, _body);

            GUILayout.Label("1. 選擇入口", _title);
            _entry = (EntryType)GUILayout.Toolbar((int)_entry, EntryLabels, _big);

            GUILayout.Label("2. 選擇 COM port", _title);
            GUILayout.BeginHorizontal();
            if (_ports.Length == 0)
            {
                GUILayout.Label("找不到任何 COM port，請確認 USB 線。", _body);
            }
            else
            {
                _portIndex = GUILayout.Toolbar(_portIndex, _ports, _big);
            }
            if (GUILayout.Button("重新整理", _big, GUILayout.Width(Px(140)))) RefreshPorts();
            GUILayout.EndHorizontal();

            GUILayout.Space(Px(12));
            GUI.enabled = _portIndex >= 0;
            PrimaryButton("3. 連線", Connect);
            GUI.enabled = true;
        }

        private void DrawIdleCard()
        {
            if (_roundMessage != null) GUILayout.Label(_roundMessage, _body);
            DrawNextRound();

            GUILayout.Space(Px(16));
            if (GUILayout.Button(_showManualTools ? "收起手動工具" : "展開手動工具（單發、手動單段）", _big))
            {
                _showManualTools = !_showManualTools;
            }
            if (!_showManualTools) return;

            DrawSingleCommands();
            GUILayout.Space(Px(8));
            GUILayout.Label("手動單段", _title);
            DrawSegment();
        }

        // ===== 手動工具 =====

        private void DrawSegment()
        {
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
                GUILayout.Label("上一段逾時，Arduino 可能還在執行。請等它回報完成，或中斷連線後重新連線。", _body);
            }

            GUI.enabled = state == SessionState.Ready && !waitingForLateDone;
            _direction = (SegmentDirection)GUILayout.Toolbar((int)_direction, DirectionLabels, _big);
            _gapIndex = GUILayout.Toolbar(_gapIndex, GapLabels, _big);

            GUILayout.BeginHorizontal();
            GUILayout.Label("筆數", _body, GUILayout.Width(Px(60)));
            _countText = GUILayout.TextField(_countText, _input, GUILayout.Width(Px(100)));
            GUILayout.Space(Px(24));
            GUILayout.Label("目前面板速度", _body, GUILayout.Width(Px(130)));
            _startText = GUILayout.TextField(_startText, _input, GUILayout.Width(Px(120)));
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
                _inputError = "目前面板速度請輸入 1.0～12.0。";
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
            var rect = GUILayoutUtility.GetRect(100, Px(24), GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            var filled = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01((float)(elapsed / estimate)), rect.height);
            GUI.DrawTexture(filled, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.3f, 0.6f, 0.95f), 0, 0);
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
            if (expectedSpeed < PanelSpeedInput.Min - 1e-9)
            {
                GUILayout.Label($"預期低於跑步機最低速度 {PanelSpeedInput.Min:F1}，面板會停在 {PanelSpeedInput.Min:F1}：請照實際面板輸入，這段會標記「可能觸底」。", _title);
            }
            else if (expectedSpeed > PanelSpeedInput.Max + 1e-9)
            {
                GUILayout.Label($"預期高於跑步機最高速度 {PanelSpeedInput.Max:F1}：請照實際面板輸入，這段會標記「可能觸頂」。", _title);
            }

            var submit = false;
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                submit = true;
                e.Use();
            }

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(ReadingControl);
            _readingText = GUILayout.TextField(_readingText, _input, GUILayout.Width(Px(200)));
            PrimaryButton("確認（Enter）", () => submit = true);
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
                    _inputError = "請輸入 1.0～12.0 的面板速度，例如 2.7 或 27。";
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
                _small);
            if (_segmentCsvPath != null) GUILayout.Label("已寫入：" + _segmentCsvPath, _small);
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
                GUILayout.Label($"韌體回報錯誤：{_session.LastError}", _small);
            }
            else if (_session?.LastAck != null && _session.LastAck.StartsWith("ONE,"))
            {
                GUILayout.Label($"韌體已執行：{_session.LastAck}", _small);
            }
        }
    }
}
