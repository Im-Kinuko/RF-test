# 02: 光耦單發（第一條貫穿路徑）

**What to build:** 研究人員在 StressTest 場景選擇 COM port 與「光耦」入口、按連線，看到綠燈與韌體回報的身分，再按「啟動／加速／減速／停止」任一單發按鈕，光耦即依正式版參數按壓一次。這條路徑同時建立序列協定的握手與單發指令，以及唯一的測試接縫（序列連線介面＋假 Arduino）。

**Blocked by:** 01

**Status:** done（實機驗收待做）

- [x] 光耦測試韌體（115200 baud）開機與收到 `HELLO` 時回 `READY,OPTO,<版本>,<參數>`；參數至少含按壓時間 120 ms
- [x] 韌體支援 `ONE,<START|UP|DOWN|STOP>`，回 `ACK`；腳位對應 3 加速、4 減速、5 停止、6 啟動；格式錯誤回 `ERR`
- [x] 序列連線以「送一行／收到一行／連線狀態」介面抽象，正式實作在背景執行緒讀取並交給主執行緒
- [x] 介面提供 COM port 下拉選單與重新整理、入口選擇、連線／斷線、狀態燈與連線失敗的中文原因
- [x] 所選入口與 `READY` 回報不一致時顯示警告且無法送出指令
- [x] EditMode 測試以假 Arduino 驗證：握手成功、入口不符被阻擋、`ERR` 被呈現
- [ ] 實機：接上 Nano，按「加速」時光耦動作一次

## 完成紀錄（2026-11-21）

- 核心：`ISerialLink`（送一行／收一行／IsOpen）、`StressTestSession`（`BeginHandshake`、`Poll`、`SendSingle`）、`FirmwareInfo`、`SessionState`、`SingleCommand`、`EntryType`。
- 握手：送 `HELLO`，4 秒內未收到 `READY` 即 `HandshakeTimeout`；Nano 開埠重設時 HELLO 可能遺失，開機主動送出的 `READY` 一樣會被接受。
- 入口不符時狀態為 `EntryMismatch`，`SendSingle` 拒絕且不送出任何指令。
- 正式連線 `SerialPortLink`（Runtime 組件）：115200 baud、背景執行緒讀取；開埠失敗以中文說明「被占用／找不到 port」。
- 介面 `StressTestRunner`（OnGUI）：在 StressTest 場景自動建立；入口切換、COM port 選擇與重新整理、連線燈、韌體參數、四顆單發按鈕、最後一次 ACK／ERR。
- 韌體 `Arduino/StressTest_Opto` v0.1.0：`HELLO`／`READY`／`ONE`，腳位 3 加速、4 減速、5 停止、6 啟動，按壓 120 ms。
- 測試：假 Arduino 接在 `ISerialLink` 上，涵蓋握手、入口不符、逾時、開機 READY、四種單發、未連線拒絕、ERR 呈現。
- 審查修正：序列埠寫入例外不再拋進 OnGUI，改為標記故障並顯示「序列埠已中斷」（票 03 開工前完成）。
- 驗證：EditMode 13/13 通過；韌體以 avr-g++ 語法檢查通過。尚未接 Nano 實測。
