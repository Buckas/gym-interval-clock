using System;
using System.Linq;
using GymClock.Wpf;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>Phase 4 of version 5: the Session, Settings and About pages, tested through their view models.</summary>
    public class ControlPagesTests
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
END STATION

STATION ""Box step ups""
END STATION

STATION ""Treadmill""
END STATION
";

        private const string Parallel = @"PROGRAM ""Own Clocks""
MODE PARALLEL
ROTATIONS 2

STATION ""Short""
    WORK 10s
END STATION

STATION ""Long""
    WORK 20s
END STATION
";

        private sealed class FakeDisplay : IDisplayActions
        {
            public int Moves, FullScreenToggles, SoundApplied;
            public bool FullScreen = true;
            public string ScreenDescription { get { return "main screen · 1920 × 1080"; } }
            public bool IsFullScreen { get { return FullScreen; } }
            public bool HasOtherScreen { get { return true; } }
            public void MoveToNextScreen() { Moves++; }
            public void ToggleFullScreen() { FullScreenToggles++; FullScreen = !FullScreen; }
            public void ApplySound() { SoundApplied++; }
        }

        private static AppSession Session(string script, int prep = 0)
        {
            return new AppSession(new TimerSettings { ProgramScript = script, UseBuiltProgram = true, PrepSeconds = prep, SoundEnabled = true }, new SilentCuePlayer());
        }

        // ================================================================ Session

        [Fact]
        public void SessionPage_ShowsWhatIsRunning_AndItsButtonsDriveTheClock()
        {
            WpfTestHost.Run(() =>
            {
                AppSession session = Session(Circuit);
                FakeDisplay display = new FakeDisplay();
                SessionViewModel page = new SessionViewModel(session, display);

                Assert.Equal("Cardio Circuit", page.ProgramName);
                Assert.Equal("GROUPS ROTATE, ONE CLOCK", page.ModeTag);
                Assert.StartsWith("Groups rotate, one clock · 3 stations · ", page.Description);
                Assert.Equal("Start", page.PauseText);
                Assert.Equal("Display is on: main screen · 1920 × 1080", page.ScreenText);
                Assert.True(page.ShowStations);
                Assert.Equal(new[] { "1 · Spin bike", "2 · Box step ups", "3 · Treadmill" }, page.Stations.Select(s => s.Label).ToArray());
                Assert.Equal(3, page.UpNext.Count);
                Assert.Equal("Work · Spin bike", page.UpNext[0].Name);

                page.Pause.Execute(null);
                Assert.Equal(Phase.Active, session.Engine.Phase);
                Assert.Equal("Pause", page.PauseText);
                Assert.Equal("WORK", page.PhaseTag);

                page.Pause.Execute(null);
                Assert.True(session.Engine.IsPaused);
                Assert.Equal("Resume", page.PauseText);
                page.Pause.Execute(null);

                page.Skip.Execute(null);
                Assert.Equal(1, session.Engine.Index);
                page.Back.Execute(null);

                page.Stations[2].Jump.Execute(null);
                Assert.Equal(2, session.Engine.CurrentBlock().StationIndex);

                page.Reset.Execute(null);
                Assert.Equal(Phase.Idle, session.Engine.Phase);

                page.ToggleFullScreen.Execute(null);
                page.MoveToNextScreen.Execute(null);
                Assert.Equal(1, display.FullScreenToggles);
                Assert.Equal(1, display.Moves);
                Assert.Equal("Full screen (F11)", page.FullScreenText);

                page.SoundOn = false;
                Assert.False(session.Settings.SoundEnabled);
                Assert.Equal(1, display.SoundApplied);
                page.Volume = 40;
                Assert.Equal(0.40, session.OwnSettings.Volume, 3);
            });
        }

        [Fact]
        public void SessionPage_Parallel_HasNoStationChipsAndNoSkip()
        {
            WpfTestHost.Run(() =>
            {
                AppSession session = Session(Parallel);
                SessionViewModel page = new SessionViewModel(session, new FakeDisplay());
                page.Pause.Execute(null);

                Assert.Equal("EACH STATION ITS OWN CLOCK", page.ModeTag);
                Assert.False(page.ShowStations);
                Assert.Empty(page.Stations);
                Assert.False(page.Skip.CanExecute(null));
                Assert.Equal("RUNNING", page.PhaseTag);
            });
        }

        // ================================================================ Settings

        [Fact]
        public void SettingsPage_HandsEveryChangeOn_AndChecksWhatIsTyped()
        {
            WpfTestHost.Run(() =>
            {
                TimerSettings start = new TimerSettings { PrepSeconds = 10, ClockFormat = "h:mm tt" };
                TimerSettings committed = null;
                FakeDialogs dialogs = new FakeDialogs { ColourAnswer = "#123456" };
                SettingsViewModel page = new SettingsViewModel(start, s => committed = s, dialogs);

                page.PrepSeconds = 999;            // kept to 300
                page.LayoutIndex = 1;
                page.SafeMargin = 3;
                page.ReduceMotion = true;
                page.ShowStationsTable = false;
                page.Volume = 2;                   // kept to 5%
                page.Colours.First(c => c.Name == "Rest").Pick.Execute(null);
                page.ClockFormat = "H:mm";
                page.ClockFormat = "%%%";          // not a format: the last good one is kept
                page.Flush();

                Assert.NotNull(committed);
                Assert.Equal(300, committed.PrepSeconds);
                Assert.Equal("bignumber", committed.DisplayLayout);
                Assert.Equal(3, committed.SafeMargin);
                Assert.Equal("on", committed.ReduceMotion);
                Assert.False(committed.ShowStationsTable);
                Assert.Equal(0.05, committed.Volume, 3);
                Assert.Equal("#123456", committed.RestColour);
                Assert.Equal("H:mm", committed.ClockFormat);
                Assert.Equal(10, start.PrepSeconds);    // the page works on its own copy

                page.ReduceMotion = false;
                page.Flush();
                Assert.Equal("system", committed.ReduceMotion);
            });
        }

        [Fact]
        public void SettingsPage_FromTheOldEditor_JustEditsItsCopy()
        {
            WpfTestHost.Run(() =>
            {
                SettingsViewModel page = new SettingsViewModel(new TimerSettings(), null, new FakeDialogs());
                page.PrepSeconds = 25;
                page.Flush();
                Assert.Equal(25, page.Edited.PrepSeconds);
            });
        }

        [Fact]
        public void ApplyingSettings_KeepsTheProgramAndTheRunningSession()
        {
            WpfTestHost.Run(() =>
            {
                AppSession session = Session(Circuit);
                session.Engine.Start();
                session.Engine.Advance();

                TimerSettings edited = session.OwnSettings.Clone();
                edited.PrepSeconds = 42;
                edited.WorkColour = "#000080";
                edited.ProgramScript = "PROGRAM \"Not this\"";     // not a general setting: ignored
                ControlHost.ApplyGeneral(session, edited);

                Assert.Equal(42, session.Settings.PrepSeconds);
                Assert.Equal("#000080", session.Settings.WorkColour);
                Assert.Contains("Cardio Circuit", session.Settings.ProgramScript);
                Assert.Equal(Phase.Active, session.Engine.Phase);
                Assert.Equal(1, session.Engine.Index);
            });
        }

        [Fact]
        public void GeneralSettings_AreEverythingOnTheSettingsPage_AndNothingElse()
        {
            TimerSettings from = new TimerSettings
            {
                PrepSeconds = 7, Volume = 0.3, SoundEnabled = false, ShowClock = false, ClockFormat = "H:mm", AlwaysOnTop = false,
                ShowStationsTable = false, ShowStationNameAsDescription = true, IdleColour = "#010203", CustomColour = "#040506",
                Theme = "dark", DisplayLayout = "stations", SafeMargin = 4, ReduceMotion = "off", ControlBar = false,
                WorkSeconds = 99, ProgramScript = "PROGRAM \"X\"", LinkRole = "lead",
            };
            TimerSettings to = new TimerSettings();
            to.CopyGeneralFrom(from);

            Assert.Equal(7, to.PrepSeconds);
            Assert.Equal("H:mm", to.ClockFormat);
            Assert.True(to.ShowStationNameAsDescription);
            Assert.Equal("#010203", to.IdleColour);
            Assert.Equal("#040506", to.CustomColour);
            Assert.Equal("dark", to.Theme);
            Assert.Equal("stations", to.DisplayLayout);
            Assert.False(to.ControlBar);
            Assert.Equal(45, to.WorkSeconds);
            Assert.Equal(string.Empty, to.ProgramScript);
            Assert.Equal("off", to.LinkRole);
        }

        [Theory]
        [InlineData("h:mm tt", true)]
        [InlineData("H:mm:ss", true)]
        [InlineData("", false)]
        [InlineData("%%%", false)]
        public void ClockFormats_AreCheckedBeforeUse(string format, bool ok)
        {
            Assert.Equal(ok, SettingsViewModel.IsValidClockFormat(format));
        }

        // ================================================================ About

        [Fact]
        public void About_WordsEachLicenceState_AsTheOldWindowDid()
        {
            LineState state;
            string text = AboutViewModel.StatusWording(new LicenceStatus
            {
                State = LicenceState.Licensed, Licence = new Licence { Name = "Casey Grammar", Expires = null }
            }, out state);
            Assert.StartsWith("Licensed to Casey Grammar.", text);
            Assert.Contains("does not expire", text);
            Assert.Equal(LineState.Good, state);

            text = AboutViewModel.StatusWording(new LicenceStatus { State = LicenceState.TrialEnding, DaysRemaining = 1 }, out state);
            Assert.Contains("1 day remaining.", text);
            Assert.Equal(LineState.Waiting, state);

            text = AboutViewModel.StatusWording(new LicenceStatus { State = LicenceState.TrialExpired }, out state);
            Assert.Contains("Enter a licence key below", text);
            Assert.Equal(LineState.Problem, state);

            text = AboutViewModel.StatusWording(new LicenceStatus { State = LicenceState.ClockRolledBack }, out state);
            Assert.Contains("system clock", text);
        }

        [Fact]
        public void About_LicenceRequest_ChecksTheDetails_AndOpensAnEmailWithThem()
        {
            WpfTestHost.Run(() =>
            {
                FakeDialogs dialogs = new FakeDialogs();
                AboutViewModel about = new AboutViewModel(dialogs, false, () => new TimerSettings(), () => null);
                about.Name = "";
                Assert.Equal("Please enter your name.", about.Problem());

                about.Name = "Test Teacher";
                about.School = "Test School";
                about.Email = "nobody";
                Assert.Contains("email address", about.Problem());
                about.Email = "teacher@example.org";
                Assert.Null(about.Problem());

                about.RequestKind = 2;
                about.Note = "Gym office PC";
                string body = about.Body();
                Assert.Contains("Name         : Test Teacher", body);
                Assert.Contains("Request type : a replacement - please move a seat", body);
                Assert.Contains("Computer ID  : " + Licensing.MachineId(), body);
                Assert.Contains("Note         : Gym office PC", body);
                Assert.StartsWith("To: " + Licensing.ContactDetails, about.Preview);

                about.SendRequest.Execute(null);
                Assert.Single(dialogs.Opened);
                Assert.StartsWith("mailto:", dialogs.Opened[0]);
                Assert.Contains(Uri.EscapeDataString("Test Teacher"), dialogs.Opened[0]);

                about.CopyRequest.Execute(null);
                Assert.Equal(about.Preview, dialogs.Clipboard);

                about.LicenceKey = "";
                about.Activate.Execute(null);
                Assert.Contains("Paste the licence key into the box first.", dialogs.Shown);
                about.LicenceKey = "not a key";
                about.Activate.Execute(null);
                Assert.Contains(dialogs.Shown, m => m.StartsWith("That licence key was not accepted", StringComparison.Ordinal));
            });
        }

        [Fact]
        public void About_BlockingCheck_SaysExit_NotClose()
        {
            WpfTestHost.Run(() =>
            {
                Assert.Equal("Exit", new AboutViewModel(new FakeDialogs(), true, () => null, () => null).CloseText);
                Assert.Equal("Close", new AboutViewModel(new FakeDialogs(), false, () => null, () => null).CloseText);
            });
        }

        /// <summary>
        /// Not a check: renders the four Phase 4 pages in the Control window to PNG files, dark and
        /// light. Runs only when GYMCLOCK_SCREENSHOTS names a folder.
        /// </summary>
        [Fact]
        public void Screenshots_OfThePhase4Pages_WhenAsked()
        {
            string folder = Environment.GetEnvironmentVariable("GYMCLOCK_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            System.IO.Directory.CreateDirectory(folder);

            WpfTestHost.Run(() =>
            {
                AppSession session = Session(Circuit, 10);
                session.Engine.SnapTo(Phase.Active, 2, 11.5, 252.5, false);
                FakeDialogs dialogs = new FakeDialogs();
                var pages = new System.Collections.Generic.Dictionary<string, Func<object>>
                {
                    [ControlViewModel.SessionPage] = () => new SessionPageView(new SessionViewModel(session, new FakeDisplay())),
                    [ControlViewModel.SettingsPage] = () => new SettingsPageView(new SettingsViewModel(session.OwnSettings, null, dialogs)),
                    [ControlViewModel.LinkPage] = () => new LinkPageView(new LinkViewModel(session.OwnSettings,
                        () => new GymClock.Link.LinkStatus { Role = "lead", Code = "4821", LeaderRunning = true }, a => { }, dialogs)),
                    [ControlViewModel.AboutPage] = () => new AboutPageView(new AboutViewModel(dialogs, false, () => session.Settings, () => null)),
                };
                session.OwnSettings.LinkRole = "lead";

                foreach (string theme in new[] { "dark", "light" })
                {
                    ThemeService.Apply(theme);
                    foreach (string id in pages.Keys)
                    {
                        ControlViewModel model = new ControlViewModel(pages);
                        model.Navigate(id);
                        ControlWindow window = new ControlWindow(model)
                        {
                            Left = -20000, Top = -20000, Width = 1280, Height = 900, MinWidth = 0, MinHeight = 0,
                            ShowActivated = false, ShowInTaskbar = false,
                        };
                        window.Show();
                        window.UpdateLayout();
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

                        System.Windows.FrameworkElement content = (System.Windows.FrameworkElement)window.Content;
                        int w = (int)Math.Ceiling(content.ActualWidth), h = (int)Math.Ceiling(content.ActualHeight);
                        System.Windows.Media.DrawingVisual visual = new System.Windows.Media.DrawingVisual();
                        using (System.Windows.Media.DrawingContext dc = visual.RenderOpen())
                        {
                            dc.DrawRectangle(window.Background, null, new System.Windows.Rect(0, 0, w, h));
                            dc.DrawRectangle(new System.Windows.Media.VisualBrush(content), null, new System.Windows.Rect(0, 0, w, h));
                        }
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(visual);
                        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using (var file = System.IO.File.Create(System.IO.Path.Combine(folder, "page-" + id + "-" + theme + ".png"))) png.Save(file);
                        window.Close();
                    }
                }
                ThemeService.Apply("system");
            });
        }

        [Fact]
        public void ControlWindow_ShowsTheBuiltPages_AndMakesSettingsDependentOnesAgainEachTime()
        {
            WpfTestHost.Run(() =>
            {
                int made = 0;
                ControlViewModel model = new ControlViewModel(new System.Collections.Generic.Dictionary<string, Func<object>>
                {
                    [ControlViewModel.LinkPage] = () => { made++; return new object(); },
                });
                model.Navigate(ControlViewModel.LinkPage);
                object first = model.CurrentView;
                model.Navigate(ControlViewModel.SessionPage);
                Assert.IsType<PlaceholderPageView>(model.CurrentView);
                model.Navigate(ControlViewModel.LinkPage);
                Assert.NotSame(first, model.CurrentView);
                Assert.Equal(2, made);
            });
        }
    }
}
