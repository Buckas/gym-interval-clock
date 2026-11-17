using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;
using GymClock.AI;
using GymClock.Link;

namespace GymClock
{
    /// <summary>
    /// The whole display is custom painted so every element scales to the projector
    /// resolution automatically - there are no fixed-size controls to fight with.
    ///
    /// The timing engine walks a flattened List&lt;RuntimeBlock&gt; (built by
    /// WorkoutProgram.BuildRuntimeSequence) one block at a time - the same
    /// approach for Shared Timing (one sequence, no station is ever "active") and
    /// Sequential Stations (each station's own blocks appear back to back,
    /// tagged with which station they belong to). A settings file with no
    /// program built yet (TimerSettings.ProgramScript empty) is synthesised into
    /// a Shared-timing program by TimerSettings.EffectiveProgram - see
    /// UsingLegacyStationRotation below for how that keeps today's rotating
    /// station highlight working unchanged even though Shared Timing itself
    /// never highlights a station.
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>Settings, timing engine, display model and link: everything that is not this window.</summary>
        private readonly AppSession _session;

        // These read the session under the names the drawing code already uses. They are temporary:
        // this window, and they with it, is replaced by the new Display in Phase 3 of the build guide.
        private TimerSettings _settings { get { return _session.Settings; } }
        private SessionEngine _engine { get { return _session.Engine; } }
        private DisplayModel _display { get { return _session.Display; } }   // every word and colour on the clock
        private Phase _phase { get { return _engine.Phase; } }
        private double _phaseLength { get { return _engine.PhaseLength; } }
        private RunClock _phaseClock { get { return _engine.PhaseClock; } }
        private RunClock _sessionClock { get { return _engine.SessionClock; } }
        private WorkoutProgram _program { get { return _engine.Program; } }
        private IReadOnlyList<RuntimeBlock> _sequence { get { return _engine.Sequence; } }
        private int _index { get { return _engine.Index; } }
        private ParallelRotationPlan _parallelPlan { get { return _engine.ParallelPlan; } }
        private bool InTimedPhase { get { return _engine.InTimedPhase; } }
        private bool IsPaused { get { return _engine.IsPaused; } }
        private bool UsingLegacyStationRotation { get { return _engine.UsingLegacyStationRotation; } }
        private bool IsParallelActive { get { return _engine.IsParallelActive; } }
        private RuntimeBlock CurrentBlock() { return _engine.CurrentBlock(); }
        private RuntimeBlock BlockAt(int index) { return _engine.BlockAt(index); }
        private int NextIndex() { return _engine.NextIndex(); }
        private Station LegacyStationAt(int index) { return _engine.LegacyStationAt(index); }
        private Station LegacyCurrentStation() { return _engine.LegacyCurrentStation(); }
        private double RemainingSeconds() { return _engine.RemainingSeconds(); }
        private List<StationPosition> ComputeParallelPositions() { return _engine.ComputeParallelPositions(); }
        private void ResetSession() { _engine.Reset(); }
        private void StartSession() { _engine.Start(); }
        private void Advance() { _engine.Advance(); }
        private void GoBack() { _engine.GoBack(); }
        private void TogglePause() { _engine.TogglePause(); }
        private void JumpToStation(int stationIndex) { _engine.JumpToStation(stationIndex); }
        private void RunTick() { _engine.Tick(); }
        private readonly System.Windows.Forms.Timer _ticker = new System.Windows.Forms.Timer();

        private bool _fullScreen;
        private Rectangle _windowedBounds;
        private DateTime _hintHideAt;

        /// <summary>A brief on-screen confirmation, e.g. right after building a program with Quick Setup - see ShowToast.</summary>
        private string _toastMessage = string.Empty;
        private DateTime _toastHideAt;

        private readonly Dictionary<string, Font> _fontCache = new Dictionary<string, Font>();
        private string _labelFamily;
        private string _numberFamily;
        private FontStyle _numberStyle = FontStyle.Bold;

        private UpdateInfo _update;

        private static readonly Color Ink = Color.White;
        private static readonly Color InkSoft = Color.FromArgb(205, 255, 255, 255);
        private static readonly Color InkFaint = Color.FromArgb(140, 255, 255, 255);

        public MainForm() : this(TimerSettings.Load())
        {
        }

        /// <summary>For tests: a clock window on the given settings instead of this PC's settings file.</summary>
        public MainForm(TimerSettings settings)
        {
            // The session (GymClock.Core) owns the settings, the engine, the display model and the link.
            _session = new AppSession(settings ?? new TimerSettings(), new BeeperCuePlayer());
            _session.Engine.Changed += delegate { Invalidate(); };
            _session.Engine.HintRequested += ShowHint;
            _session.SettingsApplied += delegate
            {
                // What is this window's own business when the settings in force change.
                ApplyAudioSettings();
                TopMost = _settings.AlwaysOnTop;
                Invalidate();
            };
            _session.Link.Changed += delegate { ApplyAudioSettings(); Invalidate(); };
            _session.Link.ToastRequested += ShowToast;

            Text = "Gym Interval Clock Version " + AppVersion.Display;
            Icon = AppIconProvider.GetIcon();
            BackColor = Color.Black;
            ClientSize = new Size(1280, 720);
            MinimumSize = new Size(520, 340);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            ResizeRedraw = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _labelFamily = PickFamily(new[] { "Segoe UI", "Tahoma", "Arial" }, FontStyle.Bold);

            // Heavy display faces are already bold, and many of them do not offer a
            // separate bold weight - asking for one would throw.
            if (FamilySupports("Segoe UI Black", FontStyle.Regular))
            {
                _numberFamily = "Segoe UI Black";
                _numberStyle = FontStyle.Regular;
            }
            else if (FamilySupports("Arial Black", FontStyle.Regular))
            {
                _numberFamily = "Arial Black";
                _numberStyle = FontStyle.Regular;
            }
            else
            {
                _numberFamily = _labelFamily;
                _numberStyle = FontStyle.Bold;
            }

            _ticker.Interval = 40;                          // 25 fps: smooth bar, accurate second flip
            _ticker.Tick += Ticker_Tick;

            MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                if (IsFollowing) FollowerControlBlocked(); else TogglePause();
            };

