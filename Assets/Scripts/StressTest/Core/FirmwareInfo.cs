using System.Collections.Generic;

namespace StressTest.Core
{
    /// <summary>韌體在 READY 行回報的身分與單筆動作參數。</summary>
    public sealed class FirmwareInfo
    {
        public FirmwareInfo(EntryType entry, string version, IReadOnlyDictionary<string, string> parameters)
        {
            Entry = entry;
            Version = version;
            Parameters = parameters;
        }

        public EntryType Entry { get; }
        public string Version { get; }
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>這個韌體能不能用指定送法跑一段 count 筆；可以時回傳 null，否則回傳原因。</summary>
        public string CheckSegment(SendMode mode, int count)
        {
            if (mode == SendMode.Ack && !SupportsAck)
            {
                return $"韌體 v{Version} 不支援逐筆送出（A 送法）。請燒錄最新的測試韌體。";
            }
            var max = mode == SendMode.Ack ? SegmentRun.MaxPacedCount : MaxRunCount;
            if (count >= 1 && count <= max) return null;
            return mode == SendMode.Burst && count > max
                ? $"韌體 v{Version} 每段最多 {max} 筆，這一段需要 {count} 筆。請燒錄最新的測試韌體。"
                : $"筆數請輸入 1～{max}。";
        }

        /// <summary>沒有回報 max_run 的舊版韌體（0.3.0 以前）每段上限 32 筆。</summary>
        public const int LegacyMaxRunCount = 32;

        /// <summary>韌體支援逐筆指令 ZERO／CMD／STAT（A 送法；READY 的 ack=1）。</summary>
        public bool SupportsAck => Parameters.TryGetValue("ack", out var text) && text == "1";

        /// <summary>韌體一次 RUN 最多能送的筆數（READY 的 max_run）。</summary>
        public int MaxRunCount =>
            Parameters.TryGetValue("max_run", out var text) && int.TryParse(text, out var max) && max > 0
                ? max
                : LegacyMaxRunCount;
    }
}
