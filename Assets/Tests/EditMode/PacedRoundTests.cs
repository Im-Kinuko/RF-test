using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    /// <summary>以 A 送法跑完整一輪：流程、各段掉在哪裡、CSV 與矩陣。</summary>
    public class PacedRoundTests
    {
        private static readonly DateTime Fixed = new DateTime(2026, 11, 27, 9, 30, 0);

        private sealed class Outputs
        {
            public readonly StringWriter Commands = new StringWriter();
            public readonly StringWriter Segments = new StringWriter();
            public readonly StringWriter Rounds = new StringWriter();

            public RoundCsvLog CreateLog() => new RoundCsvLog(Commands, Segments, Rounds, true, () => Fixed);

            /// <summary>第 row 列資料（從 0 起算）以欄名取值。</summary>
            public static string Cell(StringWriter w, int row, string column)
            {
                var lines = w.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var index = Array.IndexOf(lines[0].Split(','), column);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), "沒有欄位 " + column);
                return lines[row + 1].Split(',')[index];
            }

            public static int Rows(StringWriter w) =>
                w.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Length - 1;
        }

        /// <summary>20 筆一段、加減各一段。第 1 段第 3 筆在序列線上遺失，面板只到 2.7（少 2 格）。</summary>
        private static Outputs RunRoundWithOneLostCommand()
        {
            var outputs = new Outputs();
            var arduino = new FakeArduino { GapJitterMicros = 8 };
            arduino.LoseCommands.Add(3);
            var h = new RoundHarness(gapMs: 5, arduino: arduino, log: outputs.CreateLog(), mode: SendMode.Ack);
            h.StartAndWarmUp();
            h.CompleteSegment(2.7); // 起點 1.0 → 2.7：17 格
            h.CompleteSegment(1.0); // 2.7 → 1.0：17 格
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 2);
            h.Round.Finish();
            return outputs;
        }

        [Test]
        public void ExperimentDefaultsToSendModeA()
        {
            Assert.That(new RoundConfig(gapMs: 5, n: 30).Mode, Is.EqualTo(SendMode.Ack));
        }

        [Test]
        public void Round_SendsEveryCommandFromThePc_NeverRun()
        {
            var h = new RoundHarness(sets: 2, mode: SendMode.Ack);
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);
            h.CompleteSegment(1.0);
            h.CompleteSegment(3.0);
            h.CompleteSegment(1.0);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 2);

            Assert.That(h.Arduino.Received.Count(l => l.StartsWith("CMD,")), Is.EqualTo(80));
            Assert.That(h.Arduino.Received.Any(l => l.StartsWith("RUN,")), Is.False);
            Assert.That((h.Round.Summary.UpSent, h.Round.Summary.DownSent), Is.EqualTo((40, 40)));
            Assert.That(h.Round.Summary.TotalDrops, Is.EqualTo(0));
        }

        [Test]
        public void LossIsSplitBetweenSerialLinkAndTreadmill()
        {
            var h = new RoundHarness(mode: SendMode.Ack, arduino: LosingThirdCommand());
            h.StartAndWarmUp();

            var result = h.CompleteSegment(2.7);

            Assert.That(result.LinkLost, Is.EqualTo(1));        // 送 20、收 19
            Assert.That(result.PanelSteps, Is.EqualTo(17));
            Assert.That(result.TreadmillMissing, Is.EqualTo(2)); // 執行 19、面板 17
            Assert.That(result.Drops, Is.EqualTo(3));
        }

        private static FakeArduino LosingThirdCommand()
        {
            var arduino = new FakeArduino();
            arduino.LoseCommands.Add(3);
            return arduino;
        }

        [Test]
        public void OverReaction_ShowsAsNegativeTreadmillMissing()
        {
            var h = new RoundHarness(mode: SendMode.Ack);
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);

            var down = h.CompleteSegment(1.0); // 3.0 → 1.0：20 格
            Assert.That((down.PanelSteps, down.TreadmillMissing), Is.EqualTo((20, (int?)0)));

            var other = new RoundHarness(mode: SendMode.Ack);
            other.StartAndWarmUp();
            var up = other.CompleteSegment(3.2); // 22 格
            Assert.That((up.TreadmillMissing, up.Drops), Is.EqualTo(((int?)(-2), 2)));
        }

        [Test]
        public void RunControlMode_UsesArduinoReportedCountForTreadmillMissing()
        {
            var h = new RoundHarness(mode: SendMode.Burst);
            h.StartAndWarmUp();

            var result = h.CompleteSegment(2.8);

            Assert.That((result.LinkLost, result.TreadmillMissing), Is.EqualTo(((int?)null, (int?)2)));
        }

        [Test]
        public void SegmentsCsv_HasPerHopColumns()
        {
            var o = RunRoundWithOneLostCommand();

            string S(string column) => Outputs.Cell(o.Segments, 0, column);
            Assert.That(S("send_mode"), Is.EqualTo("A"));
            Assert.That((S("pc_sent"), S("arduino_received"), S("arduino_executed"), S("arduino_bad")),
                Is.EqualTo(("20", "19", "19", "0")));
            Assert.That((S("replies"), S("rejected"), S("no_reply")), Is.EqualTo(("19", "0", "1")));
            Assert.That((S("panel_steps"), S("link_lost"), S("treadmill_missing"), S("rebooted")),
                Is.EqualTo(("17", "1", "2", "0")));
        }

        [Test]
        public void CommandsCsv_HasARowForEverySentCommand_IncludingUnanswered()
        {
            var o = RunRoundWithOneLostCommand();

            Assert.That(Outputs.Rows(o.Commands), Is.EqualTo(40));
            string C(int row, string column) => Outputs.Cell(o.Commands, row, column);
            Assert.That((C(0, "send_mode"), C(0, "cmd_seq"), C(0, "status")), Is.EqualTo(("A", "1", "done")));
            Assert.That(C(0, "rx_us"), Is.EqualTo("999990"));
            Assert.That(C(1, "gap_us"), Is.EqualTo("5008"));
            Assert.That((C(2, "status"), C(2, "start_us"), C(2, "pc_reply_us")), Is.EqualTo(("no_reply", "", "")));
            Assert.That(C(2, "pc_send_us"), Is.Not.Empty);
            Assert.That(C(3, "gap_us"), Is.Empty); // 前一筆沒有回覆，休息時間不明
        }

        [Test]
        public void RoundsCsv_TotalsPerHop()
        {
            var o = RunRoundWithOneLostCommand();

            string R(string column) => Outputs.Cell(o.Rounds, 0, column);
            Assert.That(R("send_mode"), Is.EqualTo("A"));
            Assert.That((R("pc_sent"), R("arduino_received"), R("arduino_executed")), Is.EqualTo(("40", "39", "39")));
            Assert.That((R("link_lost"), R("treadmill_missing"), R("any_rebooted")), Is.EqualTo(("1", "5", "0")));
            Assert.That(R("status"), Is.EqualTo("completed"));
        }

        [Test]
        public void RunControlMode_LeavesPcSideColumnsEmpty()
        {
            var outputs = new Outputs();
            var h = new RoundHarness(log: outputs.CreateLog(), mode: SendMode.Burst);
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);

            string S(string column) => Outputs.Cell(outputs.Segments, 0, column);
            Assert.That((S("send_mode"), S("pc_sent"), S("link_lost"), S("treadmill_missing")),
                Is.EqualTo(("RUN", "", "", "0")));
            Assert.That(Outputs.Cell(outputs.Commands, 0, "cmd_seq"), Is.Empty);
        }

        [Test]
        public void FirmwareWithoutAckSupport_RefusesModeA()
        {
            var arduino = new FakeArduino(parameters: "press_ms=120;action_ms=120;max_run=90");
            var h = new RoundHarness(arduino: arduino, mode: SendMode.Ack);

            h.Start();

            Assert.That(h.Round.State, Is.EqualTo(RoundState.Failed));
            Assert.That(h.Round.FailureReason, Does.Contain("燒錄"));
            Assert.That(h.Sent("ONE,START"), Is.EqualTo(0));
        }

        [Test]
        public void ModeA_IsNotLimitedByTheRunBufferSize()
        {
            var arduino = new FakeArduino(parameters: "action_ms=120;max_run=32;ack=1");
            var h = new RoundHarness(n: 60, arduino: arduino, mode: SendMode.Ack);

            h.Start();

            Assert.That(h.Round.State, Is.EqualTo(RoundState.Starting));
        }

        [Test]
        public void EmergencyStopDuringModeA_StopsSendingThenStopsTheTreadmill()
        {
            var arduino = new FakeArduino();
            arduino.LoseCommands.Add(5);
            var h = new RoundHarness(arduino: arduino, mode: SendMode.Ack);
            h.StartAndWarmUp();
            h.AdvanceUntil(RoundState.AwaitingSegmentReading, maxSeconds: 0.8); // 卡在第 5 筆

            h.Round.EmergencyStop(h.Now);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 3);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.Completed));
            Assert.That(h.Round.IsVoided, Is.True);
            Assert.That(h.Arduino.Received.Last(), Is.EqualTo("ONE,STOP"));
            Assert.That(h.Arduino.Received.Count(l => l.StartsWith("CMD,")), Is.EqualTo(5));
        }

        [Test]
        public void ManualSegment_CanBeLoggedCommandByCommand()
        {
            var arduino = new FakeArduino();
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            session.StartSegment(SendMode.Ack, SegmentDirection.Up, 2, 1000, 2.0, 1.0);
            for (var now = 1.0; session.State == SessionState.RunningSegment && now < 10; now += 0.01) session.Poll(now);
            var output = new StringWriter();
            var writer = new CommandCsvWriter(output, writeHeader: true);

            writer.Write(session.SubmitReading(2.2).Segment, EntryType.Opto);

            Assert.That(Outputs.Rows(output), Is.EqualTo(2));
            Assert.That((Outputs.Cell(output, 0, "round_id"), Outputs.Cell(output, 0, "n"), Outputs.Cell(output, 0, "sets")),
                Is.EqualTo(("", "2", "")));
            Assert.That((Outputs.Cell(output, 1, "gap_ms"), Outputs.Cell(output, 1, "cmd_seq")), Is.EqualTo(("1000", "2")));
        }

        [Test]
        public void Matrix_CountsEachSendModeSeparately()
        {
            var o = RunRoundWithOneLostCommand();
            var lines = o.Rounds.ToString().Replace(",5,20,1,", ",5,30,2,")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            var a = new ConditionMatrix(3) { Mode = SendMode.Ack };
            var run = new ConditionMatrix(3) { Mode = SendMode.Burst };
            a.LoadRoundsCsv(lines);
            run.LoadRoundsCsv(lines);

            Assert.That(a.Completed(EntryType.Opto, 5, 30), Is.EqualTo(1));
            Assert.That(run.Completed(EntryType.Opto, 5, 30), Is.EqualTo(0));
        }
    }
}
