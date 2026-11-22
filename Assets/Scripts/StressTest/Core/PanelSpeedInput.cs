using System.Globalization;

namespace StressTest.Core
{
    /// <summary>解析研究人員輸入的跑步機面板速度（km/h）。</summary>
    public static class PanelSpeedInput
    {
        public const double Min = 0.1;
        public const double Max = 12.0;

        /// <summary>
        /// 接受「1」「1.0」；沒有小數點且 ≥ 10 的整數視為照面板數字直接輸入，自動除以 10（「27」→ 2.7）。
        /// 超出 0.1～12.0 的值一律拒絕。
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
