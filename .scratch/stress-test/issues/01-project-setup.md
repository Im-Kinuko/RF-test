# 01: 專案前置設定

**What to build:** 讓 Unity 專案具備序列埠與自動化測試的基礎：能使用 `System.IO.Ports`、鍵盤輸入不會因舊版 Input API 拋例外、EditMode 測試可以執行，並有一個專屬的壓力測試場景。完成後開發者打開專案即可在 Test Runner 看到並跑綠一個空測試。

**Blocked by:** None (can start immediately)

**Status:** done

- [x] API Compatibility Level 為 .NET Framework，含 `System.IO.Ports` 的程式能編譯
- [x] Active Input Handling 為 Both
- [x] manifest 明確加入 Unity Test Framework
- [x] 建立不依賴 UnityEngine 的測試核心組件，以及引用它的 EditMode 測試組件
- [x] 一個空的 EditMode 測試在 Test Runner 中通過
- [x] 建立僅供壓力測試使用的 StressTest 場景
- [x] 不修改既有 `TreadmillController`／`RfSenderSerialController`

## 完成紀錄（2026-11-21）

- `ProjectSettings.asset`：`apiCompatibilityLevel` 6 → 3（.NET Framework）、`activeInputHandler` 1 → 2（Both）。
- `manifest.json` 明確加入 `com.unity.test-framework` 1.8.0（原本只是間接相依）。
- 新增組件 `StressTest.Core`（`noEngineReferences: true`）與 `StressTest.Tests.EditMode`（僅 Editor、`UNITY_INCLUDE_TESTS`）。
- 新增 `ProjectSetupTests`：`SerialPort.GetPortNames()` 可呼叫；核心組件不引用 UnityEngine／UnityEditor。
- 新增 `Assets/Scenes/StressTest.unity`（複製 SampleScene，僅相機與燈光）。
- 驗證：Unity 6000.6.10f1 batchmode EditMode 2/2 通過，無編譯錯誤；Unity 跑完後設定值仍保留。
- 未提交：`main` 尚無任何 commit，等待決定提交方式。
