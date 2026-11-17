using System;
using System.Collections.Generic;
using GymClock.Link;

namespace GymClock
{
    /// <summary>
    /// The timing engine: which phase and block the session is in, and the two clocks.
    /// No drawing, no windows, and no sound of its own - it asks an <see cref="ICuePlayer"/>
    /// for cues and raises <see cref="Changed"/> when the screen needs redrawing.
    ///
    /// It walks a flattened list of RuntimeBlocks (WorkoutProgram.BuildRuntimeSequence) one
    /// block at a time - the same approach for Shared Timing (one sequence, no station is ever
    /// "active"), Sequential Stations and Shared Circuit (each station's blocks back to back,
    /// tagged with their station). Parallel is different: every station runs at once, located
    /// by elapsed time (ParallelClock.Locate), so there is no single current block.
    ///
    /// The methods below were moved unchanged from MainForm (build guide task P1.3).
    /// </summary>
    public sealed class SessionEngine
    {
        private readonly ICuePlayer _cues;

        private Phase _phase = Phase.Idle;
        private double _phaseLength;                       // seconds in the current phase
        private int _lastCueSecond = -1;

        // RunClock = a Stopwatch that can also be set to a position (a second screen is put where the main PC is).
        private readonly RunClock _phaseClock = new RunClock();
        private readonly RunClock _sessionClock = new RunClock();

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

        /// <summary>Parallel mode's rotation timing (rotation length, move length, how many), or null in other modes.</summary>
        private ParallelRotationPlan _parallelPlan;

        /// <summary>Last rotation/move state seen by the ticker, so the rotation cues fire once per change.</summary>
        private int _lastRotationKey = -1;

        public SessionEngine(TimerSettings settings, ICuePlayer cues)
        {
            Settings = settings ?? new TimerSettings();
            _cues = cues ?? new SilentCuePlayer();
            RebuildProgram();
            Reset();
        }

        // ---------------------------------------------------------------- state

        /// <summary>The settings the session is running from. Replace them with <see cref="ApplySettings"/>.</summary>
        public TimerSettings Settings { get; private set; }

        public WorkoutProgram Program { get { return _program; } }
        public IReadOnlyList<RuntimeBlock> Sequence { get { return _sequence; } }
        public IReadOnlyList<List<ResolvedBlock>> ParallelStations { get { return _parallelStations; } }
        public ParallelRotationPlan ParallelPlan { get { return _parallelPlan; } }
        public int[] LegacyRound { get { return _legacyRound; } }
        public Phase Phase { get { return _phase; } }
        public int Index { get { return _index; } }
        public double PhaseLength { get { return _phaseLength; } }
        public RunClock PhaseClock { get { return _phaseClock; } }
        public RunClock SessionClock { get { return _sessionClock; } }

        /// <summary>Raised whenever the screen needs redrawing (where MainForm used to call Invalidate).</summary>
        public event Action Changed;

        /// <summary>Asks for the key hints to be shown for a number of seconds (where MainForm used to call ShowHint).</summary>
        public event Action<double> HintRequested;

        private void OnChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        private void RequestHint(double seconds)
        {
            Action<double> handler = HintRequested;
            if (handler != null) handler(seconds);
        }

