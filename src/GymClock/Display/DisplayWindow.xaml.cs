using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GymClock.AI;
using GymClock.Link;
using Forms = System.Windows.Forms;

namespace GymClock.Wpf
{
    /// <summary>
    /// The clock (build guide P3.4): the version 5 replacement for MainForm. It owns the
    /// AppSession, ticks it every 40 ms, draws a DisplayFrame on the ClockSurface, and handles
    /// the keys and the mouse exactly as MainForm did (the plan's section 6.1). The settings,
    /// program and licence windows are still the old ones until phases 4 and 5 replace them.
    /// </summary>
    public partial class DisplayWindow : Window, IDisplayActions
    {
        private const double BarSeconds = 4;

        private readonly AppSession _session;
        private readonly DispatcherTimer _timer;
        private readonly DisplayCommands _commands;

        private bool _fullScreen;
        private Rectangle _windowed;
        private DateTime _hintHideAt;
        private string _toastMessage = string.Empty;
        private DateTime _toastHideAt;
        private DateTime _barHideAt;
        private System.Windows.Point _lastMouse = new System.Windows.Point(double.NaN, double.NaN);
        private UpdateInfo _update;
        private bool _closing;

        public DisplayWindow() : this(TimerSettings.Load())
        {
        }

        /// <summary>For tests: a clock on the given settings instead of this PC's settings file.</summary>
        public DisplayWindow(TimerSettings settings)
        {
            InitializeComponent();

            _session = new AppSession(settings ?? new TimerSettings(), new BeeperCuePlayer());
            _session.Engine.HintRequested += ShowHint;
            _session.SettingsApplied += delegate
            {
                // What is this window's own business when the settings in force change.
                ApplyAudioSettings();
                Topmost = _session.Settings.AlwaysOnTop;
            };
            _session.Link.Changed += ApplyAudioSettings;
            _session.Link.ToastRequested += ShowToast;

            ControlHost.Attach(this, this, _session, () => _update);

            Title = "Gym Interval Clock Version " + AppVersion.Display;
            System.Drawing.Icon icon = AppIconProvider.GetIcon();
            if (icon != null)
            {
                try { Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
                catch { }
            }

            _commands = new DisplayCommands(GoBack, TogglePause, Skip, ResetSession, ToggleSound,
                () => ControlHost.Open(ControlViewModel.LinkPage), () => ControlHost.Open(ControlViewModel.SessionPage), () => SetFullScreen(!_fullScreen));
            Bar.DataContext = _commands;

            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };   // 25 fps
            _timer.Tick += delegate { Tick(); };

            // The keys must reach this window whenever it is active, including after an old dialog closes.
            Activated += delegate { if (!IsKeyboardFocusWithin) Keyboard.Focus(this); };

            Surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
            Surface.TouchDown += Surface_TouchDown;

            ApplyAudioSettings();
            ShowHint(14);
            UsageLog.Begin();
        }

        /// <summary>The session this clock runs (for tests and the Control window).</summary>
        public AppSession Session
        {
            get { return _session; }
        }

        public bool IsFullScreen
        {
            get { return _fullScreen; }
        }

        private TimerSettings Settings
        {
            get { return _session.Settings; }
        }

        private TimerSettings OwnSettings
        {
            get { return _session.OwnSettings; }
        }

        private bool IsFollowing
        {
            get { return _session.Link.IsFollowing; }
        }

