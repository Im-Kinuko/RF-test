# RF／光耦 控制入口間隔壓力測試

這個 Unity 專案用來比較跑步機的兩種控制方式：光耦（V1，模擬按壓面板按鍵）和 433 MHz RF（V2，走原廠遙控通道）。測試時連續送出加速、減速指令，改變每筆指令之間的休息間隔，觀察哪一種方式在哪個間隔開始掉包。

Unity 送出測試指令給 Arduino，Arduino 用毫秒級的間隔連續送出。每送完一段，研究人員看跑步機面板、輸入速度，工具就推算出這段漏了幾筆，結果全部寫成 CSV。

> 研究背景：研究問題一，「在相同加減速指令與不同發送間隔下，兩個控制入口能否正確輸出」。外部遙控器監聽（研究問題二）不在本專案範圍內。

![介面（1280×720）](Docs/screenshots/idle_1280x720.png)

---

## 目錄

- [測試設計](#測試設計)
- [需要的東西](#需要的東西)
- [快速開始](#快速開始)
- [硬體接線與燒錄](#硬體接線與燒錄)
- [操作流程](#操作流程)
- [紀錄檔（CSV）](#紀錄檔csv)
- [掉包怎麼算](#掉包怎麼算)
- [開發者：測試與建置](#開發者測試與建置)
- [專案結構](#專案結構)
- [序列協定](#序列協定)
- [已知限制](#已知限制)
- [目前進度](#目前進度)
- [相關文件](#相關文件)

---

## 測試設計

| 項目 | 設定 |
|---|---|
| 控制入口 | 光耦、RF |
| 休息間隔 | 0／1／5／10 ms，從上一筆動作**完成後**開始計時 |
| 每輪總次數 | 40／80／120 筆 |
| 每段筆數 | 20 筆（可調），加速段和減速段輪流 |
| 速度範圍 | 跑步機開機與最低速度都是 1.0 km/h；預熱到 2.0 後，每段在 2.0 ↔ 4.0 km/h 之間來回，每次按鍵 ±0.1 km/h |
| 加減速次數 | 40 次＝加速 20、減速 20；80 次＝各 40；120 次＝各 60。畫面和每輪 CSV 都會記下預計與實際送出的次數 |
| 重複次數 | 每個條件 3 輪（可調） |
| 條件總數 | 4 種間隔 × 3 種總次數 × 2 種入口 = 24 個條件，共 72 輪 |

**一輪的流程**
1. 送出「啟動」，倒數 5 秒，等跑帶穩定。
2. 預熱：每秒加速 1 次，共 10 次，把速度從開機的 1.0 km/h 帶到 2.0 km/h。這段不計分。起點設在 2.0，是為了和最低速度 1.0 之間留 10 筆的餘裕：上一段漏掉幾筆加速時，減速段才不會先撞到底。
3. 研究人員確認面板顯示 2.0。如果不是 2.0，可以選「重新預熱」（用單發補足差額，不計分），或「以實際速度為起點繼續」。
4. 依總次數分成 2／4／6 段。每段由 Arduino 連續送 20 筆，送完後研究人員輸入面板速度，確認後進入下一段。
5. 送出「停止」，顯示本輪總結。

單筆動作沿用正式版本的設定：
- **光耦**：每次按壓 120 ms。
- **RF**：24 bit 代碼，重送 2 次，送出後再等 250 ms 讓接收端判定「放開」。一筆約 340 ms。

測試版韌體拿掉了正式版的 800／1000 ms 冷卻時間，所以指令間隔完全由測試條件決定。

---

## 需要的東西

**軟體**
- Unity **6000.6.10f1**。專案已設定 API Compatibility 為 .NET Framework、Input Handling 為 Both。
- Arduino IDE。RF 版另外需要 [rc-switch](https://github.com/sui77/rc-switch) 函式庫，開發時用的是 2.6.4 版。

**硬體**
- Arduino Nano V3.0（ATmega328P）。兩種入口各燒一塊，或同一塊輪流燒錄。
- 光耦電路，接到跑步機面板按鍵。
- 433 MHz RF 發射模組。
- 跑步機：XTERRA DT168，速度範圍 0～12 km/h。

---

## 快速開始

1. 把專案 clone 下來，用 Unity Hub 加入專案，以 6000.6.10f1 開啟。
2. 把 `Arduino/StressTest_Opto` 或 `Arduino/StressTest_RF` 燒錄到 Nano（見下一節）。
3. 在 Unity 開啟 `Assets/Scenes/StressTest.unity`，按 **Play**。測試介面會自動出現，不需要手動掛任何元件。
4. 在左側選擇入口和 COM port，按「連線」。狀態燈變成綠色就可以開始。

> 第一次正式測試前，建議先照 [Docs/BenchAcceptance.md](Docs/BenchAcceptance.md) 做一次台架驗收和前測。

---

## 硬體接線與燒錄

| 韌體 | 腳位 | 用途 |
|---|---|---|
| `StressTest_Opto` | D3 | 加速 |
| | D4 | 減速 |
| | D5 | 停止 |
| | D6 | 啟動 |
| `StressTest_RF` | D10 | RF 發射（TX） |

**燒錄設定**
- 開發板選「Arduino Nano」，處理器選「ATmega328P」。上傳失敗的話改選「ATmega328P (Old Bootloader)」。
- 序列埠 baud 是 **115200**。燒錄完成後，用序列埠監控視窗（行尾選「Newline」）應該會看到：
  - 光耦版：`READY,OPTO,0.3.0,press_ms=120;action_ms=120`
  - RF 版：`READY,RF,0.3.0,bits=24;repeat=2;release_ms=250;action_ms=340`
- **燒錄完記得關掉序列埠監控視窗**，否則 Unity 連線時會顯示「被其他程式占用」。

---

## 操作流程

介面分成三個區域。每個畫面只有一個綠色的主要按鈕，照著按就能往下走。

| 區域 | 內容 |
|---|---|
| 上方狀態列 | 連線燈號、韌體版本與參數、「中斷連線」、紅色「**緊急停止（空白鍵）**」 |
| 左側「現在要做的事」 | 依目前狀態顯示下一步：連線、開始下一輪、輸入面板速度、確認、總結 |
| 右側條件矩陣 | 24 個條件各完成幾輪；每條件輪數與每段筆數的設定；開啟紀錄資料夾 |

### 開始一輪

- **開始下一輪**（左側綠色按鈕）：依照排程，從**目前連線的入口**挑出下一個還沒完成的條件。
- **點矩陣格子**：指定某個條件，例如補做或重做。

排程規則：
1. 先把每個條件的第 1 輪都跑完，再跑第 2 輪。
2. 條件的順序是先依間隔、再依總次數。
3. 同一個條件裡，奇數輪由光耦先跑，偶數輪由 RF 先跑。

因為一次只能接一塊板子，畫面會在該換入口時提醒你，你也可以選擇不換、繼續用目前的入口。

### 輸入面板速度

- 每段結束時，畫面會用大字顯示「預期 X.X km/h」，輸入框會自動取得焦點。
- 可以輸入 `2.7`，也可以直接照面板上的數字打 `27`。
- 按 **Enter** 後會先顯示這一段推算的掉包數，再按一次 **Enter** 才會進入下一段。
- 發現打錯時，按 **Esc** 回到輸入畫面修改。

預熱後如果面板不是 1.0，有兩個選擇：「**重新預熱**」會用單發指令補足差額；「**以實際速度為起點繼續**」則直接用目前的速度當起點。

### 緊急停止

任何狀態下都可以按**空白鍵**或紅色按鈕：
- 如果某一段正在執行，會先中止，再送出停止指令。
- 其他狀態下會直接送出停止指令。
- 如果 2 秒內 Arduino 沒有回應，畫面會提示「請立即按跑步機上的停止鍵」。

緊急停止的那一輪會自動記為作廢。

### 作廢與續測

- 在總結畫面可以填寫原因後按「作廢此輪」。作廢的輪次會保留原始資料，但不計入矩陣的進度。
- 每次啟動時，工具會讀取 `TestLogs/rounds_*.csv` 並還原進度，所以可以分好幾天測。
- 上次使用的 COM port、入口、每條件輪數、每段筆數都會被記住。

### 手動工具

左側卡片下方的「展開手動工具」裡有：
- **單發**：啟動、加速、減速、停止各一次。
- **手動單段**：自訂方向、間隔和筆數，跑一段。

這兩個功能用於硬體檢查與前測，結果另外寫入 `manual_segments_*.csv`，不計入矩陣進度。

---

## 紀錄檔（CSV）

所有紀錄都寫在專案根目錄的 `TestLogs/` 底下。右側面板有「開啟紀錄資料夾」按鈕可以直接打開。

紀錄檔的格式：
- 編碼是 UTF-8 BOM，用 Excel 直接開啟，中文不會變亂碼。
- 每寫入一列就立刻存檔，所以即使 Unity 當掉，已經確認過的段也不會遺失。
- 每次啟動 Unity 會產生一組新檔，檔名帶有時間戳記。

| 檔案 | 一列代表 | 主要欄位 |
|---|---|---|
| `commands_*.csv` | 一筆指令 | `round_id`、`entry`、`gap_ms`、`segment_index`、`seq`、`direction`、`start_us`、`end_us`、`gap_us`（與上一筆的實際間隔） |
| `segments_*.csv` | 一段 | `start_speed`、`expected_end`、`actual_end`、`drops`、`reported_count`、`report_short`、`timed_out`、`possibly_clamped`、`mean_gap_us`、`max_gap_us` |
| `rounds_*.csv` | 一輪 | `entry`、`gap_ms`、`total_count`、`total_sent`、`total_drops`、`warmup_reading`、`end_reading`、`up_planned`／`down_planned`（預計加速／減速次數）、`up_sent`／`down_sent`（Arduino 實際回報送出的次數）、`warmup_up`／`warmup_down`（預熱單發，不計分）、`status`（`completed`／`voided`）、`void_reason` |
| `manual_segments_*.csv` | 手動單段的一段 | 與 `segments_*.csv` 相同，但 `round_id` 和 `segment_index` 欄位留空 |

**整理論文用的掉包表**
1. 把所有 `rounds_*.csv` 合併成一份。
2. 篩選出 `status = completed` 的列。
3. 建立樞紐分析表：列放 `entry`，欄放 `gap_ms`，值放 `total_drops` 和 `total_sent` 的加總。

詳細步驟見 [BenchAcceptance.md 的 F 節](Docs/BenchAcceptance.md#f-紀錄檔)。

> `TestLogs/` 是實驗資料，目前沒有加進 `.gitignore`。要不要納入版本控制，請依研究室的資料管理方式決定。

---

## 掉包怎麼算

```
推算掉包數 = | 預期變化 − 面板實際變化 | ÷ 0.1   （四捨五入）
```

- **預期變化**：方向 × 筆數 × 0.1 km/h。
- **實際變化**：這段結束時的讀數減去這段開始時的讀數。每段的起點用上一段結束時的**實際讀數**，不用理論值，這樣誤差才不會一段段累積下去。

段結果的旗標：

| 旗標 | 意思 |
|---|---|
| `possibly_clamped`（可能觸底／觸頂） | 起點加上預期變化後超出 1.0～12.0 km/h（跑步機最低 1.0），多出來的指令會被跑步機吃掉，這段推算出來的掉包數不可信。 |
| `report_short`（回報不足） | Arduino 回報的筆數少於應送筆數，或逾時沒有回報。問題可能出在序列埠或韌體，不一定是跑步機掉包。 |
| `timed_out` | 在「筆數 ×（單筆動作時間 + 間隔）× 2 + 2 秒」內沒有收到 `DONE`。 |

Arduino 用 `micros()` 記錄每一筆的開始和結束時間。這些時間戳會先存在 RAM，等整段跑完才一次回傳；執行期間 Arduino 不會輸出任何序列資料，避免傳輸時間把間隔拉長。

---

## 開發者：測試與建置

### 自動化測試

測試只透過一個接縫進行：`ISerialLink`，也就是「送一行、收一行」。測試用 C# 寫的 `FakeArduino` 模擬韌體，驅動整個流程，不需要接任何硬體。

- **在 Unity 中執行**：Window → General → Test Runner → EditMode → Run All。
- **用命令列執行**：先關閉開著這個專案的 Unity 編輯器，再執行：

```powershell
& "<Unity 安裝路徑>\6000.6.10f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml -logFile unity.log
```

目前共有 97 個 EditMode 測試，全部通過。

### 建置 Windows 執行檔

```powershell
& "<Unity 安裝路徑>\6000.6.10f1\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod StressTest.EditorTools.StressTestBuild.BuildWindows -stresstest-build-path Build\StressTest.exe
```

執行檔可以加參數 `-stresstest-screenshot <路徑>`：啟動後會自動截圖並結束，用來在不同解析度下檢查版面。例如：

```powershell
Build\StressTest.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -stresstest-screenshot shot.png
```

### 架構原則

- **`StressTest.Core`**：協定、狀態機、判定、CSV 都放在這裡。這個組件**不引用 UnityEngine**，可以獨立測試。
- **`StressTest.Runtime`**：只負責顯示 Core 的狀態，以及把操作轉交給 Core，不放任何判定邏輯。

---

## 專案結構

```
Arduino/
  StressTest_Opto/            光耦測試韌體
  StressTest_RF/              RF 測試韌體
Assets/
  Scenes/StressTest.unity     測試場景（按 Play 即可）
  Scripts/StressTest/
    Core/                     純 C#：協定、一輪狀態機、判定、矩陣、CSV
      StressTestSession.cs    與韌體交談：握手、單發、RUN、ABORT
      TestRound.cs            一輪流程：啟動→預熱→各段→停止→總結
      RoundPlan.cs            把總次數切成加減交替的段
      SegmentResult.cs        掉包數與觸底／觸頂旗標
      ConditionMatrix.cs      條件進度、排程、從 CSV 還原
      RoundCsvLog.cs          逐筆、每段、每輪三份 CSV
      PanelSpeedInput.cs      面板速度輸入解析
    Runtime/                  Unity 介面（OnGUI）與序列埠
      SerialPortLink.cs       ISerialLink 的正式實作
      StressTestRunner*.cs    三區版面、輪次畫面、矩陣、樣式
    Editor/StressTestBuild.cs batchmode 建置腳本
  Tests/EditMode/             FakeArduino 與 97 個測試
Docs/
  StressTest-Spec.md          規格
  BenchAcceptance.md          台架驗收與前測清單
  screenshots/                版面截圖
.scratch/stress-test/issues/  開發票券與完成紀錄（01～09）
```

---

## 序列協定

115200 baud，每行以 `\n` 結尾。兩支韌體使用同一套協定。

| Unity → Arduino | 意義 |
|---|---|
| `HELLO` | 要求韌體回報身分 |
| `ONE,<START\|UP\|DOWN\|STOP>` | 單發一次 |
| `RUN,<UP\|DOWN>,<n>,<gap_ms>` | 連續送 n 筆（n ≤ 32）；每筆動作完成後休息 gap_ms 毫秒 |
| `ABORT` | 中止執行中的 RUN |

| Arduino → Unity | 意義 |
|---|---|
| `READY,<OPTO\|RF>,<版本>,<參數>` | 開機時或回應 HELLO |
| `ACK,<原指令>` | 已接受指令 |
| `T,<i>,<start_us>,<end_us>` | 第 i 筆的開始與結束時間；整段跑完後才一次送出 |
| `DONE,<n>` | RUN 完成 |
| `ABORTED,<n>` | RUN 被中止，n 為中止前已完成的筆數 |
| `ERR,<原因>` | 例如 `BAD_COMMAND`、`BAD_RUN`、`BUSY`（RUN 執行期間收到其他指令）、`UNKNOWN` |

---

## 已知限制

- **以段為單位判定**：跑步機本身不會回報收到哪一筆指令，只能從每段的面板讀數推算。如果同一段裡同時「漏一筆」又「多一筆」，兩者會互相抵銷，看不出來。要逐筆核對只能看錄影。
- **RF 的 `action_ms=340` 是推算值**：由 frame 長度和釋放時間算出，只用於估計逾時和顯示進度條。實際耗時請看 CSV 裡的時間戳，並在台架驗收時填入實測值。
- **面板輸入的規則**：沒有小數點、且數值 ≥ 10 的整數會自動除以 10（例如 `27` → 2.7）。所以要輸入 10～12 km/h 時，請打成 `10.0`。本測試的速度範圍是 2.0～4.0，平常不會遇到。

---

## 目前進度

| 部分 | 狀態 |
|---|---|
| 核心流程、介面、CSV、條件矩陣、緊急停止 | 完成，97 個自動化測試通過 |
| 兩支韌體 | 已用 avr-g++ 做過語法檢查，**尚未燒錄實測** |
| 台架驗收與前測 | 清單已完成，**等待接上硬體執行** |
| 連線後各畫面的版面 | 只截過未連線的畫面，其他畫面要接上 Arduino 才看得到 |

---

## 相關文件

- [Docs/StressTest-Spec.md](Docs/StressTest-Spec.md)：完整規格，包含使用者故事、實作決策、測試策略
- [Docs/BenchAcceptance.md](Docs/BenchAcceptance.md)：台架驗收與前測的逐項清單
- [.scratch/stress-test/issues/](.scratch/stress-test/issues/)：每張開發票的驗收項目與完成紀錄
