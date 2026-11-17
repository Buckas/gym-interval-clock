using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GymClock.Wpf;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>
    /// Snapshot tests for the version 5 clock (build guide P3.8). Each test puts a real session in a
    /// known state, draws its frame on a 1280 x 720 ClockSurface and compares the picture with the
    /// approved one in tests/GymClock.Tests/Snapshots. It passes when fewer than 0.5% of pixels
    /// differ by more than 24 in any colour channel.
    ///
    /// No approved picture yet: the test saves NAME.new.png beside where it would be and fails.
    /// Look at it; if it is right, rename it to NAME.png.
    ///
    /// Skipped on GitHub (the CI variable is set there): its Windows image has different fonts.
    /// </summary>
    public class DisplaySnapshotTests
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
    WORKNOTE ""High cadence""
    RESTNOTE ""Easy spin""
END STATION

STATION ""Box step ups""
    WORKNOTE ""Whole foot on the step""
    RESTNOTE ""Shake out, reset""
END STATION

STATION ""Treadmill""
    WORKNOTE ""Run at about 80%""
    RESTNOTE ""Walk to recover""
END STATION

STATION ""Skipping""
    WORKNOTE ""Small jumps, turn from the wrists""
    RESTNOTE ""Rope down, deep breaths""
END STATION
";

        private const string Sequential = @"PROGRAM ""Station Session""
MODE SEQUENTIAL

STATION ""Spin bike""
    WORKNOTE ""High cadence""
    RESTNOTE ""Easy spin""
    WORK 45s
    REST 15s
END STATION

STATION ""Rower""
    COLOUR #B23A78
    WORKNOTE ""Legs, body, arms""
    RESTNOTE ""Arms, body, legs""
    WORK 45s
    REST 15s
END STATION

STATION ""Wall balls""
    WORKNOTE ""Full squat""
    WORK 45s
END STATION
";

        private const string Shared = @"PROGRAM ""Whole Group HIIT""
MODE SHARED

REPEAT 8
    WORK 40s
    REST 20s
END

STATION ""Burpees""
END STATION

STATION ""Squats""
END STATION

