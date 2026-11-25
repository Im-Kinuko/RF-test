using System.Globalization;

namespace StressTest.Core
{
    /// <summary>解析研究人員輸入的跑步機面板速度（km/h）。</summary>
    public static class PanelSpeedInput
    {
        /// <summary>跑步機（XTERRA DT168）的最低速度，也是開機速度。觸底判斷也以此為準。</summary>
        public const double Min = 1.0;
        public const double Max = 12.0;

        /// <summary>
        /// 接受「1」「1.0」；沒有小數點且 ≥ 10 的整數視為照面板數字直接輸入，自動除以 10（「27」→ 2.7）。
        /// 超出 1.0～12.0 的值一律拒絕。
        /// </summary>
        public static bool TryParse(string text, out double speed)
        {
            speed = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            text = text.Trim();
            if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                return false;

            if (text.IndexOf('.') < 0 && value >= 10) value /= 10.0;
            if (value < Min - 1e-9 || value > Max + 1e-9) return false;

            speed = value;
            return true;
        }
    }
}