        /// <summary>
        /// Applies changed settings immediately, mid-session if need be. If the new
        /// interval is already shorter than the time elapsed we roll straight on.
        /// </summary>
        public void ApplySettings(TimerSettings settings)
        {
            if (settings != null) Settings = settings;
            RebuildProgram();

            if (_phase == Phase.Prep)
            {
                _phaseLength = Math.Max(1, Settings.PrepSeconds);
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

            OnChanged();
        }

        /// <summary>
        /// Puts the session at a given point - what a second screen does when the main PC's
        /// state differs from its own (pause, skip, reset, or joining mid-session). Plays the
        /// cue for the new step when the step changed and the session is running.
        /// </summary>
        public void SnapTo(Phase phase, int index, double phaseElapsedSeconds, double sessionElapsedSeconds, bool paused)
        {
            bool parallel = _program.Mode == ExecutionMode.Parallel;
            bool stepChanged = phase != _phase || (phase == Phase.Active && !parallel && index != _index);

            switch (phase)
            {
                case Phase.Idle:
                    if (_phase != Phase.Idle) Reset();
                    return;

                case Phase.Finished:
                    _phase = Phase.Finished;
                    _phaseClock.Stop();
                    _sessionClock.Set(TimeSpan.FromSeconds(sessionElapsedSeconds), false);
                    if (stepChanged)
                    {
                        _cues.Play(Cue.Finish);
                        RequestHint(20);
                    }
                    OnChanged();
                    return;

                case Phase.Prep:
                    _phase = Phase.Prep;
                    _index = -1;
                    _phaseLength = Math.Max(1, Settings.PrepSeconds);
                    break;

                default:
                    _phase = Phase.Active;
                    _index = Math.Max(0, Math.Min(index, _sequence.Count - 1));
                    if (!parallel) _phaseLength = Math.Max(1, CurrentBlock().Block.Seconds);
                    break;
            }

            _phaseClock.Set(TimeSpan.FromSeconds(phaseElapsedSeconds), !paused);
            _sessionClock.Set(TimeSpan.FromSeconds(sessionElapsedSeconds), !paused);

            if (stepChanged)
            {
                _lastCueSecond = -1;
                _lastRotationKey = -1;
                if (!paused)
                {
                    if (_phase == Phase.Prep) _cues.Play(Cue.PrepStart);
                    else if (parallel) _cues.Play(Cue.WorkStart);
                    else PlayCueFor(CurrentBlock().Block.Type);
                }
            }
            OnChanged();
        }

        // ---------------------------------------------------------------- moved from MainForm

        public bool InTimedPhase
        {
            get { return _phase == Phase.Prep || _phase == Phase.Active; }
        }

        public bool IsPaused
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
        public bool UsingLegacyStationRotation
        {
            get
            {
                return !Settings.UseBuiltProgram
                    && Settings.Stations.Count > 0
                    && Settings.HighlightCurrentStation;
            }
        }

        public bool IsParallelActive
        {
            get { return _program.Mode == ExecutionMode.Parallel && _phase == Phase.Active; }
        }

        private void RebuildProgram()
        {
            _program = Settings.EffectiveProgram();
            _sequence = _program.BuildRuntimeSequence();
            if (_sequence.Count == 0)
            {
                _sequence.Add(RuntimeBlock.From(new ResolvedBlock { Block = new Block(BlockType.Work, Math.Max(1, Settings.WorkSeconds)) }, -1));
                if (Settings.RestSeconds > 0)
                {
                    _sequence.Add(RuntimeBlock.From(new ResolvedBlock { Block = new Block(BlockType.Recovery, Settings.RestSeconds) }, -1));
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
            _parallelPlan = _program.Mode == ExecutionMode.Parallel ? _program.GetParallelRotationPlan() : null;
            _lastRotationKey = -1;
        }

        public RuntimeBlock CurrentBlock()
        {
            if (_sequence.Count == 0) return null;
            if (_index < 0 || _index >= _sequence.Count) return _sequence[0];
            return _sequence[_index];
        }

        /// <summary>The block at an arbitrary index, wrapping around for a continuous program and clamping otherwise.</summary>
        public RuntimeBlock BlockAt(int index)
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

        public int NextIndex()
        {
            if (_sequence.Count == 0) return 0;
            return _program.Continuous ? (_index + 1) % _sequence.Count : Math.Min(_index + 1, _sequence.Count - 1);
        }

        public Station LegacyStationAt(int index)
        {
            if (Settings.Stations.Count == 0 || _legacyRound.Length == 0) return null;
            if (index < 0) index = 0;
            if (index >= _legacyRound.Length) index = _legacyRound.Length - 1;

            int round = _legacyRound[index];
            int stationIndex = ((round - 1) % Settings.Stations.Count + Settings.Stations.Count) % Settings.Stations.Count;
            return Settings.Stations[stationIndex];
        }

        public Station LegacyCurrentStation()
        {
            return UsingLegacyStationRotation ? LegacyStationAt(_index) : null;
        }

        public void Reset()
        {
            _phase = Phase.Idle;
            _index = -1;
            _lastCueSecond = -1;
            _phaseClock.Reset();
            _sessionClock.Reset();
            _phaseLength = Math.Max(1, _sequence[0].Block.Seconds);
            OnChanged();
        }

        public void Start()
        {
            _index = -1;
            _sessionClock.Restart();
            if (Settings.PrepSeconds > 0)
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
            _phaseLength = Math.Max(1, Settings.PrepSeconds);
            _lastCueSecond = -1;
            _phaseClock.Restart();
            if (!_sessionClock.IsRunning) _sessionClock.Start();
            _cues.Play(Cue.PrepStart);
            OnChanged();
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
                _cues.Play(Cue.WorkStart);
                OnChanged();
                return;
            }

            RuntimeBlock current = CurrentBlock();
            _phaseLength = Math.Max(1, current.Block.Seconds);
            PlayCueFor(current.Block.Type);
            OnChanged();
        }

        /// <summary>Every Parallel-mode station's current position, located by elapsed time off the one shared _phaseClock.</summary>
        public List<StationPosition> ComputeParallelPositions()
        {
            double elapsed = _phaseClock.Elapsed.TotalSeconds;
            List<StationPosition> positions = new List<StationPosition>(_parallelStations.Count);
            foreach (List<ResolvedBlock> blocks in _parallelStations)
            {
                positions.Add(ParallelClock.Locate(blocks, elapsed, _program.Continuous));
            }
            return positions;
        }

        /// <summary>
        /// Parallel mode is otherwise silent (stations change at different moments), but
        /// the whole room rotates together - so cue that: countdown beeps in the last
        /// three seconds before a move or a new rotation, then the move / start cue.
        /// </summary>
        private void PlayParallelRotationCues()
        {
            if (_parallelPlan == null || _parallelPlan.Rotations <= 1 || _program.Continuous) return;

            _parallelPlan.Locate(_phaseClock.Elapsed.TotalSeconds, out int rotation, out bool moving, out double toChange);
            int key = (rotation * 2) + (moving ? 1 : 0);
            if (_lastRotationKey >= 0 && key != _lastRotationKey)
            {
                if (moving) _cues.Play(Cue.MoveStart); else _cues.Play(Cue.WorkStart);
                _lastCueSecond = -1;
            }
            _lastRotationKey = key;

            bool lastStretch = rotation == _parallelPlan.Rotations && !moving;
            int secondsLeft = (int)Math.Ceiling(toChange - 0.0001);
            if (!lastStretch && secondsLeft >= 1 && secondsLeft <= 3 && secondsLeft != _lastCueSecond)
            {
                _lastCueSecond = secondsLeft;
                _cues.Play(Cue.Countdown);
            }
        }

        private bool AllParallelStationsFinished(List<StationPosition> positions)
        {
            if (_program.Continuous || positions.Count == 0) return false;
            foreach (StationPosition position in positions) if (!position.Finished) return false;
            return true;
        }

        private void PlayCueFor(BlockType type)
        {
            switch (type)
            {
                case BlockType.Work: _cues.Play(Cue.WorkStart); break;
                case BlockType.Move: _cues.Play(Cue.MoveStart); break;
                case BlockType.Prepare:
                case BlockType.Countdown: _cues.Play(Cue.PrepStart); break;
                default: _cues.Play(Cue.RestStart); break;   // Recovery, Rest, WaterBreak, Instruction, Custom
            }
        }

        public double RemainingSeconds()
        {
            if (_phase == Phase.Idle) return Math.Max(1, _sequence[0].Block.Seconds);
            if (_phase == Phase.Finished) return 0;
            double remaining = _phaseLength - _phaseClock.Elapsed.TotalSeconds;
            return remaining < 0 ? 0 : remaining;
        }

        public void Tick()
        {
            if (IsParallelActive)
            {
                // No single block to count down or cue here - each station's own
                // countdown is drawn straight off elapsed time in OnPaint, and
                // per-station cues are deliberately silent (see BeginBlock) so N
                // stations transitioning at different moments don't turn into
                // constant beeping. The only thing this loop watches for is the
                // whole session ending once every station is done.
                if (_phaseClock.IsRunning) PlayParallelRotationCues();
                if (_phaseClock.IsRunning && AllParallelStationsFinished(ComputeParallelPositions())) Finish();
                OnChanged();
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
                        _cues.Play(Cue.Countdown);
                    }
                }
            }

            OnChanged();
        }