STATION ""Push-ups""
END STATION
";

        private static string ParallelScript(int stations)
        {
            string[] names = { "Bike", "Treadmill", "Skipping", "Floor", "Rower", "Wall balls", "Battle ropes", "Plank" };
            string[] notes = { "High intensity", "Run at 80%", "Rope down", "Rotating soon", "Long strokes", "Full squat", "Waves", "Hold it" };
            StringBuilder sb = new StringBuilder();
            sb.Append("PROGRAM \"Everest Session 1 - The Journey Begins\"\nMODE PARALLEL\nROTATIONS 4\n\n");
            for (int i = 0; i < stations; i++)
            {
                sb.Append("STATION \"").Append(names[i]).Append("\"\n");
                if (i == 1) sb.Append("    COLOUR #E53935\n");
                sb.Append("    WORKNOTE \"").Append(notes[i]).Append("\"\n");
                sb.Append("    RESTNOTE \"Breathe\"\n");
                if (i % 4 == 2) sb.Append("    REST 20s\n    WORK 40s\n");
                else if (i % 4 == 3) sb.Append("    RECOVERY 2m\n");
                else sb.Append("    WORK ").Append(40 + (i * 5)).Append("s\n    REST 20s\n");
                sb.Append("END STATION\n\n");
            }
            return sb.ToString();
        }

        // ================================================================ the snapshots

        [Fact] public void Circuit_Idle() { Shot("circuit-idle", Circuit, e => { }, hints: true); }
        [Fact] public void Circuit_GetReady() { Shot("circuit-prep", Circuit, e => e.SnapTo(Phase.Prep, 0, 3.5, 3.5, false), prep: 10); }
        [Fact] public void Circuit_Work() { Shot("circuit-work", Circuit, e => e.SnapTo(Phase.Active, 2, 11.5, 252.5, false)); }
        [Fact] public void Circuit_Rest() { Shot("circuit-rest", Circuit, e => e.SnapTo(Phase.Active, 3, 4.5, 300.5, false)); }
        [Fact] public void Circuit_Move() { Shot("circuit-move", Circuit, e => e.SnapTo(Phase.Active, 8, 20.5, 500.5, false)); }
        [Fact] public void Circuit_Paused() { Shot("circuit-paused", Circuit, e => e.SnapTo(Phase.Active, 2, 11.5, 252.5, true), hints: true); }
        [Fact] public void Circuit_Finished() { Shot("circuit-finished", Circuit, e => e.SnapTo(Phase.Finished, 0, 0, 1140, false), hints: true); }

        [Fact] public void Sequential_Work_HighlightsTheStationAndShowsItsTip() { Shot("sequential-work", Sequential, e => e.SnapTo(Phase.Active, 0, 11.5, 11.5, false)); }
        [Fact] public void Sequential_Rest_OnAStationWithItsOwnColour() { Shot("sequential-rest", Sequential, e => e.SnapTo(Phase.Active, 3, 4.5, 110.5, false)); }

        [Fact] public void Shared_Work() { Shot("shared-work", Shared, e => e.SnapTo(Phase.Active, 0, 9.5, 9.5, false)); }

        [Fact]
        public void SimpleSession_RotatingStations()
        {
            TimerSettings s = Settings(null, 0);
            s.Stations = TimerSettings.ParseStations("Burpees|Chest to floor|Walk it off;Squats|Below parallel|Shake out;Push-ups");
            Shot("simple-work", s, e => e.SnapTo(Phase.Active, 2, 20.5, 80.5, false));
        }

        [Fact] public void Parallel_2Tiles() { Shot("parallel-2", ParallelScript(2), e => e.SnapTo(Phase.Active, 0, 6.5, 6.5, false)); }
        [Fact] public void Parallel_4Tiles() { Shot("parallel-4", ParallelScript(4), e => e.SnapTo(Phase.Active, 0, 6.5, 6.5, false)); }
        [Fact] public void Parallel_8Tiles() { Shot("parallel-8", ParallelScript(8), e => e.SnapTo(Phase.Active, 0, 6.5, 6.5, false)); }

        [Fact] public void Layout_BigNumber() { Shot("layout-bignumber", Circuit, e => e.SnapTo(Phase.Active, 2, 11.5, 252.5, false), layout: ClockSurface.BigNumberLayout); }
        [Fact] public void Layout_Stations() { Shot("layout-stations", Sequential, e => e.SnapTo(Phase.Active, 0, 11.5, 11.5, false), layout: ClockSurface.StationsLayout); }
        [Fact] public void SafeMargin_5Percent() { Shot("margin-5", Circuit, e => e.SnapTo(Phase.Active, 2, 11.5, 252.5, false), margin: 5); }

        [Fact]
        public void SecondScreen_Idle_WaitingForTheMainPc()
        {
            Shot("second-screen-idle", Circuit, e => { }, hints: true, secondScreen: true,
                tweak: f => { f.LinkBadge = "SECOND SCREEN · LOOKING FOR THE MAIN PC"; f.LinkBadgeIsWarning = true; });
        }

        [Fact]
        public void Toast_OverTheClock()
        {
            Shot("toast", Circuit, e => { }, tweak: f => f.Toast = "PROGRAM READY: CARDIO CIRCUIT");
        }

        [Fact]
        public void LicenceWarning_UpdateNotice_AndLinkBadge()
        {
            Shot("notices", Circuit, e => e.SnapTo(Phase.Active, 0, 11.5, 11.5, false), tweak: f =>
            {
                f.Licensee = string.Empty;
                f.LicenceWarning = "Free trial: 5 days left - press F3";
                f.LicenceWarningIsUrgent = true;
                f.UpdateNotice = "Version 5.1 is available - press F3";
                f.LinkBadge = "LINKED · 1 SECOND SCREEN";
            });
        }

        [Fact]
        public void CrowdedStationList_LongNamesAndNotes()
        {
            StringBuilder sb = new StringBuilder("PROGRAM \"A program with a very long name that will not fit on one line at all\"\nMODE SEQUENTIAL\n\n");
            for (int i = 1; i <= 10; i++)
            {
                sb.Append("STATION \"Station number ").Append(i).Append(" with a long descriptive name\"\n")
                  .Append("    WORKNOTE \"Keep the core tight, breathe out on the effort and keep going until the beep\"\n")
                  .Append("    RESTNOTE \"Walk around slowly and get some water\"\n    WORK 30s\nEND STATION\n\n");
            }
            Shot("crowded", sb.ToString(), e => e.SnapTo(Phase.Active, 3, 10.5, 100.5, false));
        }

        [Fact]
        public void StationNames_ThatAlreadyStartWithTheirNumber_AreNotNumberedTwice()
        {
            Assert.Equal("3. Goblet Squats", ClockSurface.NumberedName(new StationRow { Number = 3, Name = "3. Goblet Squats" }));
            Assert.Equal("3. Goblet Squats", ClockSurface.NumberedName(new StationRow { Number = 3, Name = "Goblet Squats" }));
            Assert.Equal("1. 10 burpees", ClockSurface.NumberedName(new StationRow { Number = 1, Name = "10 burpees" }));
            Assert.Equal("2) Rower", ClockSurface.NumberedName(new StationRow { Number = 2, Name = "2) Rower" }));
        }

        /// <summary>
        /// Not a check: the clock with the control bar showing, at two screen shapes, to PNG files.
        /// Runs only when GYMCLOCK_SCREENSHOTS names a folder.
        /// </summary>
        [Fact]
        public void Screenshots_ControlBarOverTheClock_WhenAsked()
        {
            string folder = Environment.GetEnvironmentVariable("GYMCLOCK_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            StringBuilder sb = new StringBuilder("PROGRAM \"Everest Session 2 - Resistance Circuit\"\nMODE CIRCUIT\n\nREPEAT 3\n    WORK 1m\n    REST 30s\nEND\n\n");
            string[] names = { "Wall Calf Raises", "RDLs", "Goblet Squats", "Dead Bugs", "Seated Weighted Calf Raises", "Kettlebell Bird Dogs", "Wall Sit", "Plank or V-Hold" };
            for (int i = 0; i < names.Length; i++)
                sb.Append("STATION \"").Append(i + 1).Append(". ").Append(names[i]).Append("\"\n    WORKNOTE \"3 x 12-15 reps - one set per work period\"\nEND STATION\n\n");

            AppSession session = new AppSession(Settings(sb.ToString(), 0), new SilentCuePlayer());
            DisplayFrame frame = session.Display.BuildFrame(true);
            frame.TimeOfDay = "10:15:45 AM";
            frame.Licensee = "Licensed to Test School";

            WpfTestHost.Run(() =>
            {
                foreach (Size size in new[] { new Size(1280, 800), new Size(1707, 960) })
                {
                    ClockSurface surface = new ClockSurface { ReduceMotion = true, BarShowing = true, Frame = frame };
                    surface.Measure(size);
                    surface.Arrange(new Rect(size));
                    surface.UpdateLayout();
                    RenderTargetBitmap first = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                    first.Render(surface);           // works out the bar's strip

                    Rect slot = surface.BarSlot;
                    double height = Math.Min(slot.Height, 84);
                    ControlBar bar = new ControlBar
                    {
                        DataContext = new DisplayCommands(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { })
                    };
                    System.Windows.Controls.Viewbox box = new System.Windows.Controls.Viewbox
                    {
                        Child = bar, Stretch = Stretch.Uniform, Width = slot.Width, Height = height,
                    };
                    System.Windows.Controls.Canvas canvas = new System.Windows.Controls.Canvas();
                    System.Windows.Controls.Canvas.SetLeft(box, slot.X);
                    System.Windows.Controls.Canvas.SetTop(box, slot.Bottom - height);
                    canvas.Children.Add(box);
                    System.Windows.Controls.Grid grid = new System.Windows.Controls.Grid();
                    grid.Children.Add(new ClockSurface { ReduceMotion = true, BarShowing = true, Frame = frame });
                    grid.Children.Add(canvas);
                    grid.Measure(size);
                    grid.Arrange(new Rect(size));
                    grid.UpdateLayout();

                    RenderTargetBitmap bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(grid);
                    Save(bitmap, Path.Combine(folder, "bar-" + (int)size.Width + "x" + (int)size.Height + ".png"));
                }
            });
        }

        // ================================================================ helper

        private static TimerSettings Settings(string script, int prep)
        {
            return new TimerSettings
            {
                ProgramScript = script ?? string.Empty,
                UseBuiltProgram = script != null,
                PrepSeconds = prep,
                SoundEnabled = false,
                ShowClock = true,
            };
        }

        private static void Shot(string name, string script, Action<SessionEngine> setup, bool hints = false, int prep = 0,
            string layout = ClockSurface.StandardLayout, int margin = 0, bool secondScreen = false, Action<DisplayFrame> tweak = null)
        {
            Shot(name, Settings(script, prep), setup, hints, layout, margin, secondScreen, tweak);
        }

        private static void Shot(string name, TimerSettings settings, Action<SessionEngine> setup, bool hints = false,
            string layout = ClockSurface.StandardLayout, int margin = 0, bool secondScreen = false, Action<DisplayFrame> tweak = null)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))) return;

            AppSession session = new AppSession(settings, new SilentCuePlayer());
            setup(session.Engine);
            session.Display.IsSecondScreen = secondScreen;
            DisplayFrame frame = session.Display.BuildFrame(hints);

            // The parts that depend on the time and this PC, fixed so the picture never changes.
            frame.TimeOfDay = "8:39 PM";
            frame.Licensee = "Licensed to Test School";
            frame.LicenceWarning = string.Empty;
            frame.Copyright = "© Chris Bucknell and James Vella 2026";
            if (tweak != null) tweak(frame);

            WpfTestHost.Run(() =>
            {
                ClockSurface surface = new ClockSurface { Layout = layout, SafeMargin = margin, ReduceMotion = true };
                surface.Frame = frame;
                surface.Measure(new Size(1280, 720));
                surface.Arrange(new Rect(0, 0, 1280, 720));
                surface.UpdateLayout();

                RenderTargetBitmap bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                Compare(name, bitmap);
            });
        }

        private static void Compare(string name, BitmapSource actual)
        {
            string folder = SnapshotFolder();
            string approvedPath = Path.Combine(folder, name + ".png");
            string newPath = Path.Combine(folder, name + ".new.png");

            if (!File.Exists(approvedPath))
            {
                Save(actual, newPath);
                Assert.Fail("No approved snapshot for " + name + ". Look at " + newPath + " and, if it is right, rename it to " + name + ".png.");
            }

            BitmapSource approved;
            using (FileStream file = File.OpenRead(approvedPath))
            {
                approved = BitmapFrame.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }

            byte[] a = Pixels(actual);
            byte[] b = Pixels(approved);
            Assert.True(a.Length == b.Length, name + ": the picture is a different size from the approved one.");

            int differing = 0;
            for (int i = 0; i < a.Length; i += 4)
            {
                if (Math.Abs(a[i] - b[i]) > 24 || Math.Abs(a[i + 1] - b[i + 1]) > 24 || Math.Abs(a[i + 2] - b[i + 2]) > 24) differing++;
            }
            double share = differing / (a.Length / 4.0);
            if (share >= 0.005)
            {
                Save(actual, newPath);
                Assert.Fail(string.Format("{0}: {1:P2} of pixels differ from the approved snapshot. The new picture is {2}.", name, share, newPath));
            }
            if (File.Exists(newPath)) File.Delete(newPath);
        }

        private static byte[] Pixels(BitmapSource source)
        {
            FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        private static void Save(BitmapSource bitmap, string path)
        {
            PngBitmapEncoder png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path)) png.Save(file);
        }

        /// <summary>tests/GymClock.Tests/Snapshots in the source tree (not the build output), so approved pictures are committed.</summary>
        private static string SnapshotFolder()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "GymClock.Tests.csproj"))) dir = dir.Parent;
            string folder = Path.Combine(dir != null ? dir.FullName : AppContext.BaseDirectory, "Snapshots");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
