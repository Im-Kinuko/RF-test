using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    /// <summary>
    /// A 送法的一輪：整輪不停地逐筆送出、每 20 筆換方向；
    /// 送完輸入最後速度（接著停止跑步機），再看錄影輸入各轉向點讀數。
    /// </summary>
    public class ContinuousRoundTests
    {
        private static readonly DateTime Fixed = new DateTime(2026, 11, 28, 9, 30, 0);

        private sealed class Outputs
        {
            public readonly StringWriter Commands = new StringWriter();
            public readonly StringWriter Segments = new StringWriter();
            public readonly StringWriter Rounds = new StringWriter();

            public RoundCsvLog CreateLog() => new RoundCsvLog(Commands, Segments, Rounds, true, () => Fixed);

            public static string Cell(StringWriter w, int row, string column)
            {
                var lines = Lines(w);
                var index = Array.IndexOf(lines[0].Split(','), column);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), "沒有欄位 " + column);
                return lines[row + 1].Split(',')[index];
            }

            public static int Rows(StringWriter w) => Lines(w).Length - 1;

            private static string[] Lines(StringWriter w) =>
                w.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static RoundHarness NewRound(int total = 40, FakeArduino arduino = null, Outputs outputs = null) =>
            new RoundHarness(gapMs: 5, totalCount: total, arduino: arduino, log: outputs?.CreateLog(),
                baseSpeed: 2.5, mode: SendMode.Ack);

        /// <summary>預熱確認 2.5 後，跑到整輪送完。</summary>
        private static void RunToFinalReading(RoundHarness h)
        {
            h.StartAndWarmUp();
            h.AdvanceUntil(RoundState.AwaitingFinalReading);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingFinalReading));
        }

        /// <summary>輸入最後速度並確認，等停止指令回覆。</summary>
        private static void EnterFinal(RoundHarness h, double final)
        {
            h.Round.EnterReading(final);
            h.Round.Confirm(h.Now);
            h.AdvanceUntil(h.Round.TurnCount > 0 ? RoundState.AwaitingTurnReading : RoundState.ConfirmingReadings, 3);
        }

        [Test]
        public void SendsEveryCommandWithoutPausing_ChangingDirectionEveryTwenty()
        {
            var h = NewRound(total: 120);

            RunToFinalReading(h);

            var directions = h.Arduino.Received.Where(l => l.StartsWith("CMD,"))
                .Select(l => l.Split(',', '*')[2]).ToArray();
            var expected = Enumerable.Range(0, 120).Select(i => i / 20 % 2 == 0 ? "UP" : "DOWN").ToArray();
            Assert.That(directions, Is.EqualTo(expected));
            Assert.That(h.Sent("ZERO"), Is.EqualTo(1));
            Assert.That(h.Sent("STAT"), Is.EqualTo(1));
            Assert.That(h.Sent("ONE,STOP"), Is.EqualTo(0));
        }

        [Test]
        public void DirectionChange_HasNoExtraWait()
        {
            var h = NewRound(arduino: new FakeArduino { GapJitterMicros = 8 });
            RunToFinalReading(h);

            var second = h.Session.CurrentRun.Blocks[1];

            Assert.That(second.Gaps.Count(), Is.EqualTo(20)); // 含換方向那一次
            Assert.That(second.Gaps.First(), Is.EqualTo(5008));
        }

        [Test]
        public void AsksFinalSpeedFirst_ThenStopsTheTreadmill_ThenAsksTurningPoints()
        {
            var h = NewRound();
            RunToFinalReading(h);
            Assert.That(h.Round.ExpectedPanelSpeed, Is.EqualTo(2.5).Within(1e-9));

            EnterFinal(h, 2.4);

            Assert.That(h.Sent("ONE,STOP"), Is.EqualTo(1));
            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingTurnReading));
            Assert.That((h.Round.TurnIndex, h.Round.TurnCount), Is.EqualTo((0, 1)));
            Assert.That(h.Round.ExpectedPanelSpeed, Is.EqualTo(4.5).Within(1e-9));
        }

        [Test]
        public void TurningPointReadings_GiveDropsForEachBlock()
        {
            var h = NewRound();
            RunToFinalReading(h);
            EnterFinal(h, 2.4);

            h.Round.EnterReading(4.3); // 加速 20 筆只到 4.3

            Assert.That(h.Round.State, Is.EqualTo(RoundState.ConfirmingReadings));
            Assert.That(h.Round.PendingResults.Select(r => (r.Segment.Direction, r.Drops)), Is.EqualTo(new[]
            {
                (SegmentDirection.Up, 2),   // 2.5 → 4.3
                (SegmentDirection.Down, 1)  // 4.3 減 20 筆應到 2.3，實際 2.4
            }));
            h.Round.Confirm(h.Now);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.Completed));
            Assert.That(h.Round.Summary.TotalDrops, Is.EqualTo(3));
        }

        [Test]
        public void OneHundredTwenty_AsksFiveTurningPoints_EachExpectedFromThePreviousReading()
        {
            var h = NewRound(total: 120);
            RunToFinalReading(h);
            EnterFinal(h, 2.5);
            Assert.That(h.Round.TurnCount, Is.EqualTo(5));

            h.Round.EnterReading(4.2);

            Assert.That(h.Round.TurnIndex, Is.EqualTo(1));
            Assert.That(h.Round.ExpectedPanelSpeed, Is.EqualTo(2.2).Within(1e-9));
        }

        [Test]
        public void Undo_GoesBackOneReading()
        {
            var h = NewRound(total: 80);
            RunToFinalReading(h);
            h.Round.EnterReading(2.4);
            h.Round.Undo();
            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingFinalReading));
            EnterFinal(h, 2.5);
            h.Round.EnterReading(4.0);
            h.Round.EnterReading(2.0);
            h.Round.EnterReading(4.0);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.ConfirmingReadings));

            h.Round.Undo();
            h.Round.Undo();

            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingTurnReading));
            Assert.That(h.Round.TurnReadings, Is.EqualTo(new[] { 4.0 }));
            Assert.That(h.Round.ExpectedPanelSpeed, Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void CommandsAreLoggedAsSoonAsTheRunEnds_SegmentsAfterTheReadings()
        {
            var o = new Outputs();
            var h = NewRound(outputs: o);

            RunToFinalReading(h);

            Assert.That(Outputs.Rows(o.Commands), Is.EqualTo(40));
            Assert.That(Outputs.Rows(o.Segments), Is.EqualTo(0));
            string C(int row, string column) => Outputs.Cell(o.Commands, row, column);
            Assert.That((C(20, "segment_index"), C(20, "seq"), C(20, "direction")), Is.EqualTo(("1", "0", "DOWN")));
            Assert.That(C(20, "gap_us"), Is.Not.Empty);
            Assert.That(C(0, "gap_us"), Is.Empty);
        }

        [Test]
        public void SegmentAndRoundRows_AreMarkedContinuous()
        {
            var o = new Outputs();
            var h = NewRound(outputs: o);
            RunToFinalReading(h);
            EnterFinal(h, 2.4);
            h.Round.EnterReading(4.3);
            h.Round.Confirm(h.Now);
            h.Round.Finish();

            Assert.That(Outputs.Rows(o.Segments), Is.EqualTo(2));
            string S(int row, string column) => Outputs.Cell(o.Segments, row, column);
            Assert.That((S(0, "flow"), S(0, "start_speed"), S(0, "actual_end"), S(0, "drops")),
                Is.EqualTo(("continuous", "2.5", "4.3", "2")));
            Assert.That((S(1, "start_speed"), S(1, "actual_end"), S(1, "drops")), Is.EqualTo(("4.3", "2.4", "1")));
            Assert.That((Outputs.Cell(o.Rounds, 0, "flow"), Outputs.Cell(o.Rounds, 0, "end_reading")),
                Is.EqualTo(("continuous", "2.4")));
        }

        [Test]
        public void CommandLostInTheSecondBlock_IsAttributedToThatBlock()
        {
            var arduino = new FakeArduino();
            arduino.LoseCommands.Add(23);
            var h = NewRound(arduino: arduino);
            RunToFinalReading(h);
            EnterFinal(h, 2.6);
            h.Round.EnterReading(4.5);

            var results = h.Round.PendingResults;

            Assert.That(results.Select(r => r.LinkLost), Is.EqualTo(new int?[] { 0, 1 }));
            Assert.That(results.Select(r => r.TreadmillMissing), Is.EqualTo(new int?[] { 0, 0 }));
        }

        [Test]
        public void LostReplyOnly_BlockStillKnowsTheCommandWasExecuted()
        {
            var arduino = new FakeArduino();
            arduino.LoseReplies.Add(5);
            var h = NewRound(arduino: arduino);
            RunToFinalReading(h);

            var blocks = h.Session.CurrentRun.Blocks;

            Assert.That(blocks.Select(b => b.ArduinoExecuted), Is.EqualTo(new int?[] { 20, 20 }));
        }

        [Test]
        public void LostCommandAndLostReply_BlockCountsUnknownButRoundTotalsKnown()
        {
            var arduino = new FakeArduino();
            arduino.LoseReplies.Add(5);
            arduino.LoseCommands.Add(25);
            var o = new Outputs();
            var h = NewRound(arduino: arduino, outputs: o);
            RunToFinalReading(h);
            EnterFinal(h, 2.6);
            h.Round.EnterReading(4.5);
            h.Round.Confirm(h.Now);
            h.Round.Finish();

            Assert.That(h.Round.Summary.Results.Select(r => r.LinkLost), Is.EqualTo(new int?[] { null, null }));
            string R(string column) => Outputs.Cell(o.Rounds, 0, column);
            Assert.That((R("pc_sent"), R("arduino_received"), R("link_lost"), R("treadmill_missing")),
                Is.EqualTo(("40", "39", "1", "0"))); // 執行 39，面板 20 + 19 = 39 格
        }

        [Test]
        public void ArduinoRebootMidRun_EndsTheRunAndFlagsTheRemainingBlocks()
        {
            var arduino = new FakeArduino();
            arduino.LoseCommands.Add(3);
            var h = NewRound(total: 80, arduino: arduino);
            h.StartAndWarmUp();
            h.AdvanceUntil(RoundState.AwaitingFinalReading, maxSeconds: 0.6); // 卡在第 3 筆
            arduino.Boot();

            h.AdvanceUntil(RoundState.AwaitingFinalReading, maxSeconds: 1);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingFinalReading));
            Assert.That(h.Session.CurrentRun.Blocks.Select(b => b.Rebooted), Is.All.True);
        }

        [Test]
        public void EmergencyStopDuringTheRun_VoidsItAndSkipsReadings_ButKeepsCommandLog()
        {
            var arduino = new FakeArduino();
            arduino.LoseCommands.Add(5);
            var o = new Outputs();
            var h = NewRound(arduino: arduino, outputs: o);
            h.StartAndWarmUp();
            h.AdvanceUntil(RoundState.AwaitingFinalReading, maxSeconds: 0.8);

            h.Round.EmergencyStop(h.Now);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 3);

            Assert.That((h.Round.State, h.Round.IsVoided), Is.EqualTo((RoundState.Completed, true)));
            Assert.That(h.Arduino.Received.Last(), Is.EqualTo("ONE,STOP"));
            Assert.That(Outputs.Rows(o.Commands), Is.EqualTo(5));
        }

        [Test]
        public void EmergencyStopWhileEnteringTurningPoints_SendsStopButKeepsTheRound()
        {
            var h = NewRound();
            RunToFinalReading(h);
            EnterFinal(h, 2.5);

            h.Round.EmergencyStop(h.Now);

            Assert.That(h.Sent("ONE,STOP"), Is.EqualTo(2));
            Assert.That((h.Round.State, h.Round.IsVoided), Is.EqualTo((RoundState.AwaitingTurnReading, false)));
        }

        [Test]
        public void UnreadableTurningPoint_VoidsTheRoundWithoutGuessing()
        {
            var o = new Outputs();
            var h = NewRound(total: 80, outputs: o);
            RunToFinalReading(h);
            EnterFinal(h, 2.5);
            h.Round.EnterReading(4.5);

            h.Round.GiveUpReadings();
            h.Round.Finish();

            Assert.That((h.Round.State, h.Round.IsVoided), Is.EqualTo((RoundState.Completed, true)));
            Assert.That(Outputs.Rows(o.Segments), Is.EqualTo(0));
            string R(string column) => Outputs.Cell(o.Rounds, 0, column);
            Assert.That((R("status"), R("pc_sent"), R("treadmill_missing")), Is.EqualTo(("voided", "80", "")));
        }

        [Test]
        public void ModeA_StartsAtTwoPointFive_RunControlAtTwo()
        {
            var a = new RoundConfig(gapMs: 0, totalCount: 40);
            var run = new RoundConfig(gapMs: 0, totalCount: 40) { Mode = SendMode.Burst };

            Assert.That((a.BaseSpeed, a.WarmupCount, a.IsContinuous), Is.EqualTo((2.5, 15, true)));
            Assert.That((run.BaseSpeed, run.WarmupCount, run.IsContinuous), Is.EqualTo((2.0, 10, false)));
            Assert.That(RoundConfig.MaxSegmentSizeFor(SendMode.Ack), Is.EqualTo(35));
            Assert.That(RoundConfig.MaxSegmentSizeFor(SendMode.Burst), Is.EqualTo(40));
        }

        [Test]
        public void Matrix_CountsOnlyContinuousRoundsForModeA()
        {
            var lines = new[]
            {
                "round_id,entry,gap_ms,total_count,status,send_mode,flow",
                "R1,OPTO,0,40,completed,A,continuous",
                "R2,OPTO,0,40,completed,A,segmented",
                "R3,OPTO,0,40,completed,RUN,segmented",
            };
            var withoutFlow = new[]
            {
                "round_id,entry,gap_ms,total_count,status,send_mode",
                "R4,OPTO,0,40,completed,A",
                "R5,OPTO,0,40,completed,RUN",
            };
            var a = new ConditionMatrix(3) { Mode = SendMode.Ack };
            var run = new ConditionMatrix(3) { Mode = SendMode.Burst };
            foreach (var m in new[] { a, run })
            {
                m.LoadRoundsCsv(lines);
                m.LoadRoundsCsv(withoutFlow);
            }

            Assert.That(a.Completed(EntryType.Opto, 0, 40), Is.EqualTo(1));
            Assert.That(run.Completed(EntryType.Opto, 0, 40), Is.EqualTo(2));
        }
    }
}
