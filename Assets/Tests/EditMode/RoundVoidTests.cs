using System;
using System.IO;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class RoundVoidTests
    {
        private StringWriter _rounds;

        [SetUp]
        public void SetUp() => _rounds = new StringWriter();

        private RoundHarness NewHarness(FakeArduino arduino = null, int totalCount = 40, double? bootSpeed = null)
        {
            var log = new RoundCsvLog(new StringWriter(), new StringWriter(), _rounds, writeHeaders: false,
                () => new DateTime(2026, 11, 22, 10, 0, 0));
            return new RoundHarness(gapMs: 5, totalCount: totalCount, arduino: arduino, log: log, bootSpeed: bootSpeed);
        }

        private string RoundRows() => _rounds.ToString().TrimEnd();

        private static void CompleteNormally(RoundHarness h)
        {
            h.StartAndWarmUp();
            h.CompleteSegment(3.0);
            h.CompleteSegment(1.0);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 1);
        }

        [Test]
        public void CompletedRound_WritesRowOnlyWhenFinished()
        {
            var h = NewHarness();
            CompleteNormally(h);
            Assert.That(RoundRows(), Is.Empty);

            Assert.That(h.Round.Finish(), Is.True);

            Assert.That(RoundRows(), Does.EndWith(",completed,"));
            Assert.That(h.Round.IsVoided, Is.False);
        }

        [Test]
        public void CompletedRound_CanBeVoidedWithReason()
        {
            var h = NewHarness();
            CompleteNormally(h);

            h.Round.Finish("面板讀錯, 重做");

            Assert.That(h.Round.IsVoided, Is.True);
            Assert.That(RoundRows(), Does.EndWith(",voided,面板讀錯， 重做"));
        }

        [Test]
        public void EmergencyStoppedRound_IsWrittenAsVoided()
        {
            var h = NewHarness(new FakeArduino { HangOnRun = true });
            h.StartAndWarmUp();
            h.Round.EmergencyStop(h.Now);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 3);

            h.Round.Finish();

            Assert.That(RoundRows(), Does.EndWith(",voided,緊急停止"));
        }

        [Test]
        public void FailedRound_IsWrittenAsVoidedWithFailureReason()
        {
            var h = NewHarness(bootSpeed: 0.1);
            h.Start();
            h.AdvanceUntil(RoundState.WarmingUp);
            h.Arduino.ErrorReply = "BUSY";
            h.AdvanceUntil(RoundState.Failed, maxSeconds: 10);

            h.Round.Finish();

            Assert.That(RoundRows(), Does.EndWith(",voided,韌體回報錯誤：BUSY"));
        }

        [Test]
        public void Finish_IsIdempotentAndRefusedWhileRunning()
        {
            var h = NewHarness();
            h.StartAndWarmUp();
            Assert.That(h.Round.Finish(), Is.False);

            h.CompleteSegment(3.0);
            h.CompleteSegment(1.0);
            h.AdvanceUntil(RoundState.Completed, maxSeconds: 1);
            h.Round.Finish();
            h.Round.Finish("後悔");

            Assert.That(RoundRows().Split('\n'), Has.Length.EqualTo(1));
            Assert.That(h.Round.IsFinished, Is.True);
        }
    }
}
