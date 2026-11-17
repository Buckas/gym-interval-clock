using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>
    /// The timing engine by itself: no window, no real time. Time is moved with RunClock.Set,
    /// and sounds are recorded instead of played.
    /// </summary>
    public class SessionEngineTests
    {
        private sealed class RecordingCuePlayer : ICuePlayer
        {
            public readonly List<Cue> Played = new List<Cue>();
            public void Play(Cue cue) { Played.Add(cue); }
        }

        // 2 stations; each: 2 x (work 20 s, rest 10 s); 15 s to move between them.
        private const string Circuit = @"PROGRAM ""Engine Test""
MODE CIRCUIT

REPEAT 2
    WORK 20s
    REST 10s
END

BETWEEN
    MOVE 15s ""Rotate""
END BETWEEN

STATION ""Bike""
END STATION

STATION ""Rower""
END STATION
";

        // Two stations with their own clocks: 10 s and 20 s of work.
        private const string Parallel = @"PROGRAM ""Parallel Test""
MODE PARALLEL
ROTATIONS 1

STATION ""Short""
    WORK 10s
END STATION

STATION ""Long""
    WORK 20s
END STATION
";

        private static SessionEngine Make(string script, int prepSeconds, out RecordingCuePlayer cues)
        {
            cues = new RecordingCuePlayer();
            TimerSettings settings = new TimerSettings { ProgramScript = script, UseBuiltProgram = true, PrepSeconds = prepSeconds };
            return new SessionEngine(settings, cues);
        }

        /// <summary>Puts the current phase's clock at a number of seconds, still running.</summary>
        private static void At(SessionEngine engine, double seconds)
        {
            engine.PhaseClock.Set(TimeSpan.FromSeconds(seconds), true);
        }

        [Fact]
        public void StartsIdle_WithTheProgramBuilt()
        {
            SessionEngine engine = Make(Circuit, 5, out _);

            Assert.Equal(Phase.Idle, engine.Phase);
            Assert.Equal(ExecutionMode.Circuit, engine.Program.Mode);
            Assert.False(engine.InTimedPhase);
            Assert.False(engine.IsPaused);
            Assert.Equal(
                new[] { BlockType.Work, BlockType.Rest, BlockType.Work, BlockType.Rest, BlockType.Move, BlockType.Work, BlockType.Rest, BlockType.Work, BlockType.Rest },
                engine.Sequence.Select(b => b.Block.Type).ToArray());
        }

        [Fact]
        public void Circuit_RunsPrep_ThenEveryBlockInOrder_ThenFinishes()
        {
            SessionEngine engine = Make(Circuit, 5, out RecordingCuePlayer cues);
            int changes = 0;
            engine.Changed += delegate { changes++; };

            engine.Start();
            Assert.Equal(Phase.Prep, engine.Phase);
            Assert.Equal(5, engine.PhaseLength);
            Assert.True(engine.PhaseClock.IsRunning);
            Assert.True(engine.SessionClock.IsRunning);

            // Each block ends by itself when its time is up.
            At(engine, 5.1);
            engine.Tick();
            Assert.Equal(Phase.Active, engine.Phase);
            Assert.Equal(0, engine.Index);
            Assert.Equal(20, engine.PhaseLength);

            List<BlockType> seen = new List<BlockType> { engine.CurrentBlock().Block.Type };
            for (int i = 0; i < 8; i++)
            {
                At(engine, engine.PhaseLength + 0.1);
                engine.Tick();
                seen.Add(engine.CurrentBlock().Block.Type);
            }
            Assert.Equal(8, engine.Index);
            Assert.Equal(engine.Sequence.Select(b => b.Block.Type).ToArray(), seen.ToArray());

            At(engine, engine.PhaseLength + 0.1);
            engine.Tick();
            Assert.Equal(Phase.Finished, engine.Phase);
            Assert.False(engine.PhaseClock.IsRunning);
            Assert.False(engine.SessionClock.IsRunning);
            Assert.Equal(Cue.Finish, cues.Played.Last());
            Assert.True(changes > 10);
        }

        [Fact]
        public void EachBlockStart_PlaysItsCue()
        {
            SessionEngine engine = Make(Circuit, 5, out RecordingCuePlayer cues);

            engine.Start();
            Assert.Equal(new[] { Cue.PrepStart }, cues.Played.ToArray());
            engine.Advance();   // work
            engine.Advance();   // rest
            engine.Advance();   // work
            engine.Advance();   // rest
            engine.Advance();   // move
            Assert.Equal(new[] { Cue.PrepStart, Cue.WorkStart, Cue.RestStart, Cue.WorkStart, Cue.RestStart, Cue.MoveStart }, cues.Played.ToArray());
        }

        [Fact]
        public void LastThreeSeconds_BeepOncePerSecond()
        {
            SessionEngine engine = Make(Circuit, 0, out RecordingCuePlayer cues);
            engine.Start();            // no prep: straight into the first work block (20 s)
            cues.Played.Clear();

            At(engine, 16.5);          // 3.5 s left: nothing yet
            engine.Tick();
            Assert.Empty(cues.Played);

            At(engine, 17.2);          // 2.8 s left = "3"
            engine.Tick();
            engine.Tick();             // same second: no second beep
            Assert.Equal(new[] { Cue.Countdown }, cues.Played.ToArray());

            At(engine, 18.2);
            engine.Tick();
            At(engine, 19.2);
            engine.Tick();
            Assert.Equal(new[] { Cue.Countdown, Cue.Countdown, Cue.Countdown }, cues.Played.ToArray());
        }

        [Fact]
        public void Pause_StopsBothClocks_AndResumeCarriesOn()
        {
            SessionEngine engine = Make(Circuit, 0, out RecordingCuePlayer cues);
            engine.Start();
            At(engine, 7);

            engine.TogglePause();
            Assert.True(engine.IsPaused);
            Assert.False(engine.PhaseClock.IsRunning);
            Assert.False(engine.SessionClock.IsRunning);
            Assert.Equal(Cue.Pause, cues.Played.Last());
            Assert.Equal(7, engine.PhaseClock.Elapsed.TotalSeconds, 1);

            engine.Tick();             // a paused session does not advance or beep
            Assert.Equal(0, engine.Index);

            engine.TogglePause();
            Assert.False(engine.IsPaused);
            Assert.True(engine.PhaseClock.IsRunning);
            Assert.Equal(0, engine.Index);
        }

        [Fact]
        public void TogglePause_WhenIdleOrFinished_StartsTheSession()
        {
            SessionEngine engine = Make(Circuit, 5, out _);
            engine.TogglePause();
            Assert.Equal(Phase.Prep, engine.Phase);
        }

        [Fact]
        public void Back_RestartsTheInterval_OrStepsBackWhenItHasJustBegun()
        {
            SessionEngine engine = Make(Circuit, 0, out _);
            engine.Start();
            engine.Advance();
            engine.Advance();          // third block (index 2)
            Assert.Equal(2, engine.Index);

            At(engine, 3);             // more than 2.5 s in: restart this interval
            engine.GoBack();
            Assert.Equal(2, engine.Index);
            Assert.True(engine.PhaseClock.Elapsed.TotalSeconds < 1);

            At(engine, 1);             // just begun: go to the one before
            engine.GoBack();
            Assert.Equal(1, engine.Index);
        }

        [Fact]
        public void Reset_GoesBackToIdle()
        {
            SessionEngine engine = Make(Circuit, 5, out _);
            engine.Start();
            engine.Advance();
            engine.Advance();

            engine.Reset();
            Assert.Equal(Phase.Idle, engine.Phase);
            Assert.Equal(-1, engine.Index);
            Assert.False(engine.PhaseClock.IsRunning);
            Assert.Equal(0, engine.SessionClock.Elapsed.TotalSeconds, 3);
        }

        [Fact]
        public void JumpToStation_LandsOnThatStationsFirstBlock_StartingTheSessionIfNeeded()
        {
            SessionEngine engine = Make(Circuit, 5, out _);
            double hinted = 0;
            engine.HintRequested += seconds => hinted = seconds;

            engine.JumpToStation(1);   // station 2, from idle

            Assert.Equal(Phase.Active, engine.Phase);
            Assert.Equal(5, engine.Index);                         // W R W R M | W ...
            Assert.Equal(1, engine.CurrentBlock().StationIndex);
            Assert.Equal(BlockType.Work, engine.CurrentBlock().Block.Type);
            Assert.Equal(4, hinted);

            engine.JumpToStation(7);   // no such station: nothing moves
            Assert.Equal(5, engine.Index);
        }

        [Fact]
        public void Parallel_HasNoSingleBlockToSkip_AndFinishesWhenEveryStationIsDone()
        {
            SessionEngine engine = Make(Parallel, 0, out RecordingCuePlayer cues);
            bool hinted = false;
            engine.HintRequested += delegate { hinted = true; };

            engine.Start();
            Assert.True(engine.IsParallelActive);
            Assert.Equal(2, engine.ParallelStations.Count);

            engine.Advance();          // "skip" means nothing here: show the keys instead
            Assert.True(hinted);
            Assert.Equal(Phase.Active, engine.Phase);

            At(engine, 12);            // the short station is done, the long one is not
            engine.Tick();
            List<StationPosition> positions = engine.ComputeParallelPositions();
            Assert.Equal(2, positions.Count);
            Assert.Equal(Phase.Active, engine.Phase);

            At(engine, 25);            // both done
            engine.Tick();
            Assert.Equal(Phase.Finished, engine.Phase);
            Assert.Equal(Cue.Finish, cues.Played.Last());
        }

        [Fact]
        public void SnapTo_PutsTheSessionWhereAMainPcSaysItIs()
        {
            SessionEngine engine = Make(Circuit, 5, out RecordingCuePlayer cues);

            // Joining a session that is 7.5 s into its third block.
            engine.SnapTo(Phase.Active, 2, 7.5, 40, paused: false);
            Assert.Equal(Phase.Active, engine.Phase);
            Assert.Equal(2, engine.Index);
            Assert.Equal(20, engine.PhaseLength);
            Assert.True(engine.PhaseClock.IsRunning);
            Assert.InRange(engine.PhaseClock.Elapsed.TotalSeconds, 7.5, 8.0);
            Assert.InRange(engine.SessionClock.Elapsed.TotalSeconds, 40, 40.5);
            Assert.Equal(new[] { Cue.WorkStart }, cues.Played.ToArray());   // the cue for the block it landed on

            // The main PC pauses.
            engine.SnapTo(Phase.Active, 2, 9, 41.5, paused: true);
            Assert.True(engine.IsPaused);
            Assert.Equal(9, engine.PhaseClock.Elapsed.TotalSeconds, 3);
            Assert.Single(cues.Played);                                     // same step: no new cue

            // The main PC resets.
            engine.SnapTo(Phase.Idle, -1, 0, 0, paused: false);
            Assert.Equal(Phase.Idle, engine.Phase);

            // The main PC finishes.
            engine.SnapTo(Phase.Active, 8, 1, 100, paused: false);
            engine.SnapTo(Phase.Finished, 8, 0, 110, paused: false);
            Assert.Equal(Phase.Finished, engine.Phase);
            Assert.Equal(Cue.Finish, cues.Played.Last());
            Assert.Equal(110, engine.SessionClock.Elapsed.TotalSeconds, 3);
        }

        [Fact]
        public void ApplySettings_MidSession_KeepsTheSessionInsideTheNewProgram()
        {
            SessionEngine engine = Make(Circuit, 0, out _);
            engine.Start();
            for (int i = 0; i < 7; i++) engine.Advance();
            Assert.Equal(7, engine.Index);

            // A shorter program: one station, so only four blocks.
            string oneStation = Circuit.Replace("STATION \"Rower\"\nEND STATION\n", string.Empty).Replace("STATION \"Rower\"\r\nEND STATION\r\n", string.Empty);
            engine.ApplySettings(new TimerSettings { ProgramScript = oneStation, UseBuiltProgram = true, PrepSeconds = 0 });

            Assert.Equal(4, engine.Sequence.Count);
            Assert.InRange(engine.Index, 0, 3);
            Assert.Equal(Phase.Active, engine.Phase);
        }

        [Fact]
        public void SimpleSession_WithNoProgram_StillRuns()
        {
            RecordingCuePlayer cues = new RecordingCuePlayer();
            SessionEngine engine = new SessionEngine(
                new TimerSettings { UseBuiltProgram = false, WorkSeconds = 30, RestSeconds = 10, Rounds = 2, PrepSeconds = 0 }, cues);

            engine.Start();
            Assert.Equal(Phase.Active, engine.Phase);
            Assert.Equal(BlockType.Work, engine.CurrentBlock().Block.Type);
            Assert.Equal(30, engine.PhaseLength);
            Assert.Equal(30, engine.RemainingSeconds(), 0);
        }
    }
}
