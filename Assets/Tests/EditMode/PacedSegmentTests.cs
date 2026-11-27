using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    /// <summary>A 送法：電腦逐筆送出、收到完成回覆後等 gap 再送下一筆。</summary>
    public class PacedSegmentTests
    {
        private static StressTestSession Connect(FakeArduino arduino)
        {
            var session = new StressTestSession(arduino, EntryType.Opto);
            session.BeginHandshake(0.0);
            session.Poll(0.1);
            return session;
        }

        /// <summary>從 start 起每 10 ms Poll 一次，直到這一段結束；回傳結束時間。</summary>
        private static double PollToEnd(StressTestSession session, double start, double maxSeconds = 60)
        {
            var now = start;
            while (session.State == SessionState.RunningSegment && now < start + maxSeconds)
            {
                now += 0.01;
                session.Poll(now);
            }
            return now;
        }

        [Test]
        public void SendsOneCommandAtATime_WaitingForEachCompletionReply()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);

            Assert.That(session.RunPacedSegment(SegmentDirection.Up, 3, 0, 2.0, 1.0), Is.True);
            Assert.That(arduino.Received.Last(), Is.EqualTo("ZERO"));

            session.Poll(1.01);
            Assert.That(arduino.Received.Last(), Is.EqualTo("CMD,1,UP*7E"));
            session.Poll(1.02);
            Assert.That(arduino.Received.Last(), Is.EqualTo("CMD,2,UP*7D"));
            session.Poll(1.03);
            Assert.That(arduino.Received.Last(), Is.EqualTo("CMD,3,UP*7C"));
            session.Poll(1.04);
            Assert.That(arduino.Received.Last(), Is.EqualTo("STAT"));
            Assert.That(session.State, Is.EqualTo(SessionState.RunningSegment));
            session.Poll(1.05);

            Assert.That(session.State, Is.EqualTo(SessionState.AwaitingReading));
            var s = session.CurrentSegment;
            Assert.That((s.PcSent, s.Replies), Is.EqualTo((3, 3)));
            Assert.That((s.ArduinoReceived, s.ArduinoExecuted, s.ArduinoBad), Is.EqualTo(((int?)3, (int?)3, (int?)0)));
            Assert.That(s.ReportedCount, Is.EqualTo(3));
            Assert.That(s.ReportShort, Is.False);
            Assert.That(s.Commands.Select(c => c.Status), Is.All.EqualTo(CommandStatus.Done));
        }

        [Test]
        public void SequenceNumbers_KeepIncreasingAcrossSegments()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 3, 0, 2.0, 1.0);
            var end = PollToEnd(session, 1.0);
            session.SubmitReading(2.3);

            session.RunPacedSegment(SegmentDirection.Down, 1, 0, 2.3, end);
            PollToEnd(session, end);

            Assert.That(arduino.Received, Has.Member("CMD,4,DOWN*6C"));
        }

        [Test]
        public void NextCommand_WaitsGapAfterTheCompletionReply()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 2, 100, 2.0, 1.0);
            session.Poll(1.01); // 送第 1 筆
            session.Poll(1.02); // 收到回覆，開始等 100 ms

            session.Poll(1.11);
            Assert.That(arduino.Received.Count(l => l.StartsWith("CMD,")), Is.EqualTo(1));
            session.Poll(1.12);

            Assert.That(arduino.Received.Count(l => l.StartsWith("CMD,")), Is.EqualTo(2));
        }

        [Test]
        public void RecordsPcAndArduinoTimes_AndActualRestBetweenCommands()
        {
            var arduino = new FakeArduino { GapJitterMicros = 8 };
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 3, 5, 2.0, 1.0);
            PollToEnd(session, 1.0);

            var first = session.CurrentSegment.Commands[0];
            Assert.That(first.PcSendSeconds, Is.EqualTo(1.01).Within(1e-9));
            Assert.That(first.PcReplySeconds, Is.EqualTo(1.02).Within(1e-9));
            Assert.That(first.RxMicros, Is.EqualTo(999_990));
            Assert.That(session.CurrentSegment.MeanGapMicros, Is.EqualTo(5008).Within(1e-9));
        }

        [Test]
        public void CommandLostOnTheLink_TimesOutAndIsCountedAsNotReceived()
        {
            var arduino = new FakeArduino();
            arduino.LoseCommands.Add(2);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 3, 0, 2.0, 1.0);

            PollToEnd(session, 1.0);

            var s = session.CurrentSegment;
            Assert.That(s.Commands.Select(c => c.Status),
                Is.EqualTo(new[] { CommandStatus.Done, CommandStatus.NoReply, CommandStatus.Done }));
            Assert.That((s.PcSent, s.ArduinoReceived, s.ArduinoExecuted), Is.EqualTo((3, (int?)2, (int?)2)));
            Assert.That(s.TimedOut, Is.False);
            Assert.That(s.ReportShort, Is.True);
            // 缺回覆的那一筆前後不算休息時間，否則會把逾時等待算進去
            Assert.That(s.Gaps, Is.Empty);
        }

        [Test]
        public void ReplyTimeout_IsTwiceActionTimePlusOneSecond()
        {
            var arduino = new FakeArduino(); // action_ms=120 → 1.24 秒
            arduino.LoseCommands.Add(1);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 2, 0, 2.0, 1.0);
            session.Poll(1.01);

            session.Poll(2.24);
            Assert.That(session.CurrentSegment.PcSent, Is.EqualTo(1));
            session.Poll(2.26);

            Assert.That(session.CurrentSegment.PcSent, Is.EqualTo(2));
        }

        [Test]
        public void LostReply_IsDistinguishedByArduinoCounters()
        {
            var arduino = new FakeArduino();
            arduino.LoseReplies.Add(2);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 3, 0, 2.0, 1.0);

            PollToEnd(session, 1.0);

            var s = session.CurrentSegment;
            Assert.That(s.Commands[1].Status, Is.EqualTo(CommandStatus.NoReply));
            Assert.That((s.ArduinoReceived, s.ArduinoExecuted, s.Replies), Is.EqualTo(((int?)3, (int?)3, 2)));
            Assert.That(s.ReportedCount, Is.EqualTo(3));
        }

        [Test]
        public void CorruptedCommand_IsRejectedAndTheRunContinues()
        {
            var arduino = new FakeArduino();
            arduino.CorruptCommands.Add(1);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 2, 0, 2.0, 1.0);

            PollToEnd(session, 1.0, maxSeconds: 0.5); // 不必等逾時

            var s = session.CurrentSegment;
            Assert.That(session.State, Is.EqualTo(SessionState.AwaitingReading));
            Assert.That(s.Commands.Select(c => c.Status),
                Is.EqualTo(new[] { CommandStatus.Rejected, CommandStatus.Done }));
            Assert.That((s.ArduinoReceived, s.ArduinoBad), Is.EqualTo(((int?)1, (int?)1)));
        }

        [Test]
        public void ThreeTimeoutsInARow_EndTheSegment()
        {
            var arduino = new FakeArduino();
            foreach (var i in new[] { 2, 3, 4 }) arduino.LoseCommands.Add(i);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 10, 0, 2.0, 1.0);

            PollToEnd(session, 1.0);

            var s = session.CurrentSegment;
            Assert.That(s.PcSent, Is.EqualTo(4));
            Assert.That(s.TimedOut, Is.True);
            Assert.That(s.ArduinoReceived, Is.EqualTo(1));
            Assert.That(session.State, Is.EqualTo(SessionState.AwaitingReading));
            Assert.That(session.WaitingForLateDone, Is.False);
        }

        [Test]
        public void LateReply_IsKeptAsLate()
        {
            var arduino = new FakeArduino();
            arduino.DelayReplies.Add(1);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 2, 500, 2.0, 1.0);
            session.Poll(1.01);
            session.Poll(2.30); // 第 1 筆逾時
            arduino.ReleaseDelayedReplies();
            session.Poll(2.31);

            PollToEnd(session, 2.31);

            var s = session.CurrentSegment;
            Assert.That(s.Commands.Select(c => c.Status),
                Is.EqualTo(new[] { CommandStatus.Late, CommandStatus.Done }));
            Assert.That(s.Replies, Is.EqualTo(2));
            Assert.That(s.Timestamps.Select(t => t.Index), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void LostStatReply_IsAskedAgain()
        {
            var arduino = new FakeArduino { LoseStatReplies = 2 };
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 1, 0, 2.0, 1.0);

            PollToEnd(session, 1.0);

            Assert.That(arduino.Received.Count(l => l == "STAT"), Is.EqualTo(3));
            Assert.That(session.CurrentSegment.ArduinoReceived, Is.EqualTo(1));
        }

        [Test]
        public void StatNeverAnswered_LeavesArduinoCountsUnknown()
        {
            var arduino = new FakeArduino { LoseStatReplies = 99 };
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 2, 0, 2.0, 1.0);

            PollToEnd(session, 1.0);

            var s = session.CurrentSegment;
            Assert.That(session.State, Is.EqualTo(SessionState.AwaitingReading));
            Assert.That((s.ArduinoReceived, s.ArduinoExecuted), Is.EqualTo(((int?)null, (int?)null)));
            Assert.That(s.ReportedCount, Is.EqualTo(2)); // 退回用收到的完成回覆數
        }

        [Test]
        public void ZeroNeverAcknowledged_StillRunsButDoesNotTrustCounters()
        {
            var arduino = new FakeArduino { LoseZeroReplies = 99 };
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 2, 0, 2.0, 1.0);

            PollToEnd(session, 1.0);

            var s = session.CurrentSegment;
            Assert.That(s.Replies, Is.EqualTo(2));
            Assert.That(s.ArduinoReceived, Is.Null);
        }

        [Test]
        public void Abort_StopsSendingAtOnce()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 10, 0, 2.0, 1.0);
            session.Poll(1.01);
            session.Poll(1.02);

            Assert.That(session.Abort(), Is.True);
            var sentBefore = arduino.Received.Count;
            session.Poll(1.03);
            session.Poll(5.0);

            Assert.That(arduino.Received.Count, Is.EqualTo(sentBefore));
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(session.CurrentSegment.Aborted, Is.True);
        }

        [Test]
        public void ArduinoRebootDuringSegment_EndsItAndIsFlagged()
        {
            var arduino = new FakeArduino();
            arduino.LoseCommands.Add(2);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 5, 0, 2.0, 1.0);
            session.Poll(1.01);
            session.Poll(1.02);
            arduino.Boot();

            session.Poll(1.03);

            Assert.That(session.State, Is.EqualTo(SessionState.AwaitingReading));
            Assert.That(session.CurrentSegment.Rebooted, Is.True);
            Assert.That(session.CurrentSegment.ArduinoReceived, Is.Null);
        }

        [Test]
        public void ArduinoRebootDuringRun_AlsoEndsTheSegment()
        {
            var arduino = new FakeArduino { HangOnRun = true };
            var session = Connect(arduino);
            session.RunSegment(SegmentDirection.Up, 20, 0, 2.0, 1.0);
            session.Poll(1.05);
            arduino.Boot();

            session.Poll(1.1);

            Assert.That(session.State, Is.EqualTo(SessionState.AwaitingReading));
            Assert.That(session.CurrentSegment.Rebooted, Is.True);
        }

        [Test]
        public void SecondErrorForTheSameBrokenLine_DoesNotRejectTheNextCommand()
        {
            // 一行指令被雜訊切成兩段：韌體先回 NAK，稍後再回 ERR,UNKNOWN
            var arduino = new FakeArduino();
            arduino.CorruptCommands.Add(1);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 2, 0, 2.0, 1.0);
            session.Poll(1.01);
            session.Poll(1.02);
            arduino.Emit("ERR,UNKNOWN");
            session.Poll(1.03);

            PollToEnd(session, 1.03);

            Assert.That(session.CurrentSegment.Commands.Select(c => c.Status),
                Is.EqualTo(new[] { CommandStatus.Rejected, CommandStatus.Done }));
        }

        [Test]
        public void LateReplies_DoNotResetTheConsecutiveTimeoutCount()
        {
            var arduino = new FakeArduino();
            foreach (var i in new[] { 1, 2, 3 }) arduino.DelayReplies.Add(i);
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 10, 0, 2.0, 1.0);

            var now = 1.0;
            var sent = 0;
            while (session.State == SessionState.RunningSegment && now < 30)
            {
                now += 0.01;
                session.Poll(now);
                if (session.CurrentSegment.PcSent > sent)
                {
                    // 下一筆一送出，上一筆的回覆才遲到
                    sent = session.CurrentSegment.PcSent;
                    if (sent > 1) arduino.ReleaseOldestDelayedReply();
                }
            }

            Assert.That(session.CurrentSegment.TimedOut, Is.True);
            Assert.That(session.CurrentSegment.PcSent, Is.EqualTo(3));
        }

        [Test]
        public void CountersLowerThanReplies_AreNotTrusted()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 5, 0, 2.0, 1.0);
            session.Poll(1.01);
            session.Poll(1.02);
            session.Poll(1.03);
            arduino.ResetCounters();

            PollToEnd(session, 1.03);

            var s = session.CurrentSegment;
            Assert.That(s.Replies, Is.EqualTo(5));
            Assert.That((s.ArduinoReceived, s.ArduinoExecuted), Is.EqualTo(((int?)null, (int?)null)));
        }

        [Test]
        public void RebootWhileWaitingForTheReading_KeepsTheSegmentAndFlagsIt()
        {
            var arduino = new FakeArduino();
            var session = Connect(arduino);
            session.RunPacedSegment(SegmentDirection.Up, 2, 0, 2.0, 1.0);
            var end = PollToEnd(session, 1.0);
            arduino.Boot();
            session.Poll(end + 0.01);

            var result = session.SubmitReading(2.2);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Segment.Rebooted, Is.True);
        }

        [Test]
        public void FirmwareCheck_LimitsCountBySendMode()
        {
            var session = Connect(new FakeArduino(parameters: "action_ms=120;max_run=90;ack=1"));
            var firmware = session.Firmware;

            Assert.That(firmware.CheckSegment(SendMode.Ack, 300), Is.Null);
            Assert.That(firmware.CheckSegment(SendMode.Ack, 301), Does.Contain("300"));
            Assert.That(firmware.CheckSegment(SendMode.Burst, 91), Does.Contain("90"));
            Assert.That(firmware.CheckSegment(SendMode.Burst, 0), Is.Not.Null);
        }

        [Test]
        public void LineChecksum_AcceptsOnlyUndamagedLines()
        {
            Assert.That(LineChecksum.Wrap("CMD,1,UP"), Is.EqualTo("CMD,1,UP*7E"));
            Assert.That(LineChecksum.TryUnwrap("X,1,1000,1010,121010*6B", out var payload), Is.True);
            Assert.That(payload, Is.EqualTo("X,1,1000,1010,121010"));
            Assert.That(LineChecksum.TryUnwrap("X,1,1000,1010,121011*6B", out _), Is.False);
            Assert.That(LineChecksum.TryUnwrap("X,1,1000", out _), Is.False);
        }
    }
}
