namespace StressTest.Core
{
    /// <summary>送法：下一筆指令什麼時候送。定義見 Docs/ExperimentDesign.md 第 4 節。</summary>
    public enum SendMode
    {
        /// <summary>A：電腦逐筆送，收到完成回覆後等 gap 再送下一筆。</summary>
        Ack,
        /// <summary>RUN 對照：電腦只送一行，Arduino 自己連續送完整段。</summary>
        Burst
    }

    /// <summary>送法在 CSV、輪次 ID 與畫面上的代碼。</summary>
    public static class SendModeCodes
    {
        public static string Code(this SendMode mode) => mode == SendMode.Ack ? "A" : "RUN";

        public static bool TryParse(string code, out SendMode mode)
        {
            mode = code == "A" ? SendMode.Ack : SendMode.Burst;
            return code == "A" || code == "RUN";
        }
    }
}
