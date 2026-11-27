# 壓力測試介面：所有畫面

這份文件依實際操作順序，列出測試工具會出現的每一個畫面，說明你會看到什麼、該做什麼。

> **截圖說明**：這些截圖是用執行檔的版面導覽模式自動拍下的。導覽模式接的是模擬韌體，跟真實韌體走同一套協定，所以不需要接 Arduino；畫面上的數字都是模擬資料。截圖解析度是 1920×1080。重新產生截圖的方法寫在文末。

> **改版提醒（2026-11-27）**：右側面板新增送法切換（A 等回覆／RUN 對照），A 送法的畫面另外顯示各檢查點筆數。截圖尚未重拍，圖中沒有這些新項目，韌體版本也是舊的；版面導覽拍的是 RUN 對照的流程。

**版面固定分成三區。** 上方是**狀態列**，有燈號、狀態、韌體參數、「中斷連線」和紅色「緊急停止（空白鍵）」。左側是**現在要做的事**，每個畫面只有一個綠色主要按鈕。右側是**條件矩陣**。

狀態燈號：⚪ 灰色是未連線，🟡 黃色是等待韌體回報，🟢 綠色是已連線，🔴 紅色是入口不符或逾時。

---

## 一、連線

### 1. 尚未連線
![尚未連線](screenshots/ui/01_disconnected.png)

依序選入口（光耦或 RF）、COM port，再按「3. 連線」。還沒選 COM port 之前，「連線」按鈕不能按。

### 2. 入口不符
![入口不符](screenshots/ui/02_entry_mismatch.png)

你選的入口和板子上的韌體不一樣，例如選了光耦，板子燒的卻是 RF。燈號變紅，左側只剩「中斷連線，重新選擇」這一個按鈕。請改選入口，或換一塊板子。

### 3. 等待韌體回報
![等待韌體回報](screenshots/ui/03_handshaking.png)

剛連上序列埠時，Nano 會重新開機，大約 2 秒後回報身分，這段時間燈號是黃的。

### 4. 連線逾時
![連線逾時](screenshots/ui/04_handshake_timeout.png)

4 秒內沒收到韌體回報。請確認板子燒的是測試韌體、baud 是 115200，然後重新連線。

---

## 二、閒置與準備

### 5. 已連線、閒置
![閒置](screenshots/ui/05_idle.png)

- 左側列出下一輪的條件，以及這一輪要送的加速、減速次數，例如「加速 60 次、減速 60 次，分 6 段」。按綠色的「**開始下一輪**」就開始。
- 如果照整體排程，下一輪應該換另一個入口，左側會用小字提醒。
- 右側矩陣顯示每個條件各完成幾輪。點某一格，可以直接跑那個條件。

### 6. 手動工具（展開）
![手動工具](screenshots/ui/06_idle_manual_tools.png)

用來檢查硬體和做前測：
- **單發測試**：送一次啟動、加速、減速或停止。
- **手動單段**：自己設定方向、間隔、筆數，跑一段。

手動單段的結果另存成 `manual_segments_*.csv`，不計入矩陣進度。

---

## 三、一輪測試

### 7. 啟動、倒數
![啟動倒數](screenshots/ui/07_round_starting.png)

已經送出啟動指令，倒數 5 秒，等跑帶穩定。上方會同時顯示本輪「已送／預計」的加速與減速次數。

### 8. 預熱
![預熱](screenshots/ui/08_round_warming_up.png)

每秒加速一次，共 10 次，把速度從開機的 1.0 帶到 2.0 km/h。畫面上的數字從 0 開始，每按完一次加 1。預熱不計分。

### 9. 輸入起點讀數
![輸入起點讀數](screenshots/ui/09_round_warmup_reading.png)

看面板（預期 2.0），把讀數打進輸入框，按 Enter。打 `20` 也可以，會自動換成 2.0。

### 10. 確認起點
![確認起點](screenshots/ui/10_round_warmup_confirm.png)

讀數正確就按 Enter，開始第一段；打錯了就按 Esc 回去改。

### 11. 段執行中
![段執行中](screenshots/ui/11_round_running_segment.png)

Arduino 正在連續送出這一段的 20 筆指令，藍色進度條是預估進度。**這段時間不要操作**；要緊急停止的話，按空白鍵。

### 12. 輸入段終點讀數
![輸入段終點讀數](screenshots/ui/12_round_segment_reading.png)