        // ================================================================ start and close

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowed = NativeScreen.Centred(NativeScreen.ScreenOf(this), 1280, 720);
            SetFullScreen(true);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (_timer.IsEnabled) return;
            Render();
            _timer.Start();
            _session.Link.Start();
            _ = AiAssistantRuntime.StartWarmupIfNeeded();
            StartBackgroundChecks(false);
            OfferRenewalIfDue();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel || _closing) return;
            _closing = true;
            _timer.Stop();
            ControlHost.CloseWindow();
            _session.Link.Stop(false);     // a second screen goes back to its own settings before they are saved
            _session.SaveOwnSettings();
            UsageLog.End();
        }


        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // The link was stopped in OnClosing, as MainForm did; the session is not disposed here
            // because that can wait on the link's threads. The app ends with this window.
            if (Application.Current != null) Application.Current.Shutdown();
        }

        // ================================================================ each tick

        private void Tick()
        {
            _session.Tick();
            Render();

            if (BarBox.Visibility == Visibility.Visible && DateTime.Now >= _barHideAt && !BarBox.IsMouseOver)
                BarBox.Visibility = Visibility.Collapsed;
        }

        /// <summary>Builds this moment's frame and hands it to the surface.</summary>
        public void Render()
        {
            DisplayModel display = _session.Display;
            display.IsSecondScreen = IsFollowing;
            display.HasLeaderProgram = _session.Link.HasLeaderProgram;

            SessionEngine engine = _session.Engine;
            bool showHint = DateTime.Now < _hintHideAt || engine.Phase == Phase.Idle || engine.Phase == Phase.Finished || engine.IsPaused;
            DisplayFrame frame = display.BuildFrame(showHint);

            if (_update != null && _update.IsNewerThanThis) frame.UpdateNotice = _update.Description + " - press F3";
            bool warning;
            frame.LinkBadge = _session.Link.BadgeText(showHint, out warning);   // timing detail only while the hints show (H)
            frame.LinkBadgeIsWarning = warning;
            if (!string.IsNullOrEmpty(_toastMessage) && DateTime.Now < _toastHideAt) frame.Toast = _toastMessage;

            // A second screen can be set to show only some of a Parallel program's stations (Link displays).
            if (frame.IsParallel)
            {
                int count = frame.Tiles.Count;
                frame.Tiles = frame.Tiles.Where(t => _session.Link.ShowsStation(t.StationIndex, count)).ToList();
            }

            // The look of this screen is this PC's own, never the main PC's.
            Surface.Layout = OwnSettings.DisplayLayout;
            Surface.SafeMargin = OwnSettings.SafeMargin;
            Surface.ReduceMotion = ReduceMotionOn(OwnSettings.ReduceMotion);
            Surface.BarShowing = BarBox.Visibility == Visibility.Visible;
            Surface.Frame = frame;
            PlaceBar();

            _commands.Refresh(engine, Beeper.Enabled, _fullScreen);
        }

        /// <summary>on, off, or (system) whatever Windows' "Animation effects" setting says.</summary>
        public static bool ReduceMotionOn(string setting)
        {
            switch (TimerSettings.ReadReduceMotion(setting))
            {
                case "on": return true;
                case "off": return false;
                default: return !SystemParameters.ClientAreaAnimation;
            }
        }

        // ================================================================ keyboard

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;

            // Alt combinations (Alt+F4, Alt+Tab) belong to Windows.
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 || e.Key == Key.System) return;
            Key key = e.Key;

            if (key == Key.L)
            {
                ControlHost.Open(ControlViewModel.LinkPage);
                e.Handled = true;
                return;
            }

            // A second screen only displays: the session keys belong to the main PC.
            if (IsFollowing)
            {
                switch (key)
                {
                    case Key.M:
                        OwnSettings.LinkSound = !OwnSettings.LinkSound;
                        Settings.SoundEnabled = OwnSettings.LinkSound;
                        ApplyAudioSettings();
                        OwnSettings.Save();
                        ShowToast(OwnSettings.LinkSound ? "SOUND ON FOR THIS SCREEN" : "SOUND OFF FOR THIS SCREEN", 3);
                        break;
                    case Key.T:
                        OwnSettings.AlwaysOnTop = !OwnSettings.AlwaysOnTop;
                        Settings.AlwaysOnTop = OwnSettings.AlwaysOnTop;
                        Topmost = OwnSettings.AlwaysOnTop;
                        OwnSettings.Save();
                        ShowHint(4);
                        break;
                    case Key.F:
                    case Key.F11:
                        SetFullScreen(!_fullScreen);
                        break;
                    case Key.F6:
                        MoveToNextScreen();
                        break;
                    case Key.F3:
                        ControlHost.OpenAbout(false);
                        break;
                    case Key.C:
                        ControlHost.Open(ControlViewModel.LinkPage);
                        break;
                    case Key.H:
                    case Key.F1:
                        ShowHint(15);
                        break;
                    case Key.Escape:
                        if (!ControlHost.ClosePanel() && _fullScreen) SetFullScreen(false);
                        break;
                    case Key.Q:
                        Close();
                        break;
                    case Key.LeftShift:
                    case Key.RightShift:
                    case Key.LeftCtrl:
                    case Key.RightCtrl:
                    case Key.LWin:
                    case Key.RWin:
                    case Key.CapsLock:
                    case Key.NumLock:
                        break;
                    default:
                        FollowerControlBlocked();
                        break;
                }
                e.Handled = true;
                return;
            }

            int stationIndex = -1;
            if (key >= Key.D1 && key <= Key.D9) stationIndex = key - Key.D1;
            else if (key >= Key.NumPad1 && key <= Key.NumPad9) stationIndex = key - Key.NumPad1;

            if (stationIndex >= 0)
            {
                _session.Engine.JumpToStation(stationIndex);
                e.Handled = true;
                return;
            }

            switch (key)
            {
                case Key.Space:
                case Key.Enter:
                    TogglePause();
                    break;

                case Key.R:
                    ResetSession();
                    break;

                case Key.S:
                case Key.F2:
                    ShowProgramEditor(false);
                    break;

                case Key.A:
                    ShowProgramEditor(true);
                    break;

                case Key.Right:
                case Key.PageDown:
                case Key.N:
                    Skip();
                    break;

                case Key.Left:
                case Key.PageUp:
                case Key.P:
                    GoBack();
                    break;

                case Key.M:
                    ToggleSound();
                    break;

                case Key.T:
                    Settings.AlwaysOnTop = !Settings.AlwaysOnTop;
                    Topmost = Settings.AlwaysOnTop;
                    Settings.Save();
                    ShowHint(4);
                    break;

                case Key.F:
                case Key.F11:
                    SetFullScreen(!_fullScreen);
                    break;

                case Key.F6:
                    MoveToNextScreen();
                    break;

                case Key.F3:
                    ControlHost.OpenAbout(false);
                    break;

                case Key.C:
                    ControlHost.Open(ControlViewModel.SessionPage);
                    break;

                case Key.H:
                case Key.F1:
                    ShowHint(15);
                    break;

                case Key.Escape:
                    if (!ControlHost.ClosePanel() && _fullScreen) SetFullScreen(false);
                    break;

                case Key.Q:
                    Close();
                    break;

                default:
                    return;     // not ours: leave it unhandled
            }

            e.Handled = true;
        }

        // ================================================================ actions (keys and the control bar)

        private void TogglePause()
        {
            if (IsFollowing) { FollowerControlBlocked(); return; }
            _session.Engine.TogglePause();
        }

        private void Skip()
        {
            if (IsFollowing) { FollowerControlBlocked(); return; }
            if (_session.Engine.Phase == Phase.Idle) _session.Engine.Start(); else _session.Engine.Advance();
        }

        private void GoBack()
        {
            if (IsFollowing) { FollowerControlBlocked(); return; }
            _session.Engine.GoBack();
        }

        private void ResetSession()
        {
            if (IsFollowing) { FollowerControlBlocked(); return; }
            _session.Engine.Reset();
            ShowHint(8);
        }

        private void ToggleSound()
        {
            if (IsFollowing) return;
            Settings.SoundEnabled = !Settings.SoundEnabled;
            ApplyAudioSettings();
            Settings.Save();
            ShowHint(4);
        }

        // ================================================================ mouse, touch and the control bar

        private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsFollowing) FollowerControlBlocked(); else TogglePause();
        }

        private void Surface_TouchDown(object sender, TouchEventArgs e)
        {
            // A touch shows the control bar instead of pausing (handling it stops the click that would follow).
            ShowBar();
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            if (e.StylusDevice != null) return;
            System.Windows.Point p = e.GetPosition(this);
            bool moved = double.IsNaN(_lastMouse.X) || Math.Abs(p.X - _lastMouse.X) + Math.Abs(p.Y - _lastMouse.Y) > 3;
            _lastMouse = p;
            if (moved) ShowBar();
        }

        private void ShowBar()
        {
            if (IsFollowing || !OwnSettings.ControlBar) return;
            bool wasHidden = BarBox.Visibility != Visibility.Visible;
            BarBox.Visibility = Visibility.Visible;
            _barHideAt = DateTime.Now.AddSeconds(BarSeconds);
            if (wasHidden) PlaceBar();
        }

        /// <summary>Fits the control bar into the strip the clock keeps for it (at most 84 high, centred).</summary>
        private void PlaceBar()
        {
            Rect slot = Surface.BarSlot;
            if (slot.Width < 10 || slot.Height < 10) return;
            double height = Math.Min(slot.Height, 84);
            System.Windows.Controls.Canvas.SetLeft(BarBox, slot.X);
            System.Windows.Controls.Canvas.SetTop(BarBox, slot.Bottom - height);
            BarBox.Width = slot.Width;
            BarBox.Height = height;
        }

        // ================================================================ hints, toasts, sound

        private void ShowHint(double seconds)
        {
            _hintHideAt = DateTime.Now.AddSeconds(seconds);
        }

        private void ShowToast(string message, double seconds)
        {
            _toastMessage = message;
            _toastHideAt = DateTime.Now.AddSeconds(seconds);
        }

        private void FollowerControlBlocked()
        {
            ShowToast("THE MAIN PC CONTROLS THIS SCREEN  -  PRESS L FOR LINK SETTINGS", 3);
        }

        private void ApplyAudioSettings()
        {
            // A second screen has its own sound switch (Link displays), off by default.
            Beeper.Enabled = IsFollowing ? OwnSettings.LinkSound : Settings.SoundEnabled;
            Beeper.Volume = OwnSettings.Volume;
        }

        // ================================================================ window

        private void SetFullScreen(bool on)
        {
            Forms.Screen screen = NativeScreen.ScreenOf(this);
            if (on)
            {
                if (!_fullScreen && IsLoaded) _windowed = NativeScreen.GetBounds(this);
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                NativeScreen.SetBounds(this, screen.Bounds);
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Normal;
                bool usable = _windowed.Width > 300 && _windowed.Height > 200 && ControlHost.IsMostlyOn(_windowed, screen.WorkingArea);
                NativeScreen.SetBounds(this, usable ? _windowed : NativeScreen.Centred(screen, 1280, 720));
            }

            _fullScreen = on;
            Topmost = Settings.AlwaysOnTop;
        }

        /// <summary>Sends the clock to the next monitor - handy when the projector is the second display.</summary>
        private void MoveToNextScreen()
        {
            Forms.Screen[] screens = Forms.Screen.AllScreens;
            if (screens.Length < 2) { ShowHint(4); return; }

            Forms.Screen current = NativeScreen.ScreenOf(this);
            int index = 0;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].DeviceName == current.DeviceName) { index = i; break; }
            }

            Forms.Screen next = screens[(index + 1) % screens.Length];
            if (_fullScreen)
            {
                NativeScreen.SetBounds(this, next.Bounds);
            }
            else
            {
                Rectangle b = NativeScreen.GetBounds(this);
                NativeScreen.SetBounds(this, new Rectangle(next.WorkingArea.X + 60, next.WorkingArea.Y + 60, b.Width, b.Height));
            }
        }

        // ================================================================ the old windows (until phases 4 and 5)

        /// <summary>
        /// The single combined window for general settings and program editing.
        /// A jumps straight into the AI assistant; S opens the editor at rest.
        /// </summary>
        private void ShowProgramEditor(bool launchWizard)
        {
            using (ProgramEditorForm dialog = new ProgramEditorForm(Settings))
            {
                dialog.LinkSetupRequested = owner => ShowLinkWindow(owner);
                if (launchWizard) dialog.Shown += delegate { dialog.LaunchAiAssistant(); };

                if (dialog.ShowDialog(NativeScreen.Owner(this)) == Forms.DialogResult.OK)
                {
                    bool justActivatedProgram = dialog.Result.UseBuiltProgram
                        && (!Settings.UseBuiltProgram || Settings.ProgramScript != dialog.Result.ProgramScript);

                    // The session saves and applies them (keeping the link settings). It returns false if
                    // "Link displays..." in the settings window made this PC a second screen: the edits are
                    // then kept as its own settings for later, and the main PC's session stays on screen.
                    if (!_session.ReplaceSettings(dialog.Result, reset: true)) return;

                    if (justActivatedProgram)
                    {
                        ShowToast("PROGRAM READY: " + Settings.EffectiveProgram().Name.ToUpperInvariant(), 5);
                        ShowHint(6);
                    }
                    else
                    {
                        ShowHint(4);
                    }
                }
            }
        }

        /// <summary>
        /// The Link displays page in a window of its own, over the old program editor (its "Link
        /// displays..." button): that editor blocks the Control window while it is open. Every choice
        /// takes effect at once, so the page shows the link coming up live.
        /// </summary>
        private void ShowLinkWindow(Forms.IWin32Window owner)
        {
            PageWindow window = new PageWindow("Link displays", 1100, 760);
            window.ShowPage(ControlHost.LinkPage(_session, window.Dialogs), owner ?? NativeScreen.Owner(this));
            ShowHint(6);
        }

        /// <summary>
        /// Offered once at startup and never again during the session. A dialog appearing over a
        /// projected countdown halfway through a circuit would be unforgivable.
        /// </summary>
        private void OfferRenewalIfDue()
        {
            LicenceStatus status = Licensing.Current;
            if (!Licensing.ShouldPromptForRenewal(status)) return;

            Licensing.RecordRenewalPrompt(7);     // quiet for a week even if they close it without sending
            ControlHost.OpenAbout(true);
        }

        // ================================================================ for the Control window (IDisplayActions)

        string IDisplayActions.ScreenDescription
        {
            get
            {
                Forms.Screen screen = NativeScreen.ScreenOf(this);
                Forms.Screen[] all = Forms.Screen.AllScreens;
                string name = screen.Primary ? "main screen" : "screen " + (Array.FindIndex(all, s => s.DeviceName == screen.DeviceName) + 1);
                return name + " · " + screen.Bounds.Width + " × " + screen.Bounds.Height;
            }
        }

        bool IDisplayActions.HasOtherScreen
        {
            get { return Forms.Screen.AllScreens.Length > 1; }
        }

        void IDisplayActions.MoveToNextScreen()
        {
            MoveToNextScreen();
        }

        void IDisplayActions.ToggleFullScreen()
        {
            SetFullScreen(!_fullScreen);
        }

        void IDisplayActions.ApplySound()
        {
            ApplyAudioSettings();
        }

        /// <summary>
        /// Looks online for a licence issued to this machine, and for a newer version. Runs off the
        /// UI thread and fails silently - with no wifi, or the site blocked, nothing happens.
        /// </summary>
        private void StartBackgroundChecks(bool force)
        {
            Dispatcher dispatcher = Dispatcher;
            System.Threading.Tasks.Task.Run(async delegate
            {
                bool installed = false;
                try
                {
                    if (OnlineServices.ShouldCheck(Licensing.Current, force)) installed = await OnlineServices.TryFetchLicenceAsync();
                }
                catch { }

                UpdateInfo update = null;
                try { update = await OnlineServices.TryFetchVersionAsync(); }
                catch { }

                try { await OnlineServices.TryFetchContactAsync(); }
                catch { }

                // Newer exercise library (at most once a day). Only the app-owned copy is replaced.
                try { await ExerciseCatalog.CheckForUpdateAsync(force); }
                catch { }

                try
                {
                    dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (update != null && update.IsNewerThanThis) _update = update;
                        if (installed) Licensing.Refresh();
                    }));
                }
                catch { }
            });
        }
    }
}
