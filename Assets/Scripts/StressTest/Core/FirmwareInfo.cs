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

        /// <summary>沒有回報 max_run 的舊版韌體（0.3.0 以前）每段上限 32 筆。</summary>
        public const int LegacyMaxRunCount = 32;

        /// <summary>韌體一次 RUN 最多能送的筆數（READY 的 max_run）。</summary>
        public int MaxRunCount =>
            Parameters.TryGetValue("max_run", out var text) && int.TryParse(text, out var max) && max > 0
                ? max
                : LegacyMaxRunCount;
    }
}
