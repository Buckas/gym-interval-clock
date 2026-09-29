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
        Active,
        Finished
    }

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
    public class MainForm : Form
    {
        private TimerSettings _settings;

        private Phase _phase = Phase.Idle;
        private double _phaseLength;                       // seconds in the current phase
        private int _lastCueSecond = -1;

        private readonly Stopwatch _phaseClock = new Stopwatch();
        private readonly Stopwatch _sessionClock = new Stopwatch();
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

        private WorkoutProgram _program;
        private List<RuntimeBlock> _sequence = new List<RuntimeBlock>();
        private int _index = -1;                            // position in _sequence of the block in progress

        /// <summary>
        /// One resolved block list per station, only populated in Parallel mode.
        /// Every station is located within its own list by elapsed time (see
        /// ParallelClock.Locate) rather than stepped through with an index, since
        /// stations run independently and _phaseClock is shared by all of them.
        /// </summary>
        private List<List<ResolvedBlock>> _parallelStations = new List<List<ResolvedBlock>>();

        /// <summary>1-based legacy round number for each entry in _sequence - only
        /// meaningful on the legacy synthesis path (see UsingLegacyStationRotation),
        /// where every round contributes exactly one Work block.</summary>
        private int[] _legacyRound = new int[0];

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
            RebuildProgram();
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
            get { return _phase == Phase.Prep || _phase == Phase.Active; }
        }

        private bool IsPaused
        {
            get { return InTimedPhase && !_phaseClock.IsRunning; }
        }

        /// <summary>
        /// True while running the legacy synthesis path (no program has been
        /// built yet) with the classic "linear session" rotating station
        /// highlight turned on. That legacy behaviour - one station label per
        /// round, cycling forever - doesn't map onto either new execution mode
        /// (Shared never highlights a station; Sequential highlights a station
        /// for its own whole timeline, not one round at a time), so it keeps
        /// its own small codepath here rather than being forced into one.
        /// </summary>
        private bool UsingLegacyStationRotation
        {
            get
            {
                return !_settings.UseBuiltProgram
                    && _settings.Stations.Count > 0
                    && _settings.HighlightCurrentStation;
            }
        }

        /// <summary>
        /// True while N independent stations are actually running at once. In
        /// this state _phaseClock is read as elapsed time since every station
        /// started rather than time left in one shared block - see
        /// ComputeParallelPositions and ParallelClock.Locate.
        /// </summary>
        private bool IsParallelActive
        {
            get { return _program.Mode == ExecutionMode.Parallel && _phase == Phase.Active; }
        }

        private void RebuildProgram()
        {
            _program = _settings.EffectiveProgram();
            _sequence = _program.BuildRuntimeSequence();
            if (_sequence.Count == 0)
            {
                _sequence.Add(RuntimeBlock.From(new ResolvedBlock { Block = new Block(BlockType.Work, Math.Max(1, _settings.WorkSeconds)) }, -1));
                if (_settings.RestSeconds > 0)
                {
                    _sequence.Add(RuntimeBlock.From(new ResolvedBlock { Block = new Block(BlockType.Recovery, _settings.RestSeconds) }, -1));
                }
            }

            // Every legacy round contributes exactly one Work block (see
            // TimerSettings.SynthesizeProgram) - counting them back up recovers
            // the round number for UsingLegacyStationRotation without needing to
            // plumb it through the general Program/Timeline model.
            _legacyRound = new int[_sequence.Count];
            int round = 0;
            for (int i = 0; i < _sequence.Count; i++)
            {
                if (_sequence[i].Block.Type == BlockType.Work) round++;
                _legacyRound[i] = round;
            }

            _parallelStations = _program.Mode == ExecutionMode.Parallel
                ? _program.BuildParallelSequences()
                : new List<List<ResolvedBlock>>();
        }

        private RuntimeBlock CurrentBlock()
        {
            if (_sequence.Count == 0) return null;
            if (_index < 0 || _index >= _sequence.Count) return _sequence[0];
            return _sequence[_index];
        }

        /// <summary>The block at an arbitrary index, wrapping around for a continuous program and clamping otherwise.</summary>
        private RuntimeBlock BlockAt(int index)
        {
            if (_sequence.Count == 0) return null;

            if (_program.Continuous)
            {
                int i = index % _sequence.Count;
                if (i < 0) i += _sequence.Count;
                return _sequence[i];
            }

            if (index < 0) index = 0;
            if (index >= _sequence.Count) index = _sequence.Count - 1;
            return _sequence[index];
        }

        private int NextIndex()
        {
            if (_sequence.Count == 0) return 0;
            return _program.Continuous ? (_index + 1) % _sequence.Count : Math.Min(_index + 1, _sequence.Count - 1);
        }

        private Station LegacyStationAt(int index)
        {
            if (_settings.Stations.Count == 0 || _legacyRound.Length == 0) return null;
            if (index < 0) index = 0;
            if (index >= _legacyRound.Length) index = _legacyRound.Length - 1;

            int round = _legacyRound[index];
            int stationIndex = ((round - 1) % _settings.Stations.Count + _settings.Stations.Count) % _settings.Stations.Count;
            return _settings.Stations[stationIndex];
        }

        private Station LegacyCurrentStation()
        {
            return UsingLegacyStationRotation ? LegacyStationAt(_index) : null;
        }

        private void ResetSession()
        {
            _phase = Phase.Idle;
            _index = -1;
            _lastCueSecond = -1;
            _phaseClock.Reset();
            _sessionClock.Reset();
            _phaseLength = Math.Max(1, _sequence[0].Block.Seconds);
            Invalidate();
        }

        private void StartSession()
        {
            _index = -1;
            _sessionClock.Restart();
            if (_settings.PrepSeconds > 0)
            {
                BeginPrep();
            }
            else
            {
                _index = 0;
                BeginBlock();
            }
        }

        private void BeginPrep()
        {
            _phase = Phase.Prep;
            _phaseLength = Math.Max(1, _settings.PrepSeconds);
            _lastCueSecond = -1;
            _phaseClock.Restart();
            if (!_sessionClock.IsRunning) _sessionClock.Start();
            Beeper.CuePrepStart();
            Invalidate();
        }

        private void BeginBlock()
        {
            _phase = Phase.Active;
            _lastCueSecond = -1;
            _phaseClock.Restart();
            if (!_sessionClock.IsRunning) _sessionClock.Start();

            if (_program.Mode == ExecutionMode.Parallel)
            {
                // There is no single "current block" once every station runs
                // independently - _phaseClock becomes elapsed time since they
                // all started, read per-station by ComputeParallelPositions, and
                // is never restarted again by per-block advancement because
                // there is no such thing here (see Ticker_Tick/Advance/GoBack).
                Beeper.CueWorkStart();
                Invalidate();
                return;
            }

            RuntimeBlock current = CurrentBlock();
            _phaseLength = Math.Max(1, current.Block.Seconds);
            PlayCueFor(current.Block.Type);
            Invalidate();
        }

        /// <summary>Every Parallel-mode station's current position, located by elapsed time off the one shared _phaseClock.</summary>
        private List<StationPosition> ComputeParallelPositions()
        {
            double elapsed = _phaseClock.Elapsed.TotalSeconds;
            List<StationPosition> positions = new List<StationPosition>(_parallelStations.Count);
            foreach (List<ResolvedBlock> blocks in _parallelStations)
            {
                positions.Add(ParallelClock.Locate(blocks, elapsed, _program.Continuous));
            }
            return positions;
        }

        private bool AllParallelStationsFinished(List<StationPosition> positions)
        {
            if (_program.Continuous || positions.Count == 0) return false;
            foreach (StationPosition position in positions) if (!position.Finished) return false;
            return true;
        }

        private static void PlayCueFor(BlockType type)
        {
            switch (type)
            {
                case BlockType.Work: Beeper.CueWorkStart(); break;
                case BlockType.Move: Beeper.CueMoveStart(); break;
                case BlockType.Prepare:
                case BlockType.Countdown: Beeper.CuePrepStart(); break;
                default: Beeper.CueRestStart(); break;   // Recovery, Rest, WaterBreak, Instruction, Custom
            }
        }

        private double RemainingSeconds()
        {
            if (_phase == Phase.Idle) return Math.Max(1, _sequence[0].Block.Seconds);
            if (_phase == Phase.Finished) return 0;
            double remaining = _phaseLength - _phaseClock.Elapsed.TotalSeconds;
            return remaining < 0 ? 0 : remaining;
        }

        private void Ticker_Tick(object sender, EventArgs e)
        {
            if (IsParallelActive)
            {
                // No single block to count down or cue here - each station's own
                // countdown is drawn straight off elapsed time in OnPaint, and
                // per-station cues are deliberately silent (see BeginBlock) so N
                // stations transitioning at different moments don't turn into
                // constant beeping. The only thing this loop watches for is the
                // whole session ending once every station is done.
                if (_phaseClock.IsRunning && AllParallelStationsFinished(ComputeParallelPositions())) FinishSession();
                Invalidate();
                return;
            }

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
            if (IsParallelActive)
            {
                // No single "skip to next block" makes sense when every station
                // is at a different point in its own pattern.
                ShowHint(4);
                return;
            }

            switch (_phase)
            {
                case Phase.Idle:
                case Phase.Finished:
                    StartSession();
                    return;

                case Phase.Prep:
                    _index = 0;
                    BeginBlock();
                    return;

                case Phase.Active:
                {
                    bool last = !_program.Continuous && _index >= _sequence.Count - 1;
                    if (last) { FinishSession(); return; }

                    _index = _program.Continuous ? (_index + 1) % _sequence.Count : _index + 1;
                    BeginBlock();
                    return;
                }
            }
        }

        private void GoBack()
        {
            if (IsParallelActive)
            {
                // "Back" means restart the whole parallel run from 0:00 - there is
                // no single block to step back to, but restarting everything
                // together is still a useful, well-defined action.
                _phaseClock.Restart();
                _lastCueSecond = -1;
                Invalidate();
                return;
            }

            // More than a couple of seconds in, "back" means restart this interval.
            if (InTimedPhase && _phaseClock.Elapsed.TotalSeconds > 2.5)
            {
                if (_phase == Phase.Prep) BeginPrep(); else BeginBlock();
                return;
            }

            switch (_phase)
            {
                case Phase.Active:
                    if (_index > 0) { _index--; BeginBlock(); }
                    else if (_settings.PrepSeconds > 0) BeginPrep();
                    else BeginBlock();
                    return;

                case Phase.Prep:
                    BeginPrep();
                    return;

                case Phase.Finished:
                    _index = Math.Max(0, _sequence.Count - 1);
                    BeginBlock();
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
            RebuildProgram();
            TopMost = _settings.AlwaysOnTop;

            if (_phase == Phase.Prep)
            {
                _phaseLength = Math.Max(1, _settings.PrepSeconds);
                if (_phaseClock.Elapsed.TotalSeconds >= _phaseLength) Advance();
            }
            else if (IsParallelActive)
            {
                // Nothing to clamp - _phaseClock just keeps counting elapsed time
                // and every station's position is recomputed fresh each tick.
            }
            else if (_phase == Phase.Active)
            {
                if (_index >= _sequence.Count) _index = _sequence.Count - 1;
                _phaseLength = Math.Max(1, CurrentBlock().Block.Seconds);
                if (_phaseClock.Elapsed.TotalSeconds >= _phaseLength) Advance();
            }
            else if (_phase == Phase.Idle)
            {
                _phaseLength = Math.Max(1, _sequence[0].Block.Seconds);
            }

            Invalidate();
        }

        /// <summary>
        /// The single combined window for general settings and building a
        /// program. "B" jumps straight into its Quick Start Wizard for the
        /// fastest path to a basic program; "S" just opens it at rest.
        /// </summary>
        private void ShowProgramEditor(bool launchWizard)
        {
            using (ProgramEditorForm dialog = new ProgramEditorForm(_settings))
            {
                if (launchWizard) dialog.Shown += delegate { dialog.LaunchQuickWizard(); };

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    bool justActivatedProgram = dialog.Result.UseBuiltProgram
                        && (!_settings.UseBuiltProgram || _settings.ProgramScript != dialog.Result.ProgramScript);

                    _settings = dialog.Result;
                    _settings.Save();
                    ApplySettingsLive();
                    ResetSession();

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

        private void ApplyPreset(Preset preset)
        {
            // A preset key is the quick way back to a simple session, so it switches
            // off any built program rather than leaving one silently in force - but
            // doesn't erase it, so it's still there if you switch back to it later.
            _settings.Plan = string.Empty;
            _settings.UseBuiltProgram = false;
            _settings.WorkSeconds = preset.Work;
            _settings.RestSeconds = preset.Rest;
            if (preset.Rounds > 0) _settings.Rounds = preset.Rounds;
            _settings.Save();
            ApplySettingsLive();
            ShowHint(4);
        }

        /// <summary>
        /// Jumps straight to the given station (0-based), for a teacher correcting
        /// a mistake or starting mid-way through a class already spread across the
        /// stations. Wraps forward so it always moves the session on rather than
        /// back. Works against whichever station model is actually in force - the
        /// legacy rotating labels, or a real Sequential-mode program.
        /// </summary>
        private void JumpToStation(int stationIndex)
        {
            if (_program.Mode == ExecutionMode.Parallel)
            {
                // Every station is already running from the start - there's
                // nothing to jump to.
                ShowHint(4);
                return;
            }

            if (UsingLegacyStationRotation)
            {
                int count = _settings.Stations.Count;
                if (stationIndex >= count) { ShowHint(4); return; }

                if (_phase == Phase.Idle || _phase == Phase.Finished) StartSession();

                int fromIndex = Math.Max(0, Math.Min(_index, _legacyRound.Length - 1));
                int currentStation = (_legacyRound[fromIndex] - 1) % count;
                if (currentStation < 0) currentStation += count;

                int delta = stationIndex - currentStation;
                if (delta < 0) delta += count;

                int target = _index;
                int steps = delta;
                while (steps > 0 && target < _sequence.Count - 1)
                {
                    target++;
                    if (_sequence[target].Block.Type == BlockType.Work) steps--;
                }

                _index = target;
                BeginBlock();
                ShowHint(4);
                return;
            }

            if (_program.Mode != ExecutionMode.Sequential || stationIndex >= _program.Stations.Count)
            {
                ShowHint(4);
                return;
            }

            int found = -1;
            for (int i = 0; i < _sequence.Count; i++)
            {
                if (_sequence[i].StationIndex == stationIndex) { found = i; break; }
            }
            if (found < 0) { ShowHint(4); return; }

            if (_phase == Phase.Idle || _phase == Phase.Finished) StartSession();
            _index = found;
            BeginBlock();
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
                if (e.Control) JumpToStation(presetIndex);
                else if (presetIndex < _settings.Presets.Count) ApplyPreset(_settings.Presets[presetIndex]);
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

                case Keys.B:
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
            Color background = BackgroundColour();
            if (!IsParallelActive && InTimedPhase) background = ApplyPulse(background, remaining, _phaseClock.IsRunning);
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
                DrawText(g, CurrentTimeText(), _labelFamily, h * 0.075f, FontStyle.Bold, InkSoft,
                    new RectangleF(paneLeft + (paneWidth * 0.45f), margin, (paneWidth * 0.55f) - margin, h * 0.10f),
                    StringAlignment.Far, StringAlignment.Near);
            }

            // ---- configuration summary + session elapsed, top left of the pane
            DrawText(g, ConfigSummaryText(), _labelFamily, h * 0.045f, FontStyle.Bold, InkSoft,
                new RectangleF(paneLeft + margin, margin, paneWidth * 0.5f, h * 0.05f),
                StringAlignment.Near, StringAlignment.Near);

            DrawText(g, SessionElapsedText(), _labelFamily, h * 0.035f, FontStyle.Regular, InkFaint,
                new RectangleF(paneLeft + margin, margin + (h * 0.052f), paneWidth * 0.5f, h * 0.045f),
                StringAlignment.Near, StringAlignment.Near);

            // ---- licence status, deliberately unobtrusive but always present
            LicenceStatus licence = Licensing.Current;
            Color licenceInk = licence.State == LicenceState.TrialEnding
                ? Color.FromArgb(230, 255, 235, 140)
                : Color.FromArgb(115, 255, 255, 255);

            DrawText(g, Licensing.ShortDescription(licence), _labelFamily, h * 0.030f, FontStyle.Regular,
                licenceInk,
                new RectangleF(paneLeft + margin, margin + (h * 0.095f), paneWidth * 0.5f, h * 0.040f),
                StringAlignment.Near, StringAlignment.Near);

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
                RectangleF dashboardArea = new RectangleF(paneLeft + margin, h * 0.185f, paneWidth - (margin * 2f), h * 0.705f);
                PaintParallelDashboard(g, dashboardArea);
            }
            else
            {
                // ---- secondary description line - station name, an announcement, or blank
                string secondary = SecondaryDescriptionText();
                if (!string.IsNullOrEmpty(secondary))
                {
                    DrawFitted(g, secondary, _labelFamily, h * 0.075f, FontStyle.Bold, InkSoft,
                        new RectangleF(paneLeft + (paneWidth * 0.05f), h * 0.115f, paneWidth * 0.90f, h * 0.075f));
                }

                // ---- phase label
                DrawFitted(g, PhaseLabelText(), _labelFamily, h * 0.105f, FontStyle.Bold, Ink,
                    new RectangleF(paneLeft + (paneWidth * 0.04f), h * 0.195f, paneWidth * 0.92f, h * 0.105f));

                // ---- the big number
                DrawFitted(g, BigNumberText(), _numberFamily, h * 0.44f, _numberStyle, Ink,
                    new RectangleF(paneLeft + (paneWidth * 0.03f), h * 0.285f, paneWidth * 0.94f, h * 0.475f));

                // ---- round counter
                DrawFitted(g, RoundText(), _labelFamily, h * 0.055f, FontStyle.Bold, InkSoft,
                    new RectangleF(paneLeft + (paneWidth * 0.05f), h * 0.760f, paneWidth * 0.90f, h * 0.065f));

                // ---- what is coming next
                DrawFitted(g, NextUpText(), _labelFamily, h * 0.038f, FontStyle.Regular, InkFaint,
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
                DrawFitted(g, HintText(), _labelFamily, h * 0.030f, FontStyle.Regular,
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
            int n = positions.Count;
            if (n == 0 || area.Width < 10 || area.Height < 10) return;

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

                DrawStationTile(g, tile, i, positions[i]);
            }
        }

        /// <summary>One station's tile: its own colour + final-seconds pulse, name, word, countdown and progress bar.</summary>
        private void DrawStationTile(Graphics g, RectangleF tile, int index, StationPosition position)
        {
            string name = index < _program.Stations.Count ? _program.Stations[index].Name : _program.Name;
            string stationColourText = index < _program.Stations.Count ? _program.Stations[index].Colour : string.Empty;

            Color background;
            string word;
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
                background = ApplyPulse(baseColour, position.RemainingSeconds, _phaseClock.IsRunning);
                word = block.Word;
                countdown = FormatSeconds(position.RemainingSeconds);
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
                new RectangleF(tile.X + pad, tile.Y + (tile.Height * 0.22f), tile.Width - (pad * 2), tile.Height * 0.14f));

            if (!string.IsNullOrEmpty(countdown))
            {
                // The countdown number, the bulk of the tile.
                DrawFitted(g, countdown, _numberFamily, tile.Height * 0.36f, _numberStyle, Ink,
                    new RectangleF(tile.X + pad, tile.Y + (tile.Height * 0.36f), tile.Width - (pad * 2), tile.Height * 0.46f));

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
        /// The final-few-seconds flash, factored out so both the single-screen
        /// background and every Parallel tile apply exactly the same effect
        /// instead of two copies of the same sine-wave blend drifting apart.
        /// </summary>
        private Color ApplyPulse(Color background, double remainingSeconds, bool running)
        {
            int secondsLeft = (int)Math.Ceiling(remainingSeconds - 0.0001);
            if (!running || secondsLeft < 1 || secondsLeft > 3) return background;

            double pulse = 0.5 + (0.5 * Math.Sin(_phaseClock.Elapsed.TotalMilliseconds / 90.0));
            return Blend(background, Color.White, 0.10 + (0.20 * pulse));
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
                    currentAllIndex = ActiveStationIndexForDisplay();
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

            // Font-size caps are expressed against clientHeight so a handful of
            // stations still look substantial, but that same fixed cap would
            // badly overflow a short row once several more stations are packed
            // in - so it's scaled down as the list grows, always leaving each
            // row's actual height (via rowHeight * factor below) as the final say.
            float density = Math.Min(1f, 3f / stations.Count);

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
                Color nameInk = current ? Ink : InkSoft;
                Color detailInk = current ? InkSoft : InkFaint;

                bool hasWork = !string.IsNullOrEmpty(st.WorkInstruction);
                bool hasRest = !string.IsNullOrEmpty(st.RestInstruction);

                // One detail line normally, unless there is room to spare (few
                // enough stations that each row is tall) and both notes are set,
                // in which case they get a line each rather than being crammed together.
                bool twoDetailLines = hasWork && hasRest && stations.Count <= 4;

                float nameFraction = twoDetailLines ? 0.34f : 0.40f;
                float detailFraction = twoDetailLines ? 0.24f : 0.34f;

                float nameSize = Math.Min(clientHeight * 0.03f * density, rowHeight * nameFraction * 0.85f);
                float detailSize = Math.Min(clientHeight * 0.021f * density, rowHeight * detailFraction * 0.75f);

                // A per-station accent colour, drawn as a small swatch rather than
                // tinting the text itself, so it stays legible over any background.
                if (!string.IsNullOrEmpty(st.Colour))
                {
                    Color swatch = TimerSettings.ParseColour(st.Colour, Color.Transparent);
                    if (swatch.A > 0)
                    {
                        float swatchSize = Math.Min(pad * 0.8f, rowHeight * nameFraction * 0.65f);
                        float swatchY = rowTop + (rowHeight * 0.05f) + ((rowHeight * nameFraction - swatchSize) / 2f);
                        using (SolidBrush swatchBrush = new SolidBrush(swatch))
                        {
                            g.FillEllipse(swatchBrush, area.X + (pad * 0.15f), swatchY, swatchSize, swatchSize);
                        }
                    }
                }

                RectangleF nameRect = new RectangleF(area.X + pad, rowTop + (rowHeight * 0.05f),
                    area.Width - (pad * 1.6f), rowHeight * nameFraction);
                DrawTableCell(g, (i + 1) + ". " + st.Name, nameSize, FontStyle.Bold, nameInk, nameRect);

                float detailTop = rowTop + (rowHeight * (0.05f + nameFraction + 0.03f));

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
                else if (hasWork || hasRest)
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

        /// <summary>Left-aligned, wraps within its box and trims with an ellipsis rather than overflowing.</summary>
        private void DrawTableCell(Graphics g, string text, float pixelSize, FontStyle style, Color colour, RectangleF bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width < 4 || bounds.Height < 4) return;

            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.LineLimit;

                using (SolidBrush brush = new SolidBrush(colour))
                {
                    g.DrawString(text, GetFont(_labelFamily, pixelSize, style), brush, bounds, format);
                }
            }
        }

        private Color BackgroundColour()
        {
            if (IsPaused) return _settings.PausedBg;

            switch (_phase)
            {
                case Phase.Prep: return _settings.PrepBg;

                case Phase.Active:
                {
                    // No single phase colours the whole screen once N stations are
                    // each showing their own - just a neutral base behind the tiles,
                    // which draw their own colour and pulse individually.
                    if (IsParallelActive) return _settings.IdleBg;

                    RuntimeBlock current = CurrentBlock();
                    return current == null ? _settings.IdleBg : _settings.ColourFor(current.Block.Type, current.Block.Colour);
                }

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

            if (_program.Mode == ExecutionMode.Parallel)
            {
                int count = _parallelStations.Count;
                text = _program.Name + "   -   Parallel (" + count + (count == 1 ? " station" : " stations") + ")";
            }
            else if (_program.Mode == ExecutionMode.Sequential && _program.Stations.Count > 0)
            {
                int stationIndex = ActiveStationIndexForDisplay();
                text = stationIndex >= 0
                    ? string.Format(CultureInfo.InvariantCulture, "STATION {0} OF {1}", stationIndex + 1, _program.Stations.Count)
                    : _program.Name;
            }
            else
            {
                text = SharedTimelineSummary() + "   (" + IntervalPlan.FormatDuration(_program.SharedTimeline.TotalSeconds()) + ")";
                if (_program.Continuous) text += "  repeating";
            }

            return text + (_settings.SoundEnabled ? string.Empty : "   (MUTED)");
        }

        /// <summary>
        /// A compact "45s WORK / 15s REST x10" summary for the common single-block
        /// shared timeline (what every legacy session and every simple Quick Setup
        /// pattern produces), falling back to the plain itemised Timeline.Summary()
        /// for anything more elaborate a teacher has hand-built.
        /// </summary>
        private string SharedTimelineSummary()
        {
            Timeline timeline = _program.SharedTimeline;

            if (timeline.Items.Count == 1 && timeline.Items[0].Group != null)
            {
                RepeatGroup group = timeline.Items[0].Group;

                if (group.Blocks.Count == 1)
                {
                    Block only = group.Blocks[0];
                    return string.Format(CultureInfo.InvariantCulture, "{0}s {1} x {2}", only.Seconds, only.Word, group.Count);
                }

                if (group.Blocks.Count == 2)
                {
                    Block a = group.Blocks[0];
                    Block b = group.Blocks[1];
                    return string.Format(CultureInfo.InvariantCulture, "{0}s {1} / {2}s {3} x {4}",
                        a.Seconds, a.Word, b.Seconds, b.Word, group.Count);
                }
            }

            return timeline.Summary();
        }

        private string SessionElapsedText()
        {
            if (_phase == Phase.Idle) return string.Empty;
            TimeSpan t = _sessionClock.Elapsed;
            return string.Format(CultureInfo.InvariantCulture, "Session {0:00}:{1:00}",
                (int)t.TotalMinutes, t.Seconds);
        }

        /// <summary>The station currently shown as "active" in a real Sequential-mode program, or null.</summary>
        private StationDef ActiveStation()
        {
            int index = ActiveStationIndexForDisplay();
            return (index >= 0 && index < _program.Stations.Count) ? _program.Stations[index] : null;
        }

        /// <summary>
        /// The station to treat as active for a real Sequential-mode program:
        /// the current block's own station, or - while playing a between-station
        /// block with none - whichever station comes up next, so the display
        /// always points at where the class is headed. Always -1 for Shared
        /// mode (including the legacy synthesis path, which uses
        /// LegacyCurrentStation instead).
        /// </summary>
        private int ActiveStationIndexForDisplay()
        {
            if (_program.Mode != ExecutionMode.Sequential || _program.Stations.Count == 0) return -1;
            if (_phase == Phase.Prep) return 0;
            if (_phase != Phase.Active) return -1;

            RuntimeBlock current = CurrentBlock();
            if (current == null) return -1;
            if (current.StationIndex >= 0) return current.StationIndex;

            for (int i = _index + 1; i < _sequence.Count; i++)
            {
                if (_sequence[i].StationIndex >= 0) return _sequence[i].StationIndex;
            }
            return _program.Stations.Count - 1;
        }

        /// <summary>
        /// True only when a station name is a sensible thing to show as THE big
        /// description for a Work block: the option is on, and either the legacy
        /// rotating-station session or a real Sequential-mode program actually
        /// has one active right now.
        /// </summary>
        private bool ShowStationNameAsBigLabel(RuntimeBlock current)
        {
            if (!_settings.ShowStationNameAsDescription || current == null || current.Block.Type != BlockType.Work) return false;
            if (UsingLegacyStationRotation) return LegacyCurrentStation() != null;
            return ActiveStation() != null;
        }

        /// <summary>
        /// The smaller banner line above the big word. A block's own Announcement
        /// always wins (e.g. a Move block's "Move to the next station" message).
        /// Otherwise: the legacy rotating station label with its familiar
        /// UP FIRST/COMING UP wording, or - for a real Sequential-mode program -
        /// the plain active station name, or nothing at all in Shared Timing,
        /// which never names a single station.
        /// </summary>
        private string SecondaryDescriptionText()
        {
            if (_phase == Phase.Idle || _phase == Phase.Finished) return string.Empty;

            if (_phase == Phase.Prep)
            {
                if (UsingLegacyStationRotation)
                {
                    Station first = LegacyStationAt(0);
                    return first == null ? string.Empty : "UP FIRST: " + first.Name.ToUpperInvariant();
                }

                if (_program.Mode == ExecutionMode.Sequential)
                {
                    StationDef first = ActiveStation();
                    return first == null ? string.Empty : first.Name.ToUpperInvariant();
                }

                return string.Empty;
            }

            RuntimeBlock current = CurrentBlock();
            if (current == null) return string.Empty;

            if (!string.IsNullOrEmpty(current.Block.Announcement)) return current.Block.Announcement.ToUpperInvariant();

            if (UsingLegacyStationRotation)
            {
                bool work = current.Block.Type == BlockType.Work;
                if (ShowStationNameAsBigLabel(current)) return current.Block.Word;

                if (work)
                {
                    Station station = LegacyCurrentStation();
                    return station == null ? string.Empty : station.Name.ToUpperInvariant();
                }

                Station next = LegacyStationAt(NextIndex());
                return next == null ? string.Empty : "COMING UP: " + next.Name.ToUpperInvariant();
            }

            if (_program.Mode != ExecutionMode.Sequential) return string.Empty;

            if (ShowStationNameAsBigLabel(current)) return current.Block.Word;

            StationDef active = ActiveStation();
            return active == null ? string.Empty : active.Name.ToUpperInvariant();
        }

        private string PhaseLabelText()
        {
            if (IsPaused) return "PAUSED";

            switch (_phase)
            {
                case Phase.Prep: return "GET READY";
                case Phase.Finished: return "DONE";

                case Phase.Active:
                {
                    RuntimeBlock current = CurrentBlock();
                    if (current == null) return "READY";

                    if (ShowStationNameAsBigLabel(current))
                    {
                        string name = UsingLegacyStationRotation
                            ? LegacyCurrentStation().Name
                            : (ActiveStation() != null ? ActiveStation().Name : string.Empty);
                        if (!string.IsNullOrEmpty(name)) return name.ToUpperInvariant();
                    }

                    return current.Block.Word;
                }

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
                    return _program.Continuous
                        ? "CONTINUOUS - NO ROUND LIMIT"
                        : string.Format(CultureInfo.InvariantCulture, "{0} STEPS READY", _sequence.Count);

                case Phase.Finished:
                    return "SESSION COMPLETE";

                default:
                {
                    RuntimeBlock current = CurrentBlock();
                    if (current == null || current.GroupCount == 0)
                    {
                        return _program.Continuous
                            ? string.Empty
                            : string.Format(CultureInfo.InvariantCulture, "STEP {0} OF {1}", _index + 1, _sequence.Count);
                    }

                    string word = _program.Mode == ExecutionMode.Sequential ? "REP" : "ROUND";
                    string text = string.Format(CultureInfo.InvariantCulture, "{0} {1} OF {2}",
                        word, current.RepeatIndex, current.RepeatTotal);

                    if (current.GroupCount > 1)
                    {
                        text += string.Format(CultureInfo.InvariantCulture,
                            "   -   GROUP {0} OF {1}  ({2} of {3})",
                            current.GroupNumber, current.GroupCount, current.PositionInGroup, current.GroupLength);
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
                {
                    RuntimeBlock first = _sequence.Count > 0 ? _sequence[0] : null;
                    return first == null ? string.Empty : string.Format(CultureInfo.InvariantCulture,
                        "Next: {0} for {1}s", first.Block.Word, first.Block.Seconds);
                }

                case Phase.Active:
                {
                    bool last = !_program.Continuous && _index >= _sequence.Count - 1;
                    if (last) return "Last effort - finish strong";

                    RuntimeBlock current = CurrentBlock();
                    RuntimeBlock next = BlockAt(_index + 1);
                    if (next == null) return "Next: finish";

                    string text = string.Format(CultureInfo.InvariantCulture, "Next: {0} for {1}s",
                        next.Block.Word, next.Block.Seconds);

                    if (current != null && next.GroupNumber > 0 && next.GroupNumber != current.GroupNumber)
                    {
                        text += "   -   pace changes";
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
                case Phase.Active:
                {
                    RuntimeBlock current = CurrentBlock();
                    return current == null ? "the session" : current.Block.Word;
                }
                default: return "the session";
            }
        }

        private string HintText()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("SPACE start/pause  -  R reset  -  B build a program  -  S settings  -  ");

            if (_program.Mode == ExecutionMode.Parallel)
            {
                sb.Append("← restart all stations  -  ");
            }
            else
            {
                sb.Append("←/→ back/skip  -  ");
            }

            if (_settings.Presets.Count > 0)
            {
                for (int i = 0; i < _settings.Presets.Count && i < 9; i++)
                {
                    sb.Append(i + 1).Append("=").Append(_settings.Presets[i].Label).Append("  ");
                }
                sb.Append("-  ");
            }

            if (_program.Mode != ExecutionMode.Parallel && _program.Stations.Count > 0)
            {
                sb.Append("CTRL+1-9 jump to station  -  ");
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
