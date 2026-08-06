using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace GymClock
{
    public enum Phase
    {
        Idle,
        Prep,
        Work,
        Rest,
        Finished
    }

    /// <summary>
    /// The whole display is custom painted so every element scales to the projector
    /// resolution automatically - there are no fixed-size controls to fight with.
    /// </summary>
    public class MainForm : Form
    {
        private TimerSettings _settings;

        private Phase _phase = Phase.Idle;
        private int _round = 1;
        private double _phaseLength;                       // seconds in the current phase
        private int _lastCueSecond = -1;

        private readonly Stopwatch _phaseClock = new Stopwatch();
        private readonly Stopwatch _sessionClock = new Stopwatch();
        private readonly System.Windows.Forms.Timer _ticker = new System.Windows.Forms.Timer();

        private bool _fullScreen;
        private Rectangle _windowedBounds;
        private DateTime _hintHideAt;

        private readonly Dictionary<string, Font> _fontCache = new Dictionary<string, Font>();
        private string _labelFamily;
        private string _numberFamily;
        private FontStyle _numberStyle = FontStyle.Bold;

        private UpdateInfo _update;
        private IntervalPlan _plan;

        private static readonly Color Ink = Color.White;
        private static readonly Color InkSoft = Color.FromArgb(205, 255, 255, 255);
        private static readonly Color InkFaint = Color.FromArgb(140, 255, 255, 255);

        public MainForm()
        {
            _settings = TimerSettings.Load();

            Text = "Gym Interval Clock";
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
                if (e.Button == MouseButtons.Left) TogglePause();
            };

            ApplyAudioSettings();
            RebuildPlan();
            ResetSession();
            ShowHint(14);
            UsageLog.Begin();
        }

        // -------------------------------------------------------------- startup

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetFullScreen(true);
            _ticker.Start();
            StartBackgroundChecks(false);
            OfferRenewalIfDue();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _ticker.Stop();
            _settings.Save();
            UsageLog.End();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------- timing engine

        private bool InTimedPhase
        {
            get { return _phase == Phase.Prep || _phase == Phase.Work || _phase == Phase.Rest; }
        }

        private bool IsPaused
        {
            get { return InTimedPhase && !_phaseClock.IsRunning; }
        }

        private void RebuildPlan()
        {
            _plan = _settings.EffectivePlan();
        }

        /// <summary>The work/rest pair for the round in progress.</summary>
        private PlanRound CurrentRound()
        {
            PlanRound round = _plan == null ? null : _plan.RoundAt(_round);
            return round ?? FallbackRound();
        }

        private PlanRound RoundNumber(int number)
        {
            PlanRound round = _plan == null ? null : _plan.RoundAt(number);
            return round ?? FallbackRound();
        }

        private PlanRound FallbackRound()
        {
            PlanRound round = new PlanRound();
            round.Work = Math.Max(1, _settings.WorkSeconds);
            round.Rest = Math.Max(0, _settings.RestSeconds);
            round.BlockNumber = 1;
            round.PositionInBlock = 1;
            round.BlockLength = 1;
            return round;
        }

        /// <summary>Rounds in the session, or 0 when it repeats until stopped.</summary>
        private int TotalRounds
        {
            get
            {
                if (_settings.Rounds == 0) return 0;
                return _plan == null ? _settings.Rounds : _plan.RoundCount;
            }
        }

        private void ResetSession()
        {
            _phase = Phase.Idle;
            _round = 1;
            _lastCueSecond = -1;
            _phaseClock.Reset();
            _sessionClock.Reset();
            _phaseLength = Math.Max(1, RoundNumber(1).Work);
            Invalidate();
        }

        private void StartSession()
        {
            _round = 1;
            _sessionClock.Restart();
            BeginPhase(_settings.PrepSeconds > 0 ? Phase.Prep : Phase.Work);
        }

        private void BeginPhase(Phase phase)
        {
            _phase = phase;
            _phaseLength = PhaseLength(phase);
            _lastCueSecond = -1;
            _phaseClock.Restart();
            if (!_sessionClock.IsRunning) _sessionClock.Start();

            switch (phase)
            {
                case Phase.Prep: Beeper.CuePrepStart(); break;
                case Phase.Work: Beeper.CueWorkStart(); break;
                case Phase.Rest: Beeper.CueRestStart(); break;
            }

            Invalidate();
        }

        private double PhaseLength(Phase phase)
        {
            switch (phase)
            {
                case Phase.Prep: return Math.Max(1, _settings.PrepSeconds);
                case Phase.Work: return Math.Max(1, CurrentRound().Work);
                case Phase.Rest: return Math.Max(1, CurrentRound().Rest);
                default: return 0;
            }
        }

        private double RemainingSeconds()
        {
            if (_phase == Phase.Idle) return Math.Max(1, RoundNumber(1).Work);
            if (_phase == Phase.Finished) return 0;
            double remaining = _phaseLength - _phaseClock.Elapsed.TotalSeconds;
            return remaining < 0 ? 0 : remaining;
        }

        private void Ticker_Tick(object sender, EventArgs e)
        {
            if (_phaseClock.IsRunning && InTimedPhase)
            {
                double remaining = _phaseLength - _phaseClock.Elapsed.TotalSeconds;

                if (remaining <= 0)
                {
                    Advance();
                }
                else
                {
                    int secondsLeft = (int)Math.Ceiling(remaining - 0.0001);
                    if (secondsLeft >= 1 && secondsLeft <= 3 && secondsLeft != _lastCueSecond)
                    {
                        _lastCueSecond = secondsLeft;
                        Beeper.CueCountdown();
                    }
                }
            }

            Invalidate();
        }

        private void Advance()
        {
            switch (_phase)
            {
                case Phase.Idle:
                case Phase.Finished:
                    StartSession();
                    return;

                case Phase.Prep:
                    BeginPhase(Phase.Work);
                    return;

                case Phase.Work:
                {
                    bool finalRound = TotalRounds > 0 && _round >= TotalRounds;

                    if (finalRound && !_settings.RestAfterFinalRound)
                    {
                        FinishSession();
                        return;
                    }

                    if (CurrentRound().Rest <= 0)
                    {
                        if (finalRound) { FinishSession(); return; }
                        _round++;
                        BeginPhase(Phase.Work);
                        return;
                    }

                    BeginPhase(Phase.Rest);
                    return;
                }

                case Phase.Rest:
                {
                    if (TotalRounds > 0 && _round >= TotalRounds)
                    {
                        FinishSession();
                        return;
                    }
                    _round++;
                    BeginPhase(Phase.Work);
                    return;
                }
            }
        }

        private void GoBack()
        {
            // More than a couple of seconds in, "back" means restart this interval.
            if (InTimedPhase && _phaseClock.Elapsed.TotalSeconds > 2.5)
            {
                BeginPhase(_phase);
                return;
            }

            switch (_phase)
            {
                case Phase.Rest:
                    BeginPhase(Phase.Work);
                    return;

                case Phase.Work:
                    if (_round > 1)
                    {
                        _round--;
                        BeginPhase(CurrentRound().Rest > 0 ? Phase.Rest : Phase.Work);
                    }
                    else if (_settings.PrepSeconds > 0)
                    {
                        BeginPhase(Phase.Prep);
                    }
                    else
                    {
                        BeginPhase(Phase.Work);
                    }
                    return;

                case Phase.Prep:
                    BeginPhase(Phase.Prep);
                    return;

                case Phase.Finished:
                    _round = TotalRounds > 0 ? TotalRounds : _round;
                    BeginPhase(Phase.Work);
                    return;
            }
        }

        private void FinishSession()
        {
            _phase = Phase.Finished;
            _phaseClock.Stop();
            _sessionClock.Stop();
            Beeper.CueFinish();
            ShowHint(20);
            Invalidate();
        }

        private void TogglePause()
        {
            if (_phase == Phase.Idle || _phase == Phase.Finished)
            {
                StartSession();
                return;
            }

            if (_phaseClock.IsRunning)
            {
                _phaseClock.Stop();
                _sessionClock.Stop();
                Beeper.CuePause();
            }
            else
            {
                _phaseClock.Start();
                _sessionClock.Start();
            }

            Invalidate();
        }

        // ------------------------------------------------------------- settings

        private void ApplyAudioSettings()
        {
            Beeper.Enabled = _settings.SoundEnabled;
            Beeper.Volume = _settings.Volume;
        }

        /// <summary>
        /// Applies changed settings immediately, mid-session if need be. If the new
        /// interval is already shorter than the time elapsed we roll straight on.
        /// </summary>
        private void ApplySettingsLive()
        {
            ApplyAudioSettings();
            RebuildPlan();
            TopMost = _settings.AlwaysOnTop;

            if (InTimedPhase)
            {
                _phaseLength = PhaseLength(_phase);
                if (_phaseClock.Elapsed.TotalSeconds >= _phaseLength) Advance();
            }
            else if (_phase == Phase.Idle)
            {
                _phaseLength = Math.Max(1, RoundNumber(1).Work);
            }

            Invalidate();
        }

        private void ShowSettings()
        {
            using (SettingsForm dialog = new SettingsForm(_settings))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _settings = dialog.Result;
                    _settings.Save();
                    ApplySettingsLive();
                    ShowHint(4);
                }
            }
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
                dialog.Update = _update;
                dialog.ShowDialog(this);
            }
            Invalidate();
        }

        private void ApplyPreset(Preset preset)
        {
            // A preset key is the quick way back to an even session, so it clears any
            // custom plan rather than leaving one silently in force.
            _settings.Plan = string.Empty;
            _settings.WorkSeconds = preset.Work;
            _settings.RestSeconds = preset.Rest;
            if (preset.Rounds > 0) _settings.Rounds = preset.Rounds;
            _settings.Save();
            ApplySettingsLive();
            ShowHint(4);
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

            int presetIndex = -1;
            if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) presetIndex = e.KeyCode - Keys.D1;
            else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) presetIndex = e.KeyCode - Keys.NumPad1;

            if (presetIndex >= 0)
            {
                if (presetIndex < _settings.Presets.Count) ApplyPreset(_settings.Presets[presetIndex]);
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
                    ShowSettings();
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
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            Rectangle client = ClientRectangle;
            if (client.Width < 40 || client.Height < 40) return;

            float w = client.Width;
            float h = client.Height;

            double remaining = RemainingSeconds();
            int secondsLeft = (int)Math.Ceiling(remaining - 0.0001);

            // Background: colour alone tells the room which phase they are in.
            Color background = BackgroundColour();
            if (_phaseClock.IsRunning && InTimedPhase && secondsLeft >= 1 && secondsLeft <= 3)
            {
                double pulse = 0.5 + (0.5 * Math.Sin(_phaseClock.Elapsed.TotalMilliseconds / 90.0));
                background = Blend(background, Color.White, 0.10 + (0.20 * pulse));
            }
            using (SolidBrush brush = new SolidBrush(background))
            {
                g.FillRectangle(brush, client);
            }

            float margin = h * 0.035f;

            // ---- wall clock, top right
            if (_settings.ShowClock)
            {
                DrawText(g, CurrentTimeText(), _labelFamily, h * 0.075f, FontStyle.Bold, InkSoft,
                    new RectangleF(w * 0.45f, margin, (w * 0.55f) - margin, h * 0.10f),
                    StringAlignment.Far, StringAlignment.Near);
            }

            // ---- configuration summary + session elapsed, top left
            DrawText(g, ConfigSummaryText(), _labelFamily, h * 0.045f, FontStyle.Bold, InkSoft,
                new RectangleF(margin, margin, w * 0.5f, h * 0.05f),
                StringAlignment.Near, StringAlignment.Near);

            DrawText(g, SessionElapsedText(), _labelFamily, h * 0.035f, FontStyle.Regular, InkFaint,
                new RectangleF(margin, margin + (h * 0.052f), w * 0.5f, h * 0.045f),
                StringAlignment.Near, StringAlignment.Near);

            // ---- licence status, deliberately unobtrusive but always present
            LicenceStatus licence = Licensing.Current;
            Color licenceInk = licence.State == LicenceState.TrialEnding
                ? Color.FromArgb(230, 255, 235, 140)
                : Color.FromArgb(115, 255, 255, 255);

            DrawText(g, Licensing.ShortDescription(licence), _labelFamily, h * 0.030f, FontStyle.Regular,
                licenceInk,
                new RectangleF(margin, margin + (h * 0.095f), w * 0.5f, h * 0.040f),
                StringAlignment.Near, StringAlignment.Near);

            // ---- station / exercise name
            string station = CurrentStationText();
            if (!string.IsNullOrEmpty(station))
            {
                DrawFitted(g, station, _labelFamily, h * 0.075f, FontStyle.Bold, InkSoft,
                    new RectangleF(w * 0.05f, h * 0.115f, w * 0.90f, h * 0.075f));
            }

            // ---- phase label
            DrawFitted(g, PhaseLabelText(), _labelFamily, h * 0.105f, FontStyle.Bold, Ink,
                new RectangleF(w * 0.04f, h * 0.195f, w * 0.92f, h * 0.105f));

            // ---- the big number
            DrawFitted(g, BigNumberText(), _numberFamily, h * 0.44f, _numberStyle, Ink,
                new RectangleF(w * 0.03f, h * 0.285f, w * 0.94f, h * 0.475f));

            // ---- round counter
            DrawFitted(g, RoundText(), _labelFamily, h * 0.055f, FontStyle.Bold, InkSoft,
                new RectangleF(w * 0.05f, h * 0.760f, w * 0.90f, h * 0.065f));

            // ---- what is coming next
            DrawFitted(g, NextUpText(), _labelFamily, h * 0.038f, FontStyle.Regular, InkFaint,
                new RectangleF(w * 0.05f, h * 0.832f, w * 0.90f, h * 0.050f));

            // ---- progress bar for the current interval
            if (InTimedPhase)
            {
                float barHeight = Math.Max(4f, h * 0.018f);
                float barLeft = w * 0.06f;
                float barWidth = w * 0.88f;
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

            // ---- keyboard hint bar
            bool showHint = DateTime.Now < _hintHideAt || _phase == Phase.Idle || _phase == Phase.Finished || IsPaused;
            if (showHint)
            {
                DrawFitted(g, HintText(), _labelFamily, h * 0.030f, FontStyle.Regular,
                    Color.FromArgb(150, 255, 255, 255),
                    new RectangleF(w * 0.03f, h * 0.920f, w * 0.94f, h * 0.040f));
            }

            // ---- copyright notice, always on screen regardless of licence state
            DrawText(g, Licensing.ShortCopyright, _labelFamily, h * 0.026f, FontStyle.Regular,
                Color.FromArgb(120, 255, 255, 255),
                new RectangleF(w * 0.50f, h * 0.960f, (w * 0.50f) - margin, h * 0.036f),
                StringAlignment.Far, StringAlignment.Near);

            // ---- who this copy is licensed to, bottom left, permanently.
            // Nobody passes software off as their own while somebody else's name
            // is projected on the wall for the whole lesson.
            string licensee = licence.State == LicenceState.Licensed && licence.Licence != null
                ? "Licensed to " + licence.Licence.Name
                : string.Empty;

            DrawText(g, licensee, _labelFamily, h * 0.026f, FontStyle.Regular,
                Color.FromArgb(120, 255, 255, 255),
                new RectangleF(margin, h * 0.960f, w * 0.48f, h * 0.036f),
                StringAlignment.Near, StringAlignment.Near);

            // ---- update notice
            if (_update != null && _update.IsNewerThanThis)
            {
                DrawText(g, _update.Description + " - press F3", _labelFamily, h * 0.026f,
                    FontStyle.Regular, Color.FromArgb(190, 255, 240, 170),
                    new RectangleF(margin, margin + (h * 0.130f), w * 0.5f, h * 0.036f),
                    StringAlignment.Near, StringAlignment.Near);
            }
        }

        private Color BackgroundColour()
        {
            if (IsPaused) return _settings.PausedBg;

            switch (_phase)
            {
                case Phase.Prep: return _settings.PrepBg;
                case Phase.Work: return _settings.WorkBg;
                case Phase.Rest: return _settings.RestBg;
                case Phase.Finished: return _settings.DoneBg;
                default: return _settings.IdleBg;
            }
        }

        private string CurrentTimeText()
        {
            try
            {
                return DateTime.Now.ToString(_settings.ClockFormat, CultureInfo.CurrentCulture);
            }
            catch
            {
                return DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
            }
        }

        private string ConfigSummaryText()
        {
            string text;

            if (_plan != null && !_plan.IsUniform)
            {
                // Variable session: show the block structure and how long it runs.
                text = _plan.Summary() + "   ("
                    + IntervalPlan.FormatDuration(_plan.TotalSeconds(_settings.PrepSeconds,
                        _settings.RestAfterFinalRound)) + ")";

                if (_settings.Rounds == 0) text += "  repeating";
            }
            else
            {
                PlanRound first = RoundNumber(1);
                string rounds = TotalRounds > 0
                    ? " x " + TotalRounds.ToString(CultureInfo.InvariantCulture)
                    : " - continuous";

                text = string.Format(CultureInfo.InvariantCulture, "{0}s {1} / {2}s {3}{4}",
                    first.Work, _settings.WorkLabel.ToUpperInvariant(),
                    first.Rest, _settings.RestLabel.ToUpperInvariant(), rounds);
            }

            return text + (_settings.SoundEnabled ? string.Empty : "   (MUTED)");
        }

        private string SessionElapsedText()
        {
            if (_phase == Phase.Idle) return string.Empty;
            TimeSpan t = _sessionClock.Elapsed;
            return string.Format(CultureInfo.InvariantCulture, "Session {0:00}:{1:00}",
                (int)t.TotalMinutes, t.Seconds);
        }

        private string CurrentStationText()
        {
            if (_settings.Stations.Count == 0) return string.Empty;
            if (_phase == Phase.Idle || _phase == Phase.Finished) return string.Empty;

            int index = (_round - 1) % _settings.Stations.Count;
            if (index < 0) index = 0;

            string name = _settings.Stations[index];
            if (_phase == Phase.Prep) return "UP FIRST: " + name.ToUpperInvariant();
            if (_phase == Phase.Rest) return "COMING UP: " + NextStationName().ToUpperInvariant();
            return name.ToUpperInvariant();
        }

        private string NextStationName()
        {
            if (_settings.Stations.Count == 0) return string.Empty;
            int index = _round % _settings.Stations.Count;
            return _settings.Stations[index];
        }

        private string PhaseLabelText()
        {
            if (IsPaused) return "PAUSED";

            switch (_phase)
            {
                case Phase.Prep: return "GET READY";
                case Phase.Work: return _settings.WorkLabel.ToUpperInvariant();
                case Phase.Rest: return _settings.RestLabel.ToUpperInvariant();
                case Phase.Finished: return "DONE";
                default: return "READY";
            }
        }

        private string BigNumberText()
        {
            if (_phase == Phase.Finished) return FormatClock(_sessionClock.Elapsed.TotalSeconds);
            return FormatSeconds(RemainingSeconds());
        }

        private string RoundText()
        {
            switch (_phase)
            {
                case Phase.Idle:
                    return TotalRounds > 0
                        ? string.Format(CultureInfo.InvariantCulture, "{0} ROUNDS READY", TotalRounds)
                        : "CONTINUOUS - NO ROUND LIMIT";

                case Phase.Finished:
                    return string.Format(CultureInfo.InvariantCulture, "SESSION COMPLETE - {0} ROUNDS", _round);

                default:
                {
                    string text = TotalRounds > 0
                        ? string.Format(CultureInfo.InvariantCulture, "ROUND {0} OF {1}", _round, TotalRounds)
                        : string.Format(CultureInfo.InvariantCulture, "ROUND {0}", _round);

                    // On a variable plan, where you are within the block matters as much
                    // as the overall round number.
                    if (_plan != null && _plan.BlockCount > 1)
                    {
                        PlanRound round = CurrentRound();
                        text += string.Format(CultureInfo.InvariantCulture,
                            "   -   BLOCK {0} OF {1}  ({2} of {3})",
                            round.BlockNumber, _plan.BlockCount,
                            round.PositionInBlock, round.BlockLength);
                    }

                    return text;
                }
            }
        }

        private string NextUpText()
        {
            if (IsPaused)
            {
                return string.Format(CultureInfo.InvariantCulture, "Paused during {0} - press SPACE to resume",
                    PhaseNameFor(_phase));
            }

            switch (_phase)
            {
                case Phase.Idle:
                    return "Press SPACE to start   -   press S to change the times";

                case Phase.Finished:
                    return "Press SPACE to run it again   -   R to reset";

                case Phase.Prep:
                    return string.Format(CultureInfo.InvariantCulture, "Next: {0} for {1}s",
                        _settings.WorkLabel.ToUpperInvariant(), RoundNumber(1).Work);

                case Phase.Work:
                {
                    bool finalRound = TotalRounds > 0 && _round >= TotalRounds;
                    if (finalRound && !_settings.RestAfterFinalRound) return "Last effort - finish strong";

                    PlanRound current = CurrentRound();
                    if (current.Rest <= 0)
                    {
                        return string.Format(CultureInfo.InvariantCulture,
                            "Next: straight into {0}s {1}",
                            RoundNumber(_round + 1).Work, _settings.WorkLabel.ToUpperInvariant());
                    }

                    return string.Format(CultureInfo.InvariantCulture, "Next: {0} for {1}s",
                        _settings.RestLabel.ToUpperInvariant(), current.Rest);
                }

                case Phase.Rest:
                {
                    if (TotalRounds > 0 && _round >= TotalRounds) return "Next: finish";

                    PlanRound next = RoundNumber(_round + 1);
                    string text = string.Format(CultureInfo.InvariantCulture,
                        "Next: {0} for {1}s   (round {2})",
                        _settings.WorkLabel.ToUpperInvariant(), next.Work, _round + 1);

                    // Call out a change of pace, since the number is about to jump.
                    if (_plan != null && _plan.BlockCount > 1 && next.BlockNumber != CurrentRound().BlockNumber)
                    {
                        text += "   -   pace changes to " + next.Work + "/" + next.Rest;
                    }

                    return text;
                }
            }

            return string.Empty;
        }

        private string PhaseNameFor(Phase phase)
        {
            switch (phase)
            {
                case Phase.Prep: return "the countdown";
                case Phase.Work: return _settings.WorkLabel.ToUpperInvariant();
                case Phase.Rest: return _settings.RestLabel.ToUpperInvariant();
                default: return "the session";
            }
        }

        private string HintText()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("SPACE start/pause  -  R reset  -  S settings  -  ");
            sb.Append("\u2190/\u2192 back/skip  -  ");

            if (_settings.Presets.Count > 0)
            {
                for (int i = 0; i < _settings.Presets.Count && i < 9; i++)
                {
                    sb.Append(i + 1).Append("=").Append(_settings.Presets[i].Label).Append("  ");
                }
                sb.Append("-  ");
            }

            sb.Append("M mute  -  F11 full screen  -  F6 next display  -  F3 licence  -  Q quit");
            return sb.ToString();
        }

        // ------------------------------------------------------ text formatting

        /// <summary>Whole seconds for short intervals, m:ss once we are past a minute.</summary>
        private static string FormatSeconds(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Ceiling(seconds - 0.0001);
            if (total >= 60) return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
            return total.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatClock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Round(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
        }

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

        private static Color Blend(Color from, Color to, double amount)
        {
            if (amount < 0) amount = 0;
            if (amount > 1) amount = 1;

            return Color.FromArgb(255,
                (int)(from.R + ((to.R - from.R) * amount)),
                (int)(from.G + ((to.G - from.G) * amount)),
                (int)(from.B + ((to.B - from.B) * amount)));
        }
    }
}