            ApplyAudioSettings();
            ShowHint(14);
            UsageLog.Begin();
        }

        // -------------------------------------------------------------- startup

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetFullScreen(true);
            _ticker.Start();
            StartLink();
            _ = AiAssistantRuntime.StartWarmupIfNeeded();
            StartBackgroundChecks(false);
            OfferRenewalIfDue();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _ticker.Stop();
            _session.Link.Stop(false);     // a second screen goes back to its own settings before they are saved
            _session.SaveOwnSettings();
            UsageLog.End();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------- timing engine

        private void Ticker_Tick(object sender, EventArgs e)
        {
            RunTick();
            LinkTick(); // main PC: tell the second screens where the session is
        }

        // ------------------------------------------------------------- settings

        private void ApplyAudioSettings()
        {
            // A second screen has its own sound switch (Link displays), off by default.
            Beeper.Enabled = IsFollowing ? OwnSettings.LinkSound : _settings.SoundEnabled;
            Beeper.Volume = OwnSettings.Volume;
        }

        /// <summary>
        /// The single combined window for general settings and program editing.
        /// "B" jumps straight into the AI Assistant; "S" opens the editor at rest.
        /// </summary>
        private void ShowProgramEditor(bool launchWizard)
        {
            using (ProgramEditorForm dialog = new ProgramEditorForm(_settings))
            {
                dialog.LinkSetupRequested = owner => ShowLinkSettings(owner);
                if (launchWizard) dialog.Shown += delegate { dialog.LaunchAiAssistant(); };

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    bool justActivatedProgram = dialog.Result.UseBuiltProgram
                        && (!_settings.UseBuiltProgram || _settings.ProgramScript != dialog.Result.ProgramScript);

                    // The session saves and applies them (keeping the link settings, which the Link displays
                    // window applies live). It returns false if "Link displays..." in the settings window made
                    // this PC a second screen: the edits are then kept as its own settings for later, and the
                    // main PC's session stays on screen.
                    if (!_session.ReplaceSettings(dialog.Result, reset: true)) return;

                    if (justActivatedProgram)
                    {
                        ShowToast("PROGRAM READY: " + _settings.EffectiveProgram().Name.ToUpperInvariant(), 5);
                        ShowHint(6);
                    }
                    else
                    {
                        ShowHint(4);
                    }
                }
            }
        }

        private void ShowToast(string message, double seconds)
        {
            _toastMessage = message;
            _toastHideAt = DateTime.Now.AddSeconds(seconds);
            Invalidate();
        }

        /// <summary>
        /// Looks online for a licence issued to this machine, and for a newer
        /// version. Runs off the UI thread and fails silently - if the gym has no
        /// wifi or the school blocks the site, nothing happens and nothing breaks.
        /// </summary>
        internal void StartBackgroundChecks(bool force)
        {
            System.Threading.Tasks.Task.Run(async delegate
            {
                bool installed = false;

                try
                {
                    if (OnlineServices.ShouldCheck(Licensing.Current, force))
                    {
                        installed = await OnlineServices.TryFetchLicenceAsync();
                    }
                }
                catch { }

                UpdateInfo update = null;
                try
                {
                    update = await OnlineServices.TryFetchVersionAsync();
                }
                catch { }

                try
                {
                    await OnlineServices.TryFetchContactAsync();
                }
                catch { }

                // Newer exercise library (at most once a day). Only the app-owned library copy is
                // replaced - the teacher's own exercises, edits, hidden list and equipment are untouched.
                try
                {
                    await GymClock.AI.ExerciseCatalog.CheckForUpdateAsync(force);
                }
                catch { }

                if (update != null && update.IsNewerThanThis) _update = update;

                if (IsDisposed || !IsHandleCreated) return;

                try
                {
                    bool refresh = installed;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (refresh) Licensing.Refresh();
                        Invalidate();
                    });
                }
                catch { }
            });
        }

        /// <summary>
        /// Offered once at startup and never again during the session. A modal dialog
        /// appearing over a projected countdown halfway through a circuit would be
        /// unforgivable, so this is deliberately start-up only.
        /// </summary>
        private void OfferRenewalIfDue()
        {
            LicenceStatus status = Licensing.Current;
            if (!Licensing.ShouldPromptForRenewal(status)) return;

            // Quiet for a week even if they close it without sending.
            Licensing.RecordRenewalPrompt(7);
            ShowLicenceRequest();
        }

        internal void ShowLicenceRequest()
        {
            using (LicenceRequestForm form = new LicenceRequestForm(Licensing.Current))
            {
                form.ShowDialog(this);
            }
            Invalidate();
        }

        private void ShowLicenceInfo()
        {
            using (LicenceDialog dialog = new LicenceDialog(Licensing.Current, false))
            {
                dialog.Settings = _settings;
                dialog.AvailableUpdate = _update;
                dialog.ShowDialog(this);
            }
            Invalidate();
        }

        // -------------------------------------------------------------- display

        private void ShowHint(double seconds)
        {
            _hintHideAt = DateTime.Now.AddSeconds(seconds);
            Invalidate();
        }

        private void SetFullScreen(bool on)
        {
            if (on)
            {
                if (!_fullScreen) _windowedBounds = Bounds;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                if (_windowedBounds.Width > 300 && _windowedBounds.Height > 200) Bounds = _windowedBounds;
            }

            _fullScreen = on;
            TopMost = _settings.AlwaysOnTop;
            Invalidate();
        }

        /// <summary>Sends the clock to the next monitor - handy when the projector is the second display.</summary>
        private void MoveToNextScreen()
        {
            Screen[] screens = Screen.AllScreens;
            if (screens.Length < 2) { ShowHint(4); return; }

            Screen current = Screen.FromControl(this);
            int index = 0;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].DeviceName == current.DeviceName) { index = i; break; }
            }

            Screen next = screens[(index + 1) % screens.Length];
            if (_fullScreen)
            {
                Bounds = next.Bounds;
            }
            else
            {
                Location = new Point(next.WorkingArea.X + 60, next.WorkingArea.Y + 60);
            }
            Invalidate();
        }

        // ------------------------------------------------------------- keyboard

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.L)
            {
                ShowLinkSettings();
                e.Handled = true;
                return;
            }

            // A second screen only displays: the session keys belong to the main PC.
            if (IsFollowing)
            {
                switch (e.KeyCode)
                {
                    case Keys.M:
                        OwnSettings.LinkSound = !OwnSettings.LinkSound;
                        _settings.SoundEnabled = OwnSettings.LinkSound;
                        ApplyAudioSettings();
                        OwnSettings.Save();
                        ShowToast(OwnSettings.LinkSound ? "SOUND ON FOR THIS SCREEN" : "SOUND OFF FOR THIS SCREEN", 3);
                        break;
                    case Keys.T:
                        OwnSettings.AlwaysOnTop = !OwnSettings.AlwaysOnTop;
                        _settings.AlwaysOnTop = OwnSettings.AlwaysOnTop;
                        TopMost = OwnSettings.AlwaysOnTop;
                        OwnSettings.Save();
                        ShowHint(4);
                        break;
                    case Keys.F:
                    case Keys.F11:
                        SetFullScreen(!_fullScreen);
                        break;
                    case Keys.F6:
                        MoveToNextScreen();
                        break;
                    case Keys.F3:
                        ShowLicenceInfo();
                        break;
                    case Keys.H:
                    case Keys.F1:
                        ShowHint(15);
                        break;
                    case Keys.Escape:
                        if (_fullScreen) SetFullScreen(false);
                        break;
                    case Keys.Q:
                        Close();
                        break;
                    case Keys.ShiftKey:
                    case Keys.ControlKey:
                    case Keys.Menu:
                        break;
                    default:
                        FollowerControlBlocked();
                        break;
                }
                e.Handled = true;
                return;
            }

            int stationIndex = -1;
            if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) stationIndex = e.KeyCode - Keys.D1;
            else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) stationIndex = e.KeyCode - Keys.NumPad1;

            if (stationIndex >= 0)
            {
                JumpToStation(stationIndex);
                e.Handled = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Space:
                case Keys.Enter:
                    TogglePause();
                    break;

                case Keys.R:
                    ResetSession();
                    ShowHint(8);
                    break;

                case Keys.S:
                case Keys.F2:
                    ShowProgramEditor(false);
                    break;

                case Keys.A:
                    ShowProgramEditor(true);
                    break;

                case Keys.Right:
                case Keys.PageDown:
                case Keys.N:
                    if (_phase == Phase.Idle) StartSession(); else Advance();
                    break;

                case Keys.Left:
                case Keys.PageUp:
                case Keys.P:
                    GoBack();
                    break;

                case Keys.M:
                    _settings.SoundEnabled = !_settings.SoundEnabled;
                    ApplyAudioSettings();
                    _settings.Save();
                    ShowHint(4);
                    break;

                case Keys.T:
                    _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
                    TopMost = _settings.AlwaysOnTop;
                    _settings.Save();
                    ShowHint(4);
                    break;

                case Keys.F:
                case Keys.F11:
                    SetFullScreen(!_fullScreen);
                    break;

                case Keys.F6:
                    MoveToNextScreen();
                    break;

                case Keys.F3:
                    ShowLicenceInfo();
                    break;

                case Keys.H:
                case Keys.F1:
                    ShowHint(15);
                    break;

                case Keys.Escape:
                    if (_fullScreen) SetFullScreen(false);
                    break;

                case Keys.Q:
                    Close();
                    break;
            }

            e.Handled = true;
        }

        // -------------------------------------------------------------- painting

        protected override void OnPaint(PaintEventArgs e)
        {
            // The display model words the hints differently on a second screen.
            _display.IsSecondScreen = IsFollowing;
            _display.HasLeaderProgram = _hasLeaderProgram;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            Rectangle client = ClientRectangle;
            if (client.Width < 40 || client.Height < 40) return;

            float w = client.Width;
            float h = client.Height;

            double remaining = RemainingSeconds();

            // Background: colour alone tells the room which phase they are in.
            // Each Parallel tile pulses its own background individually instead
            // (see DrawStationTile), so the shared neutral base behind them stays put.
            Color background = _display.BackgroundColour();
            if (!IsParallelActive && InTimedPhase) background = _display.ApplyPulse(background, remaining, _phaseClock.IsRunning);
            using (SolidBrush brush = new SolidBrush(background))
            {
                g.FillRectangle(brush, client);
            }

            float margin = h * 0.035f;

            // The station table, when shown, takes the left quarter of the screen.
            // Everything else that used to span the full width now lives in the
            // remaining pane, flush to the true right edge. Not shown once the
            // Parallel dashboard is up - it already lists every station.
            bool showTable = _settings.ShowStationsTable && !IsParallelActive && _program.Stations.Exists(s => s.ShowInPanel);
            float paneLeft = showTable ? (w / 4f) : 0f;
            float paneWidth = w - paneLeft;

            if (showTable)
            {
                RectangleF tableArea = new RectangleF(margin, margin, paneLeft - (margin * 1.5f), h - (margin * 2f));
                DrawStationsTable(g, tableArea, h);
            }

            // ---- wall clock, top right
            if (_settings.ShowClock)
            {
                DrawText(g, _display.CurrentTimeText(), _labelFamily, h * 0.075f, FontStyle.Bold, InkSoft,
                    new RectangleF(paneLeft + (paneWidth * 0.45f), margin, (paneWidth * 0.55f) - margin, h * 0.10f),
                    StringAlignment.Far, StringAlignment.Near);
            }

            // ---- configuration summary + session elapsed, top left of the pane
            // Box height is taller than the font's pixel size so descenders
            // (g, p, y, etc.) have room - a box exactly matching the pixel
            // size clips them, as text like "Rotating Group Circuit" showed.
            DrawText(g, _display.ConfigSummaryText(), _labelFamily, h * 0.045f, FontStyle.Bold, InkSoft,
                new RectangleF(paneLeft + margin, margin, paneWidth * 0.5f, h * 0.065f),
                StringAlignment.Near, StringAlignment.Near);

            DrawText(g, _display.SessionElapsedText(), _labelFamily, h * 0.035f, FontStyle.Regular, InkFaint,
                new RectangleF(paneLeft + margin, margin + (h * 0.062f), paneWidth * 0.5f, h * 0.045f),
                StringAlignment.Near, StringAlignment.Near);

            // ---- licence status, deliberately unobtrusive but always present.
            // Skipped when fully licensed since the bottom-left "Licensed to"
            // line below already says the same thing - only warnings (trial
            // ending, grace period, etc.) need to show up here as well.
            LicenceStatus licence = Licensing.Current;
            if (licence.State != LicenceState.Licensed)
            {
                Color licenceInk = licence.State == LicenceState.TrialEnding
                    ? Color.FromArgb(230, 255, 235, 140)
                    : Color.FromArgb(115, 255, 255, 255);

                DrawText(g, Licensing.ShortDescription(licence), _labelFamily, h * 0.030f, FontStyle.Regular,
                    licenceInk,
                    new RectangleF(paneLeft + margin, margin + (h * 0.095f), paneWidth * 0.5f, h * 0.040f),
                    StringAlignment.Near, StringAlignment.Near);
            }

            if (IsParallelActive)
            {
                // Every station's own colour, word, countdown and progress bar,
                // laid out as a grid - the one part of the screen that genuinely
                // can't be the existing single countdown, since N stations are
                // each at a different point in their own pattern right now.
                // Bounded well clear of the wall clock/licence text above (which
                // end around 0.17h) and the hint bar below (which starts at
                // 0.92h) - tiles are solid-filled rectangles, so unlike text they
                // would otherwise visibly paint over anything under them.
                string rotationText = _display.ParallelRotationText();
                if (!string.IsNullOrEmpty(rotationText))
                {
                    DrawFitted(g, rotationText, _labelFamily, h * 0.050f, FontStyle.Bold, Ink,
                        new RectangleF(paneLeft + (paneWidth * 0.05f), h * 0.130f, paneWidth * 0.90f, h * 0.055f));
                }

                RectangleF dashboardArea = new RectangleF(paneLeft + margin, h * 0.185f, paneWidth - (margin * 2f), h * 0.705f);
                PaintParallelDashboard(g, dashboardArea);
            }
            else
            {
                // ---- secondary description line - station name, an announcement, or blank
                string secondary = _display.SecondaryDescriptionText();
                if (!string.IsNullOrEmpty(secondary))
                {
                    DrawFitted(g, secondary, _labelFamily, h * 0.075f, FontStyle.Bold, InkSoft,
                        new RectangleF(paneLeft + (paneWidth * 0.05f), h * 0.115f, paneWidth * 0.90f, h * 0.075f));
                }

                // ---- phase label
                DrawFitted(g, _display.PhaseLabelText(), _labelFamily, h * 0.105f, FontStyle.Bold, Ink,
                    new RectangleF(paneLeft + (paneWidth * 0.04f), h * 0.195f, paneWidth * 0.92f, h * 0.105f));

                // ---- the big number
                DrawFitted(g, _display.BigNumberText(), _numberFamily, h * 0.44f, _numberStyle, Ink,
                    new RectangleF(paneLeft + (paneWidth * 0.03f), h * 0.285f, paneWidth * 0.94f, h * 0.475f));

                // ---- round counter
                DrawFitted(g, _display.RoundText(), _labelFamily, h * 0.055f, FontStyle.Bold, InkSoft,
                    new RectangleF(paneLeft + (paneWidth * 0.05f), h * 0.760f, paneWidth * 0.90f, h * 0.065f));

                // ---- what is coming next
                DrawFitted(g, _display.NextUpText(), _labelFamily, h * 0.038f, FontStyle.Regular, InkFaint,
                    new RectangleF(paneLeft + (paneWidth * 0.05f), h * 0.832f, paneWidth * 0.90f, h * 0.050f));

                // ---- progress bar for the current interval
                if (InTimedPhase)
                {
                    float barHeight = Math.Max(4f, h * 0.018f);
                    float barLeft = paneLeft + (paneWidth * 0.06f);
                    float barWidth = paneWidth * 0.88f;
                    float barTop = h * 0.893f;
                    double fraction = _phaseLength > 0 ? remaining / _phaseLength : 0;
                    if (fraction < 0) fraction = 0;
                    if (fraction > 1) fraction = 1;

                    using (SolidBrush track = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
                    {
                        g.FillRectangle(track, barLeft, barTop, barWidth, barHeight);
                    }
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                    {
                        g.FillRectangle(fill, barLeft, barTop, (float)(barWidth * fraction), barHeight);
                    }
                }
            }

            // ---- keyboard hint bar
            bool showHint = DateTime.Now < _hintHideAt || _phase == Phase.Idle || _phase == Phase.Finished || IsPaused;
            if (showHint)
            {
                DrawFitted(g, _display.HintText(), _labelFamily, h * 0.030f, FontStyle.Regular,
                    Color.FromArgb(150, 255, 255, 255),
                    new RectangleF(paneLeft + (paneWidth * 0.03f), h * 0.920f, paneWidth * 0.94f, h * 0.040f));
            }

            // ---- copyright notice, always on screen regardless of licence state
            DrawText(g, Licensing.ShortCopyright, _labelFamily, h * 0.026f, FontStyle.Regular,
                Color.FromArgb(120, 255, 255, 255),
                new RectangleF(paneLeft + (paneWidth * 0.50f), h * 0.960f, (paneWidth * 0.50f) - margin, h * 0.036f),
                StringAlignment.Far, StringAlignment.Near);

            // ---- who this copy is licensed to, bottom left, permanently.
            // Nobody passes software off as their own while somebody else's name
            // is projected on the wall for the whole lesson.
            string licensee = licence.State == LicenceState.Licensed && licence.Licence != null
                ? "Licensed to " + licence.Licence.Name
                : string.Empty;

            DrawText(g, licensee, _labelFamily, h * 0.026f, FontStyle.Regular,
                Color.FromArgb(120, 255, 255, 255),
                new RectangleF(paneLeft + margin, h * 0.960f, paneWidth * 0.48f, h * 0.036f),
                StringAlignment.Near, StringAlignment.Near);

            // ---- update notice
            if (_update != null && _update.IsNewerThanThis)
            {
                DrawText(g, _update.Description + " - press F3", _labelFamily, h * 0.026f,
                    FontStyle.Regular, Color.FromArgb(190, 255, 240, 170),
                    new RectangleF(paneLeft + margin, margin + (h * 0.130f), paneWidth * 0.5f, h * 0.036f),
                    StringAlignment.Near, StringAlignment.Near);
            }

            // ---- link status (linked displays), bottom centre between the licensee and the copyright
            string linkBadge = LinkBadgeText(showHint, out Color linkColour); // timing detail only while the key hints show (H)
            if (linkBadge.Length > 0)
            {
                // Fitted into the space actually left between the two texts either side of it.
                Font small = GetFont(_labelFamily, h * 0.026f, FontStyle.Regular);
                float gap = h * 0.03f;
                float left = paneLeft + margin + (licensee.Length > 0 ? g.MeasureString(licensee, small).Width + gap : 0f);
                float right = paneLeft + paneWidth - margin - g.MeasureString(Licensing.ShortCopyright, small).Width - gap;
                if (right - left > h * 0.2f)
                {
                    DrawFitted(g, linkBadge, _labelFamily, h * 0.026f, FontStyle.Bold, linkColour,
                        new RectangleF(left, h * 0.957f, right - left, h * 0.036f));
                }
            }

            // ---- toast confirmation (e.g. "PROGRAM READY: X" right after Quick Setup) - drawn last, on top of everything
            if (!string.IsNullOrEmpty(_toastMessage) && DateTime.Now < _toastHideAt)
            {
                RectangleF toastArea = new RectangleF(paneLeft + (paneWidth * 0.12f), h * 0.40f, paneWidth * 0.76f, h * 0.12f);
                using (SolidBrush toastBg = new SolidBrush(Color.FromArgb(220, 15, 20, 26)))
                {
                    g.FillRectangle(toastBg, toastArea);
                }
                using (Pen toastBorder = new Pen(Color.FromArgb(220, 60, 200, 120), Math.Max(2f, h * 0.003f)))
                {
                    g.DrawRectangle(toastBorder, toastArea.X, toastArea.Y, toastArea.Width, toastArea.Height);
                }
                DrawFitted(g, _toastMessage, _labelFamily, h * 0.055f, FontStyle.Bold,
                    Color.FromArgb(255, 120, 230, 150), toastArea);
            }
        }

        /// <summary>
        /// The Parallel-mode dashboard: every station's own tile, each with its
        /// own colour, word, countdown and progress bar - the one part of the
        /// screen that genuinely needs new layout, since N different phases can
        /// be on screen at once here in a way Shared and Sequential never allow.
        /// </summary>
        private void PaintParallelDashboard(Graphics g, RectangleF area)
        {
            List<StationPosition> positions = ComputeParallelPositions();
            if (positions.Count == 0 || area.Width < 10 || area.Height < 10) return;

            // A second screen can be set to show only some stations (Link displays), with bigger tiles.
            List<int> shown = new List<int>();
            for (int i = 0; i < positions.Count; i++)
            {
                if (LinkShowsStation(i, positions.Count)) shown.Add(i);
            }
            int n = shown.Count;
            if (n == 0) return;

            // Biases toward more columns than rows, since the display is normally
            // a wide projector/TV screen rather than a square one.
            double aspect = area.Width / Math.Max(1f, area.Height);
            int columns = (int)Math.Ceiling(Math.Sqrt(n * Math.Max(0.3, aspect)));
            columns = Math.Max(1, Math.Min(columns, n));
            int rows = (int)Math.Ceiling(n / (double)columns);

            float gap = Math.Min(area.Width, area.Height) * 0.02f;
            float tileWidth = (area.Width - (gap * (columns + 1))) / columns;
            float tileHeight = (area.Height - (gap * (rows + 1))) / rows;

            for (int i = 0; i < n; i++)
            {
                int col = i % columns;
                int row = i / columns;
                RectangleF tile = new RectangleF(
                    area.X + gap + (col * (tileWidth + gap)),
                    area.Y + gap + (row * (tileHeight + gap)),
                    tileWidth, tileHeight);

                DrawStationTile(g, tile, shown[i], positions[shown[i]]);
            }
        }

        /// <summary>One station's tile: its own colour + final-seconds pulse, name, word, countdown and progress bar.</summary>
        private void DrawStationTile(Graphics g, RectangleF tile, int index, StationPosition position)
        {
            string name = index < _program.Stations.Count ? _program.Stations[index].Name : _program.Name;
            string stationColourText = index < _program.Stations.Count ? _program.Stations[index].Colour : string.Empty;

            Color background;
            string word;
            string note = string.Empty;
            string countdown = string.Empty;
            double fraction = 0;

            if (position.Block == null || position.Finished)
            {
                background = _settings.DoneBg;
                word = "DONE";
            }
            else
            {
                Block block = position.Block.Block;
                Color baseColour = _settings.ColourFor(block.Type, block.Colour);
                background = _display.ApplyPulse(baseColour, position.RemainingSeconds, _phaseClock.IsRunning);
                word = block.Word;
                note = _display.TileNoteFor(index, block);
                countdown = DisplayModel.FormatSeconds(position.RemainingSeconds);
                fraction = block.Seconds > 0 ? position.RemainingSeconds / block.Seconds : 0;
                if (fraction < 0) fraction = 0;
                if (fraction > 1) fraction = 1;
            }

            using (SolidBrush brush = new SolidBrush(background))
            {
                g.FillRectangle(brush, tile);
            }

            float pad = tile.Height * 0.06f;

            // Station name + optional colour swatch, top of the tile.
            RectangleF nameRect = new RectangleF(tile.X + pad, tile.Y + pad, tile.Width - (pad * 2), tile.Height * 0.14f);
            if (!string.IsNullOrEmpty(stationColourText))
            {
                Color swatch = TimerSettings.ParseColour(stationColourText, Color.Transparent);
                if (swatch.A > 0)
                {
                    float swatchSize = nameRect.Height * 0.7f;
                    using (SolidBrush swatchBrush = new SolidBrush(swatch))
                    {
                        g.FillEllipse(swatchBrush, nameRect.X, nameRect.Y + ((nameRect.Height - swatchSize) / 2f), swatchSize, swatchSize);
                    }
                    nameRect = new RectangleF(nameRect.X + swatchSize + (pad * 0.5f), nameRect.Y,
                        nameRect.Width - swatchSize - (pad * 0.5f), nameRect.Height);
                }
            }
            DrawTableCell(g, name, tile.Height * 0.075f, FontStyle.Bold, Ink, nameRect);

            // Word (WORK/RECOVERY/... or DONE), upper-middle.
            DrawFitted(g, word, _labelFamily, tile.Height * 0.10f, FontStyle.Bold, InkSoft,
                new RectangleF(tile.X + pad, tile.Y + (tile.Height * 0.20f), tile.Width - (pad * 2), tile.Height * 0.12f));

            // The station's coaching note for this phase (WORKNOTE / RESTNOTE / move message).
            bool hasNote = !string.IsNullOrEmpty(note);
            if (hasNote)
            {
                DrawCentredWrapped(g, note, tile.Height * 0.060f, FontStyle.Regular, Ink,
                    new RectangleF(tile.X + pad, tile.Y + (tile.Height * 0.32f), tile.Width - (pad * 2), tile.Height * 0.15f));
            }

            if (!string.IsNullOrEmpty(countdown))
            {
                // The countdown number, the bulk of the tile (a little smaller when there is a note above it).
                float numberTop = hasNote ? 0.47f : 0.36f;
                DrawFitted(g, countdown, _numberFamily, tile.Height * (hasNote ? 0.30f : 0.36f), _numberStyle, Ink,
                    new RectangleF(tile.X + pad, tile.Y + (tile.Height * numberTop), tile.Width - (pad * 2), tile.Height * (0.84f - numberTop)));

                // Progress bar, bottom of the tile.
                float barHeight = Math.Max(3f, tile.Height * 0.035f);
                float barTop = tile.Y + tile.Height - pad - barHeight;
                float barLeft = tile.X + pad;
                float barWidth = tile.Width - (pad * 2);

                using (SolidBrush track = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
                {
                    g.FillRectangle(track, barLeft, barTop, barWidth, barHeight);
                }
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    g.FillRectangle(fill, barLeft, barTop, (float)(barWidth * fraction), barHeight);
                }
            }
        }

        /// <summary>
        /// The station list, drawn as a table down the left quarter of the screen.
        /// In Shared Timing every station plays at once, so this is purely
        /// informational and nothing is ever highlighted - the only exception is
        /// the legacy rotating-station-label session (see
        /// UsingLegacyStationRotation), preserved exactly as it worked before
        /// programs existed. In Sequential mode the one station currently
        /// running is highlighted.
        /// </summary>
        private void DrawStationsTable(Graphics g, RectangleF area, float clientHeight)
        {
            List<StationDef> allStations = _program.Stations;
            if (allStations.Count == 0 || area.Width < 10 || area.Height < 10) return;

            int currentAllIndex = -1;
            if (_phase != Phase.Idle && _phase != Phase.Finished)
            {
                if (UsingLegacyStationRotation)
                {
                    Station legacy = LegacyCurrentStation();
                    if (legacy != null) currentAllIndex = _settings.Stations.IndexOf(legacy);
                }
                else if (_program.Mode == ExecutionMode.Sequential)
                {
                    currentAllIndex = _display.ActiveStationIndexForDisplay();
                }
            }

            // Only stations the teacher has chosen to show on screen - a station
            // hidden this way simply never appears here, active or not.
            List<StationDef> stations = new List<StationDef>();
            int currentIndex = -1;
            for (int i = 0; i < allStations.Count; i++)
            {
                if (!allStations[i].ShowInPanel) continue;
                if (i == currentAllIndex) currentIndex = stations.Count;
                stations.Add(allStations[i]);
            }
            if (stations.Count == 0) return;

            using (SolidBrush panel = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                g.FillRectangle(panel, area);
            }

            float rowHeight = area.Height / stations.Count;
            float pad = Math.Max(6f, area.Width * 0.06f);

            for (int i = 0; i < stations.Count; i++)
            {
                float rowTop = area.Y + (i * rowHeight);
                bool current = i == currentIndex;

                if (current)
                {
                    using (SolidBrush highlight = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                    {
                        g.FillRectangle(highlight, area.X + (pad * 0.25f), rowTop + (rowHeight * 0.05f),
                            area.Width - (pad * 0.5f), rowHeight * 0.90f);
                    }
                }
                else if (i > 0)
                {
                    using (Pen rule = new Pen(Color.FromArgb(35, 255, 255, 255)))
                    {
                        g.DrawLine(rule, area.X + (pad * 0.25f), rowTop, area.X + area.Width - (pad * 0.25f), rowTop);
                    }
                }

                StationDef st = stations[i];

                // A per-station accent colour fills the whole row rather than a
                // small swatch, so the station is unmistakable at a glance; the
                // text ink then flips to black or white, whichever reads better
                // against that particular colour, rather than always assuming
                // a dark background.
                Color rowFill = Color.Transparent;
                bool hasCustomColour = !string.IsNullOrEmpty(st.Colour);
                if (hasCustomColour)
                {
                    rowFill = TimerSettings.ParseColour(st.Colour, Color.Transparent);
                }

                Color nameInk, detailInk;
                if (hasCustomColour && rowFill.A > 0)
                {
                    using (SolidBrush rowBrush = new SolidBrush(Color.FromArgb(current ? 235 : 205, rowFill)))
                    {
                        g.FillRectangle(rowBrush, area.X + (pad * 0.15f), rowTop + (rowHeight * 0.05f),
                            area.Width - (pad * 0.3f), rowHeight * 0.90f);
                    }

                    Color readableInk = DisplayModel.ReadableInkFor(rowFill);
                    nameInk = readableInk;
                    detailInk = current
                        ? DisplayModel.Blend(readableInk, readableInk == Color.Black ? Color.White : Color.Black, 0.18)
                        : DisplayModel.Blend(readableInk, readableInk == Color.Black ? Color.White : Color.Black, 0.32);
                }
                else
                {
                    nameInk = current ? Ink : InkSoft;
                    detailInk = current ? InkSoft : InkFaint;
                }

                bool hasWork = !string.IsNullOrEmpty(st.WorkInstruction);
                bool hasRest = !string.IsNullOrEmpty(st.RestInstruction);

                // One detail line normally, unless there is room to spare (few
                // enough stations that each row is tall) and both notes are set,
                // in which case they get a line each rather than being crammed together.
                bool twoDetailLines = hasWork && hasRest && stations.Count <= 4;
                bool hasDetail = hasWork || hasRest;

                // Fractions of the row's height given to the name and to the
                // detail line(s) - sized so the two (or three) pieces of text
                // between them fill the row edge-to-edge instead of leaving a
                // dead band underneath, however many stations are on screen.
                float nameFraction = hasDetail ? (twoDetailLines ? 0.36f : 0.42f) : 0.70f;
                float detailFraction = twoDetailLines ? 0.27f : 0.46f;

                // Sized from the row itself (which already accounts for the
                // station count) rather than a separate density fudge factor,
                // so text always grows to fill the space actually available to it.
                float nameSize = Math.Min(clientHeight * 0.05f, rowHeight * nameFraction * 0.80f);
                float detailSize = Math.Min(clientHeight * 0.036f, rowHeight * detailFraction * 0.78f);

                RectangleF nameRect = new RectangleF(area.X + pad, rowTop + (rowHeight * 0.05f),
                    area.Width - (pad * 1.6f), rowHeight * nameFraction);
                DrawTableCell(g, (i + 1) + ". " + st.Name, nameSize, FontStyle.Bold, nameInk, nameRect);

                float detailTop = rowTop + (rowHeight * (0.05f + nameFraction));

                if (twoDetailLines)
                {
                    RectangleF workRect = new RectangleF(area.X + pad, detailTop, area.Width - (pad * 1.6f), rowHeight * detailFraction);
                    DrawTableCell(g, _settings.WorkLabel.ToUpperInvariant() + ": " + st.WorkInstruction,
                        detailSize, FontStyle.Regular, detailInk, workRect);
                    detailTop += rowHeight * detailFraction;

                    RectangleF restRect = new RectangleF(area.X + pad, detailTop, area.Width - (pad * 1.6f), rowHeight * detailFraction);
                    DrawTableCell(g, _settings.RestLabel.ToUpperInvariant() + ": " + st.RestInstruction,
                        detailSize, FontStyle.Regular, detailInk, restRect);
                }
                else if (hasDetail)
                {
                    // Both notes on one line when space is tight, so a station with
                    // both still shows both rather than one silently winning.
                    string combined = hasWork
                        ? _settings.WorkLabel.ToUpperInvariant() + ": " + st.WorkInstruction
                        : string.Empty;
                    if (hasRest)
                    {
                        combined += (combined.Length > 0 ? "   " : string.Empty)
                            + _settings.RestLabel.ToUpperInvariant() + ": " + st.RestInstruction;
                    }

                    RectangleF detailRect = new RectangleF(area.X + pad, detailTop, area.Width - (pad * 1.6f), rowHeight * detailFraction);
                    DrawTableCell(g, combined, detailSize, FontStyle.Regular, detailInk, detailRect);
                }
            }
        }

        /// <summary>
        /// Left-aligned, wraps within its box. Rather than trusting the caller's
        /// requested size outright (a heuristic based on the row's height, which
        /// has no idea how long the actual text is), this measures the text and
        /// shrinks the font until it actually fits the box - so a long note never
        /// overflows/overlaps its neighbours, and a short one still gets to use
        /// the full size offered.
        /// </summary>
        /// <summary>Centred text that wraps onto a second line (shrinking only if needed) - used for tile notes.</summary>
        private void DrawCentredWrapped(Graphics g, string text, float pixelSize, FontStyle style, Color colour, RectangleF bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width < 4 || bounds.Height < 4) return;

            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisWord;
                format.FormatFlags = StringFormatFlags.LineLimit;

                Font font = FitFont(g, text, _labelFamily, style, pixelSize, bounds, format);
                using (SolidBrush brush = new SolidBrush(colour))
                {
                    g.DrawString(text, font, brush, bounds, format);
                }
            }
        }

        private void DrawTableCell(Graphics g, string text, float pixelSize, FontStyle style, Color colour, RectangleF bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width < 4 || bounds.Height < 4) return;

            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.LineLimit;

                Font font = FitFont(g, text, _labelFamily, style, pixelSize, bounds, format);

                using (SolidBrush brush = new SolidBrush(colour))
                {
                    g.DrawString(text, font, brush, bounds, format);
                }
            }
        }

        /// <summary>
        /// Finds the largest font size, no bigger than <paramref name="maxPixelSize"/>,
        /// whose wrapped text still fits within <paramref name="bounds"/> - a small
        /// binary search over GetFont/MeasureString rather than a fixed guess, so
        /// text of any length ends up as large as it can be without overflowing.
        /// </summary>
        private Font FitFont(Graphics g, string text, string family, FontStyle style, float maxPixelSize, RectangleF bounds, StringFormat format)
        {
            const float minPixelSize = 7f;
            float lo = minPixelSize;
            float hi = Math.Max(minPixelSize, maxPixelSize);
            Font best = GetFont(family, hi, style);

            if (Fits(g, text, best, bounds, format)) return best;

            // Binary search for the largest size that fits, to within half a pixel.
            while (hi - lo > 0.5f)
            {
                float mid = (lo + hi) / 2f;
                Font candidate = GetFont(family, mid, style);
                if (Fits(g, text, candidate, bounds, format))
                {
                    lo = mid;
                    best = candidate;
                }
                else
                {
                    hi = mid;
                }
            }

            return best;
        }

        private static bool Fits(Graphics g, string text, Font font, RectangleF bounds, StringFormat format)
        {
            SizeF measured = g.MeasureString(text, font, new SizeF(bounds.Width, float.MaxValue), format);
            return measured.Height <= bounds.Height + 0.5f;
        }

        // ------------------------------------------------------ text formatting

        // ---------------------------------------------------------- draw helpers

        private void DrawText(Graphics g, string text, string family, float pixelSize, FontStyle style,
            Color colour, RectangleF bounds, StringAlignment horizontal, StringAlignment vertical)
        {
            if (string.IsNullOrEmpty(text)) return;

            using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap))
            {
                format.Alignment = horizontal;
                format.LineAlignment = vertical;
                format.Trimming = StringTrimming.None;

                using (SolidBrush brush = new SolidBrush(colour))
                {
                    g.DrawString(text, GetFont(family, pixelSize, style), brush, bounds, format);
                }
            }
        }

        /// <summary>Centres text and shrinks the font if it would overflow the box.</summary>
        private void DrawFitted(Graphics g, string text, string family, float pixelSize, FontStyle style,
            Color colour, RectangleF bounds)
        {
            if (string.IsNullOrEmpty(text)) return;

            Font font = GetFont(family, pixelSize, style);
            SizeF measured = g.MeasureString(text, font);
            if (measured.Width > bounds.Width && measured.Width > 1f)
            {
                pixelSize = pixelSize * (bounds.Width / measured.Width) * 0.98f;
                font = GetFont(family, pixelSize, style);
            }

            using (StringFormat format = new StringFormat(StringFormatFlags.NoWrap))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.None;

                using (SolidBrush brush = new SolidBrush(colour))
                {
                    g.DrawString(text, font, brush, bounds, format);
                }
            }
        }

        private Font GetFont(string family, float pixelSize, FontStyle style)
        {
            if (pixelSize < 7f) pixelSize = 7f;
            int rounded = (int)Math.Round(pixelSize);

            string key = family + "|" + rounded + "|" + (int)style;
            Font font;
            if (_fontCache.TryGetValue(key, out font)) return font;

            if (_fontCache.Count > 250)
            {
                foreach (Font f in _fontCache.Values) f.Dispose();
                _fontCache.Clear();
            }

            try
            {
                font = new Font(family, rounded, style, GraphicsUnit.Pixel);
            }
            catch
            {
                try
                {
                    font = new Font(family, rounded, FontStyle.Regular, GraphicsUnit.Pixel);
                }
                catch
                {
                    font = new Font(FontFamily.GenericSansSerif, rounded, FontStyle.Bold, GraphicsUnit.Pixel);
                }
            }

            _fontCache[key] = font;
            return font;
        }

        /// <summary>Returns the first installed family that supports the style we need.</summary>
        private static string PickFamily(string[] preferred, FontStyle required)
        {
            foreach (string want in preferred)
            {
                if (FamilySupports(want, required)) return want;
            }

            return preferred.Length > 0 ? preferred[preferred.Length - 1] : "Arial";
        }

        private static bool FamilySupports(string name, FontStyle style)
        {
            try
            {
                using (FontFamily family = new FontFamily(name))
                {
                    return family.IsStyleAvailable(style);
                }
            }
            catch
            {
                return false;   // family is not installed
            }
        }

    }
}
