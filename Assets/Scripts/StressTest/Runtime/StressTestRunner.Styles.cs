using System;
using UnityEngine;

namespace StressTest.Runtime
{
    /// <summary>
    /// 依視窗大小等比縮放的樣式（1920×1080 為 1 倍），以及主要／危險按鈕。
    /// 規則：每個畫面只有一個綠色主要按鈕；紅色緊急停止永遠在狀態列。
    /// </summary>
    public sealed partial class StressTestRunner
    {
        private float _scale = -1f;
        private GUIStyle _big, _primary, _danger, _cell, _title, _body, _small, _huge, _input, _panel;

        private float Px(float value) => Mathf.Round(value * _scale);

        private void EnsureStyles()
        {
            var scale = Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 0.6f, 1.25f);
            // 依視窗等比縮放，但最小只縮到 0.8 倍：站在跑步機旁仍看得清楚，欄寬也跟文字一起縮
            scale = Mathf.Max(scale, 0.8f);
            if (_big != null && Mathf.Approximately(scale, _scale)) return;
            _scale = scale;

            int Size(float v) => Mathf.RoundToInt(v * scale);

            _big = new GUIStyle(GUI.skin.button) { fontSize = Size(20), fixedHeight = Size(46), wordWrap = true };
            _primary = new GUIStyle(_big) { fontSize = Size(26), fontStyle = FontStyle.Bold, fixedHeight = Size(68) };
            _danger = new GUIStyle(_big) { fontSize = Size(24), fontStyle = FontStyle.Bold, fixedHeight = Size(64) };
            _cell = new GUIStyle(_big) { fontSize = Size(17), fixedHeight = Size(64) };
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = Size(22), fontStyle = FontStyle.Bold, wordWrap = true,
                margin = new RectOffset(4, 4, Size(8), Size(6))
            };
            _body = new GUIStyle(GUI.skin.label) { fontSize = Size(18), wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = Size(15), wordWrap = true };
            _huge = new GUIStyle(GUI.skin.label) { fontSize = Size(38), fontStyle = FontStyle.Bold, wordWrap = true };
            _input = new GUIStyle(GUI.skin.textField) { fontSize = Size(30), fixedHeight = Size(52) };
            // 實心深色面板，避免場景天空盒透出來降低文字對比
            // 行高依西文字型計算，中文字底部會超出標籤框；改成不裁切
            foreach (var label in new[] { _title, _body, _small, _huge }) label.clipping = TextClipping.Overflow;

            _panel = new GUIStyle(GUI.skin.box) { normal = { background = SolidTexture(new Color(0.13f, 0.14f, 0.16f)) } };
        }

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        /// <summary>綠色主要按鈕。每個畫面只用一次。</summary>
        private void PrimaryButton(string label, Action onClick)
        {
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.4f);
            if (GUILayout.Button(label, _primary)) onClick();
            GUI.backgroundColor = previous;
        }

        private void DangerButton(string label, Action onClick, float width)
        {
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            var enabled = GUI.enabled;
            GUI.enabled = _session != null;
            if (GUILayout.Button(label, _danger, GUILayout.Width(width))) onClick();
            GUI.enabled = enabled;
            GUI.backgroundColor = previous;
        }

        // ===== 版面截圖（驗收用）=====
        // 以 -stresstest-screenshot <路徑> 啟動執行檔時，畫面穩定後截圖並結束。

        private string _screenshotPath;
        private int _screenshotFrame;

        private void ReadScreenshotArgument()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-stresstest-screenshot");
            if (i >= 0 && i + 1 < args.Length) _screenshotPath = args[i + 1];
        }

        private void UpdateScreenshot()
        {
            if (_screenshotPath == null) return;
            _screenshotFrame++;
            if (_screenshotFrame == 30) ScreenCapture.CaptureScreenshot(_screenshotPath);
            if (_screenshotFrame == 60) Application.Quit();
        }
    }
}