        public void Advance()
        {
            if (IsParallelActive)
            {
                // No single "skip to next block" makes sense when every station
                // is at a different point in its own pattern.
                RequestHint(4);
                return;
            }

            switch (_phase)
            {
                case Phase.Idle:
                case Phase.Finished:
                    Start();
                    return;

                case Phase.Prep:
                    _index = 0;
                    BeginBlock();
                    return;

                case Phase.Active:
                {
                    bool last = !_program.Continuous && _index >= _sequence.Count - 1;
                    if (last) { Finish(); return; }

                    _index = _program.Continuous ? (_index + 1) % _sequence.Count : _index + 1;
                    BeginBlock();
                    return;
                }
            }
        }

        public void GoBack()
        {
            if (IsParallelActive)
            {
                // "Back" means restart the whole parallel run from 0:00 - there is
                // no single block to step back to, but restarting everything
                // together is still a useful, well-defined action.
                _phaseClock.Restart();
                _lastCueSecond = -1;
                _lastRotationKey = -1;
                OnChanged();
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
                    else if (Settings.PrepSeconds > 0) BeginPrep();
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

        private void Finish()
        {
            _phase = Phase.Finished;
            _phaseClock.Stop();
            _sessionClock.Stop();
            _cues.Play(Cue.Finish);
            RequestHint(20);
            OnChanged();
        }

        public void TogglePause()
        {
            if (_phase == Phase.Idle || _phase == Phase.Finished)
            {
                Start();
                return;
            }

            if (_phaseClock.IsRunning)
            {
                _phaseClock.Stop();
                _sessionClock.Stop();
                _cues.Play(Cue.Pause);
            }
            else
            {
                _phaseClock.Start();
                _sessionClock.Start();
            }

            OnChanged();
        }

        /// <summary>
        /// Jumps straight to the given station (0-based), for a teacher correcting
        /// a mistake or starting mid-way through a class already spread across the
        /// stations. Wraps forward so it always moves the session on rather than
        /// back. Works against whichever station model is actually in force - the
        /// legacy rotating labels, or a real Sequential-mode program.
        /// </summary>
        public void JumpToStation(int stationIndex)
        {
            if (_program.Mode == ExecutionMode.Parallel)
            {
                // Every station is already running from the start - there's
                // nothing to jump to.
                RequestHint(4);
                return;
            }

            if (UsingLegacyStationRotation)
            {
                int count = Settings.Stations.Count;
                if (stationIndex >= count) { RequestHint(4); return; }

                if (_phase == Phase.Idle || _phase == Phase.Finished) Start();

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
                RequestHint(4);
                return;
            }

            if ((_program.Mode != ExecutionMode.Sequential && _program.Mode != ExecutionMode.Circuit)
                || stationIndex >= _program.Stations.Count)
            {
                RequestHint(4);
                return;
            }

            int found = -1;
            for (int i = 0; i < _sequence.Count; i++)
            {
                if (_sequence[i].StationIndex == stationIndex) { found = i; break; }
            }
            if (found < 0) { RequestHint(4); return; }

            if (_phase == Phase.Idle || _phase == Phase.Finished) Start();
            _index = found;
            BeginBlock();
            RequestHint(4);
        }
    }
}
