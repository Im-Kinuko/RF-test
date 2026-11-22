namespace StressTest.Core
{
    /// <summary>
    /// 與測試韌體之間的逐行序列連線。正式版包 SerialPort，測試用假 Arduino。
    /// </summary>
    public interface ISerialLink
    {
        bool IsOpen { get; }

        /// <summary>送出一行（實作負責補上行尾）。</summary>
        void WriteLine(string line);

        /// <summary>取出一行已收到的資料（不含行尾）；沒有時回傳 false。</summary>
        bool TryReadLine(out string line);
    }
}