看面板，輸入這段結束時的實際速度。這張圖的例子是預期 4.0，面板顯示 3.7。

### 13. 確認：推算掉包
![確認掉包](screenshots/ui/13_round_segment_confirm.png)

畫面顯示推算結果：「面板 3.7 → 推算掉包 3 筆」，下方列出預期變化、實際變化，以及 Arduino 回報送出幾筆。確認無誤按 Enter，進入下一段。

### 14. 本輪總結
![本輪總結](screenshots/ui/14_round_summary.png)

列出每一段的起訖速度與掉包數、實際間隔、加減速的「預計」與「Arduino 實送」次數，以及紀錄檔位置。確認跑帶完全停下之後，按「**跑帶已停止，回到設定**」，這一輪才會寫入每輪 CSV 並計入矩陣。

### 15. 作廢此輪
![作廢此輪](screenshots/ui/15_round_void_reason.png)

如果這輪結果有疑慮，填寫原因後按「作廢此輪」。原始資料會保留，但不計入條件進度。在作廢原因欄位裡打字時，空白鍵就是空白，不會觸發緊急停止。

---

## 四、異常狀況

### 16. 預熱讀數不符
![預熱讀數不符](screenshots/ui/16_round_warmup_mismatch.png)

面板不是 2.0 時，有兩個選擇：
- **重新預熱**：用單發指令補足差額，不計分。
- **以實際速度為起點繼續**：直接從目前的速度開始計分。

### 17. Arduino 回報不足／預期低於最低速度
![回報不足](screenshots/ui/17_round_report_short.png)

- 「Arduino 只回報 17 筆（應送 20 筆）」：問題可能出在序列埠或韌體，不一定是跑步機掉包。
- 預期速度低於跑步機最低速度 1.0 時，面板會停在 1.0。請照面板的實際讀數輸入，這一段會被標記為「可能觸底」。

### 18. 確認畫面的旗標
![可能觸底](screenshots/ui/18_round_clamped_confirm.png)

確認畫面會用粗體標出「可能觸底／觸頂」和「Arduino 回報不足」。有這兩個旗標的段，推算出來的掉包數不可信，請回頭對照錄影，再決定要不要作廢這一輪。

### 19. 段逾時
![段逾時](screenshots/ui/19_round_segment_timeout.png)

在預估時間的 2 倍再加 2 秒內，都沒收到 Arduino 的完成回報。畫面仍然會請你輸入面板速度。

### 20. 等待遲到的完成回報
![等待遲到回報](screenshots/ui/20_round_waiting_late_done.png)

上一段逾時了，但 Arduino 可能還在送指令。等它回報完成後，系統會自動開始下一段；超過 30 秒還沒回報，這一輪就判定失敗。

### 21. 緊急停止中
![緊急停止中](screenshots/ui/21_round_aborting.png)

按下空白鍵或紅色按鈕後，系統會先要求 Arduino 中止正在跑的段，再送出停止。**2 秒內沒有回應，請直接按跑步機上的停止鍵。**

### 22. 本輪中斷
![本輪中斷](screenshots/ui/22_round_failed.png)

畫面會寫明中斷原因。已經確認過的段都寫進了紀錄，這一輪會記為作廢。請確認跑步機的狀態，再按「回到設定」。

---

## 五、手動單段

### 23. 手動單段執行中
![手動單段執行中](screenshots/ui/23_manual_running.png)

### 24. 手動單段輸入讀數
![手動單段輸入讀數](screenshots/ui/24_manual_reading.png)

### 25. 手動單段結果
![手動單段結果](screenshots/ui/25_manual_result.png)

畫面會顯示上一段的預期變化、實際變化、推算掉包數、Arduino 回報筆數和實際間隔，以及紀錄檔的路徑。

---

## 重新產生這些截圖

介面改版之後，可以用版面導覽模式重拍。先建置執行檔，再用導覽參數啟動，大約 2 分鐘後所有截圖會存進指定的資料夾，程式自動結束：

```powershell
& "<Unity 安裝路徑>\6000.6.10f1\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod StressTest.EditorTools.StressTestBuild.BuildWindows -stresstest-build-path Build\StressTest.exe
```

```powershell
Build\StressTest.exe -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -stresstest-tour Docs\screenshots\ui
```

導覽模式接的是程式內建的模擬韌體（`SimulatedArduino`），不會碰到序列埠。這個模擬韌體只有加上 `-stresstest-tour` 參數時才會啟用，正常操作時不會用到。
