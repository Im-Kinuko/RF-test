using System.Globalization;

namespace StressTest.Core
{
    /// <summary>
    /// 逐筆指令與回覆的行檢查碼：「內容*XX」，XX 是內容所有位元組 XOR 的兩位大寫十六進位。
    /// 用來分辨在序列線上毀損的行，例如接收緩衝溢位把兩行黏在一起。
    /// </summary>
    public static class LineChecksum
    {
        public static string Wrap(string payload) =>
            payload + "*" + Compute(payload).ToString("X2", CultureInfo.InvariantCulture);

        /// <summary>檢查碼存在且相符時取出內容。</summary>
        public static bool TryUnwrap(string line, out string payload)
        {
            payload = null;
            var star = line.LastIndexOf('*');
            if (star < 0 || line.Length - star != 3) return false;
            if (!int.TryParse(line.Substring(star + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out var expected))
            {
                return false;
            }

            var body = line.Substring(0, star);
            if (Compute(body) != expected) return false;
            payload = body;
            return true;
        }

        private static int Compute(string text)
        {
            var x = 0;
            foreach (var ch in text) x ^= (byte)ch;
            return x;
        }
    }
}
