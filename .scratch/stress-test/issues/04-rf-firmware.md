# 04: RF 測試韌體

**What to build:** 研究人員把 Nano 換成 RF 版韌體後，Unity 端不需任何修改即可連線（入口顯示 RF）、單發與跑一段。RF 單筆動作沿用正式版：四組 24 bit 代碼、重送 2 次、每筆後 250 ms 釋放；移除 800／1000 ms 冷卻、RF 接收功能與其 20／50 ms 延遲。

**Blocked by:** 03

**Status:** done（ABORT 移至票 06，實機驗收待做）

- [ ] 協定與光耦版一致：`HELLO`／`READY,RF,…`／`ONE`／`RUN`／`ABORT`／`T`／`DONE`／`ABORTED`／`ERR`
- [x] `READY` 參數含重送次數與釋放時間
- [x] 無任何軟體冷卻，間隔完全由 `RUN` 的 gap_ms 決定
- [x] 不啟用 RF 接收
- [ ] 實機：Unity 選 RF 入口連線後，單發「加速」使跑步機面板 +0.1

## 完成紀錄（2026-11-22）

- 韌體 `Arduino/StressTest_RF` v0.2.0，協定與 `StressTest_Opto` v0.2.0 相同：`HELLO`／`READY,RF,…`／`ONE`／`RUN`／`T`／`DONE`／`ERR`。
- 單筆動作沿用正式版：四組 24 bit 代碼、TX 腳位 10、`setRepeatTransmit(2)`，送出後 250 ms 釋放。
- 移除：800 ms 全域冷卻、1000 ms 同指令冷卻、RF 接收與其 20／50 ms 延遲。
- `READY` 參數：`bits=24;repeat=2;release_ms=250;action_ms=340`。`action_ms` 是推算值（protocol 1 每 frame 128 × 350 µs ≈ 44.8 ms，× 2 + 250 ms），只用於逾時與進度條；實際耗時以 CSV 時間戳為準。
- 未完成：`ABORT`／`ABORTED`。光耦版目前也沒有，依票券切分於票 06 兩支韌體一起加入，因此「協定與光耦版一致」一項暫不勾選。
- Unity 端未修改；EditMode 44/44 通過。韌體以 avr-g++ 搭配 rc-switch 2.6.4 語法檢查，無錯誤無警告；程式碼審查無發現。尚未接 Nano 實測。
