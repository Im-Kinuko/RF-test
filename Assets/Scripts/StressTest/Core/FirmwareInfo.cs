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
    }
}
