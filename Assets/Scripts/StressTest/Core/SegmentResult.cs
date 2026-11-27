using System;

namespace StressTest.Core
{
    /// <summary>一段結束、研究人員輸入面板速度後的判定。</summary>
    public sealed class SegmentResult
    {
        public SegmentResult(SegmentRun segment, double actualEndSpeed)
        {
            Segment = segment;
            ActualEndSpeed = actualEndSpeed;
        }

        public SegmentRun Segment { get; }
        public double ActualEndSpeed { get; }

        public double ExpectedChange => Segment.ExpectedChange;

        /// <summary>以本段起點讀數計算的實際變化，不以理論起點計算，避免誤差累積。</summary>
        public double ActualChange => ActualEndSpeed - Segment.StartSpeed;

        /// <summary>面板往指令方向移動的格數（每格 0.1 km/h）；往反方向時為負。</summary>
        public int PanelSteps =>
            (int)Math.Round(ActualChange / SegmentRun.SpeedStep, MidpointRounding.AwayFromZero)
            * (Segment.Direction == SegmentDirection.Up ? 1 : -1);

        /// <summary>電腦到 Arduino 掉的筆數＝送出 − 收到；只有 A 送法且問得到計數時有值。</summary>
        public int? LinkLost =>
            Segment.Mode == SendMode.Ack ? Segment.PcSent - Segment.ArduinoReceived : null;

        /// <summary>Arduino 之後掉的筆數＝執行 − 面板格數；負值是多反應。執行數不明時為 null。</summary>
        public int? TreadmillMissing =>
            (Segment.Mode == SendMode.Ack ? Segment.ArduinoExecuted : Segment.ReportedCount) - PanelSteps;

        /// <summary>
        /// 「可能觸底／觸頂」：以本段起點讀數加上預期變化會超出面板範圍，
        /// 多出的指令會被跑步機吃掉，推算的掉包數不可信。
        /// </summary>
        public bool PossiblyClamped
        {
            get
            {
                var theoreticalEnd = Segment.StartSpeed + ExpectedChange;
                return theoreticalEnd < PanelSpeedInput.Min - 1e-9 || theoreticalEnd > PanelSpeedInput.Max + 1e-9;
            }
        }

        /// <summary>推算掉包數 = |預期變化 − 實際變化| ÷ 0.1，四捨五入。</summary>
        public int Drops => (int)Math.Round(Math.Abs(ExpectedChange - ActualChange) / SegmentRun.SpeedStep, MidpointRounding.AwayFromZero);
    }
}
