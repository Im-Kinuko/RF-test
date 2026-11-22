using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace StressTest.EditorTools
{
    /// <summary>
    /// 以 batchmode 建置只含 StressTest 場景的 Windows 執行檔：
    /// Unity.exe -batchmode -quit -projectPath ... -executeMethod StressTest.EditorTools.StressTestBuild.BuildWindows
    ///           -stresstest-build-path &lt;輸出 .exe 路徑&gt;
    /// </summary>
    public static class StressTestBuild
    {
        public static void BuildWindows()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-stresstest-build-path");
            if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("缺少 -stresstest-build-path <輸出路徑>");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/StressTest.unity" },
                locationPathName = args[i + 1],
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
