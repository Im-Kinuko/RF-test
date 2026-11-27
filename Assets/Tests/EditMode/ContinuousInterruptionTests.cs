using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    /// <summary>A 送法一輪被打斷時：資料要留下、跑步機要停、不能要人填猜的數字。</summary>
    public class ContinuousInterruptionTests
    {
        private sealed class Outputs
        {
            public readonly StringWriter Commands = new StringWriter();
            public readonly StringWriter Segments = new StringWriter();
            public readonly StringWriter Rounds = new StringWriter();

            public RoundCsvLog CreateLog() => new RoundCsvLog(Commands, Segments, Rounds, true,
                () => new DateTime(2026, 11, 28, 9, 30, 0));

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

        private static FakeArduino Losing(params int[] ordinals)
        {
            var arduino = new FakeArduino();
            foreach (var i in ordinals) arduino.LoseCommands.Add(i);
            return arduino;
        }

        private static void RunToFinalReading(RoundHarness h)
        {
            h.StartAndWarmUp();
            h.AdvanceUntil(RoundState.AwaitingFinalReading);
            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingFinalReading));
        }

        private static void ConfirmFinal(RoundHarness h, double final)
        {
            h.Round.EnterReading(final);
            h.Round.Confirm(h.Now);
            h.AdvanceUntil(RoundState.AwaitingTurnReading, maxSeconds: 5);
        }

        [Test]
        public void EarlyEnd_AsksOnlyAboutBlocksThatWereSent_AndVoidsTheRound()
        {
            var o = new Outputs();
            var h = NewRound(total: 80, arduino: Losing(23, 24, 25), outputs: o);
            RunToFinalReading(h);

            ConfirmFinal(h, 4.3);
            Assert.That(h.Round.TurnCount, Is.EqualTo(1));
            h.Round.EnterReading(4.5);
            Assert.That(h.Round.PendingResults, Has.Count.EqualTo(2));
            h.Round.Confirm(h.Now);
            h.Round.Finish();

            Assert.That(h.Round.IsVoided, Is.True);
            Assert.That(h.Round.VoidReason, Does.Contain("沒有送完"));
            Assert.That(Outputs.Rows(o.Segments), Is.EqualTo(2));
            Assert.That(Outputs.Cell(o.Rounds, 0, "status"), Is.EqualTo("voided"));
        }

        [Test]
        public void RebootWhileWaitingForTheFinalReading_IsRecorded()
        {
            var h = NewRound();
            RunToFinalReading(h);

            h.Arduino.Boot();
            h.Round.Tick(h.Now + 0.1);
            ConfirmFinal(h, 2.5);
            h.Round.EnterReading(4.5);
            h.Round.Confirm(h.Now);

            Assert.That(h.Round.Summary.AnyRebooted, Is.True);
        }

        [Test]
        public void SpaceRightAfterTheLastCommand_StillLogsTheCommands()
        {
            var o = new Outputs();
            var h = NewRound(outputs: o);
            h.StartAndWarmUp();
            // 傳送執行緒送完了，但介面還沒 Tick
            for (var now = h.Now; h.Session.State == SessionState.RunningSegment; now += 0.01) h.Session.Poll(now);

            h.Round.EmergencyStop(h.Now + 5);

            Assert.That(Outputs.Rows(o.Commands), Is.EqualTo(40));
        }

        [Test]
        public void Abandon_MidRun_StopsTheTreadmillAndKeepsTheData()
        {
            var o = new Outputs();
            var h = NewRound(arduino: Losing(5), outputs: o);
            h.StartAndWarmUp();
            h.AdvanceUntil(RoundState.AwaitingFinalReading, maxSeconds: 0.8);

            h.Round.Abandon("連線中斷");
            h.Round.Finish();

            Assert.That(h.Arduino.Received.Last(), Is.EqualTo("ONE,STOP"));
            Assert.That(Outputs.Rows(o.Commands), Is.EqualTo(5));
            string R(string column) => Outputs.Cell(o.Rounds, 0, column);
            Assert.That((R("status"), R("void_reason"), R("pc_sent")), Is.EqualTo(("voided", "連線中斷", "5")));
        }

        [Test]
        public void UnansweredStopAfterTheFinalReading_StillCollectsTheTurningPoints()
        {
            var h = NewRound();
            RunToFinalReading(h);
            h.Arduino.ErrorReply = "BUSY";

            ConfirmFinal(h, 2.5);

            Assert.That(h.Round.State, Is.EqualTo(RoundState.AwaitingTurnReading));
            Assert.That(h.Round.StopWarning, Does.Contain("BUSY"));
        }

        [Test]
        public void FinalReading_CanBeCorrectedAfterTheStop()
        {
            var h = NewRound();
            RunToFinalReading(h);
            ConfirmFinal(h, 3.5);
            h.Round.Undo();
            Assert.That((h.Round.State, h.Round.TreadmillStopped),
                Is.EqualTo((RoundState.AwaitingFinalReading, true)));

            h.Round.EnterReading(2.5);
            h.Round.Confirm(h.Now);
            h.Round.EnterReading(4.5);

            Assert.That(h.Round.PendingResults.Last().ActualEndSpeed, Is.EqualTo(2.5));
            Assert.That(h.Sent("ONE,STOP"), Is.EqualTo(1));
        }

        [Test]
        public void SerialLinkIsNotNeededOnceTheFinalReadingIsConfirmed()
        {
            var h = NewRound();
            RunToFinalReading(h);
            Assert.That(h.Round.NeedsLink, Is.True);

            ConfirmFinal(h, 2.5);
            Assert.That(h.Round.NeedsLink, Is.False);
            h.Round.EnterReading(4.5);
            h.Round.Confirm(h.Now);

            Assert.That(h.Round.NeedsLink, Is.False);
        }

        [Test]
        public void GivenUpRound_StillReportsDamagedLinesAndMissingReplies()
        {
            var arduino = new FakeArduino();
            arduino.CorruptCommands.Add(3);
            var o = new Outputs();
            var h = NewRound(arduino: arduino, outputs: o);
            RunToFinalReading(h);
            ConfirmFinal(h, 2.5);

            h.Round.GiveUpReadings();
            h.Round.Finish();

            string R(string column) => Outputs.Cell(o.Rounds, 0, column);
            Assert.That((R("arduino_bad"), R("any_report_short")), Is.EqualTo(("1", "1")));
        }
    }
}
