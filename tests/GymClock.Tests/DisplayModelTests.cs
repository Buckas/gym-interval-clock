using System;
using System.Linq;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>
    /// What the clock shows, checked as data: no window is created. Each test puts the engine in a
    /// known state and asks the display model for a frame.
    /// </summary>
    public class DisplayModelTests
    {
        private const string Circuit = @"PROGRAM ""Cardio Circuit""
MODE CIRCUIT

REPEAT 4
    WORK 45s
    REST 15s
END

BETWEEN
    MOVE 1m ""Rotate to the next station""
END BETWEEN

STATION ""Spin bike""
    COLOUR #E53935
    WORKNOTE ""High cadence""
    RESTNOTE ""Easy spin""
END STATION

STATION ""Box Step Ups""
    WORKNOTE ""Whole foot on the step""
END STATION
";

        private const string Sequential = @"PROGRAM ""Station Session""
MODE SEQUENTIAL

STATION ""Bike""
    WORKNOTE ""High cadence""
    RESTNOTE ""Easy spin""
    WORK 30s
    REST 10s
END STATION

STATION ""Rower""
    WORKNOTE ""Legs, body, arms""
    WORK 30s
END STATION
";

        private const string Parallel = @"PROGRAM ""Own Clocks""
MODE PARALLEL
ROTATIONS 1

STATION ""Short""
    WORKNOTE ""Go hard""
    WORK 10s
END STATION

STATION ""Long""
    WORK 20s
END STATION
";

        private static SessionEngine Engine(string script)
        {
            return new SessionEngine(new TimerSettings { ProgramScript = script, UseBuiltProgram = true, PrepSeconds = 0 }, new SilentCuePlayer());
        }

        private static void At(SessionEngine engine, double seconds)
        {
            engine.PhaseClock.Set(TimeSpan.FromSeconds(seconds), true);
        }

        [Fact]
        public void Circuit_MidWorkInterval_ShowsTheCountdownRoundAndWhatIsNext()
        {
            SessionEngine engine = Engine(Circuit);
            DisplayModel model = new DisplayModel(engine);
            engine.Start();
            engine.Advance();
            engine.Advance();      // round 2's work interval
            At(engine, 11);        // 11 of 45 seconds gone

            DisplayFrame frame = model.BuildFrame(showHints: false);

            Assert.False(frame.IsParallel);
            Assert.Equal("WORK", frame.PhaseLabel);
            Assert.Equal("34", frame.BigNumber);
            Assert.Equal("ROUND 2 OF 4", frame.RoundLine);
            Assert.Equal("Next: REST for 15s", frame.NextUp);
            Assert.Equal("STATION 1 OF 2", frame.Summary);
            Assert.True(frame.ShowProgress);
            Assert.InRange(frame.ProgressFraction, 0.74, 0.76);            // 34 of 45 seconds left
            Assert.Equal(engine.Settings.WorkBg, frame.Background);        // no pulse: more than 3 s to go
            Assert.StartsWith("Session 00:", frame.SessionElapsed);
            Assert.Equal(string.Empty, frame.Hint);
            Assert.Equal(Licensing.ShortCopyright, frame.Copyright);
            Assert.Empty(frame.Tiles);
        }

        [Fact]
        public void Circuit_ListsEveryStation_ButMarksNoneCurrent_BecauseGroupsAreAtAllOfThem()
        {
            SessionEngine engine = Engine(Circuit);
            DisplayModel model = new DisplayModel(engine);
            engine.Start();

            DisplayFrame frame = model.BuildFrame(false);

            Assert.Equal(new[] { "Spin bike", "Box Step Ups" }, frame.Stations.Select(s => s.Name).ToArray());
            Assert.Equal(new[] { 1, 2 }, frame.Stations.Select(s => s.Number).ToArray());
            Assert.Equal("WORK: High cadence", frame.Stations[0].WorkLine);
            Assert.Equal("REST: Easy spin", frame.Stations[0].RestLine);
            Assert.Equal("WORK: Whole foot on the step", frame.Stations[1].WorkLine);
            Assert.Equal(string.Empty, frame.Stations[1].RestLine);
            Assert.Equal(0xE5, frame.Stations[0].Colour.R);                // its own colour, #E53935
            Assert.True(frame.Stations[1].Colour.IsEmpty);                 // none set
            Assert.DoesNotContain(frame.Stations, s => s.IsCurrent);
            Assert.Equal(string.Empty, frame.Tip);                         // no one note fits every group
        }

        [Fact]
        public void Sequential_MarksTheCurrentStation_AndShowsItsWorkThenRestNote()
        {
            SessionEngine engine = Engine(Sequential);
            DisplayModel model = new DisplayModel(engine);
            engine.Start();

            DisplayFrame work = model.BuildFrame(false);
            Assert.Equal("STATION 1 OF 2", work.Summary);
            Assert.Equal("BIKE", work.Secondary);
            Assert.Equal("High cadence", work.Tip);
            Assert.True(work.Stations[0].IsCurrent);
            Assert.False(work.Stations[1].IsCurrent);

            engine.Advance();      // Bike's rest
            DisplayFrame rest = model.BuildFrame(false);
            Assert.Equal("REST", rest.PhaseLabel);
            Assert.Equal("Easy spin", rest.Tip);

            engine.Advance();      // on to the Rower
            DisplayFrame rower = model.BuildFrame(false);
            Assert.Equal("STATION 2 OF 2", rower.Summary);
            Assert.Equal("Legs, body, arms", rower.Tip);
            Assert.True(rower.Stations[1].IsCurrent);
        }

        [Fact]
        public void Parallel_GivesEveryStationItsOwnTile_AndNoStationList()
        {
            SessionEngine engine = Engine(Parallel);
            DisplayModel model = new DisplayModel(engine);
            engine.Start();
            At(engine, 4);

            DisplayFrame frame = model.BuildFrame(false);

            Assert.True(frame.IsParallel);
            Assert.Empty(frame.Stations);
            Assert.Equal(new[] { "Short", "Long" }, frame.Tiles.Select(t => t.Name).ToArray());
            Assert.Equal("WORK", frame.Tiles[0].PhaseLabel);
            Assert.Equal("6", frame.Tiles[0].Countdown);                   // 10 s station, 4 s gone
            Assert.Equal("Go hard", frame.Tiles[0].Note);
            Assert.InRange(frame.Tiles[0].ProgressFraction, 0.58, 0.61);
            Assert.Equal("16", frame.Tiles[1].Countdown);                  // 20 s station
            Assert.Equal(string.Empty, frame.Tiles[1].Note);
            Assert.Equal(engine.Settings.IdleBg, frame.Background);        // a neutral base behind the tiles

            // Once the short station has finished its work it waits for the long one.
            At(engine, 13);
            DisplayFrame later = model.BuildFrame(false);
            Assert.NotEqual("WORK", later.Tiles[0].PhaseLabel);
            Assert.Equal("WORK", later.Tiles[1].PhaseLabel);
            Assert.Equal("7", later.Tiles[1].Countdown);
        }

        [Fact]
        public void Paused_SaysSo()
        {
            SessionEngine engine = Engine(Circuit);
            DisplayModel model = new DisplayModel(engine);
            engine.Start();
            At(engine, 5);
            engine.TogglePause();

            DisplayFrame frame = model.BuildFrame(true);

            Assert.Equal("PAUSED", frame.PhaseLabel);
            Assert.Equal(engine.Settings.PausedBg, frame.Background);
            Assert.Equal("Paused during WORK - press SPACE to resume", frame.NextUp);
            Assert.Equal("40", frame.BigNumber);
        }

        [Fact]
        public void LastThreeSeconds_PulseTheBackground()
        {
            SessionEngine engine = Engine(Circuit);
            DisplayModel model = new DisplayModel(engine);
            engine.Start();
            At(engine, 43.2);      // under 3 s of the 45 left

            DisplayFrame frame = model.BuildFrame(false);

            Assert.Equal("2", frame.BigNumber);
            Assert.NotEqual(engine.Settings.WorkBg, frame.Background);
        }

        [Fact]
        public void Idle_OnTheMainPc_SaysHowToStart_AndListsTheKeys()
        {
            SessionEngine engine = Engine(Circuit);
            DisplayModel model = new DisplayModel(engine);

            DisplayFrame frame = model.BuildFrame(showHints: true);

            Assert.Equal("READY", frame.PhaseLabel);
            Assert.Contains("SPACE", frame.NextUp);
            Assert.Contains("SPACE start/pause", frame.Hint);
            Assert.Contains("1-9 jump to station", frame.Hint);
            Assert.False(frame.ShowProgress);
            Assert.Equal(string.Empty, frame.SessionElapsed);
        }

        [Fact]
        public void Idle_OnASecondScreen_NeverMentionsKeysItDoesNotHave()
        {
            SessionEngine engine = Engine(Circuit);
            DisplayModel model = new DisplayModel(engine) { IsSecondScreen = true, HasLeaderProgram = false };

            DisplayFrame waiting = model.BuildFrame(true);
            Assert.Equal("Waiting for the main PC...", waiting.NextUp);
            Assert.DoesNotContain("SPACE", waiting.NextUp);
            Assert.DoesNotContain("SPACE", waiting.Hint);
            Assert.Contains("the main PC has the controls", waiting.Hint);

            model.HasLeaderProgram = true;
            Assert.Equal("Ready - the session starts from the main PC", model.BuildFrame(true).NextUp);
        }

        [Fact]
        public void TimeOfDay_IsEmpty_WhenTheClockIsSwitchedOff()
        {
            SessionEngine engine = Engine(Circuit);
            DisplayModel model = new DisplayModel(engine);
            Assert.NotEqual(string.Empty, model.BuildFrame(false).TimeOfDay);

            engine.Settings.ShowClock = false;
            Assert.Equal(string.Empty, model.BuildFrame(false).TimeOfDay);

            engine.Settings.ShowStationsTable = false;
            Assert.Empty(model.BuildFrame(false).Stations);
        }
    }
}
