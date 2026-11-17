using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using GymClock.Link;

namespace GymClock
{
    /// <summary>
    /// Decides every word and colour the clock shows, from the engine's state and the settings.
    /// It draws nothing: the window asks it what to show. That keeps the wording in one place
    /// for any kind of screen, and lets it be tested without one.
    ///
    /// The methods below were moved unchanged from MainForm (build guide task P1.5).
    /// </summary>
    public sealed class DisplayModel
    {
        private readonly SessionEngine _engine;

        public DisplayModel(SessionEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        /// <summary>True on a second screen (linked displays): the main PC has the controls, so the hints say so.</summary>
        public bool IsSecondScreen { get; set; }

        /// <summary>Second screen only: true once the main PC's program has arrived.</summary>
        public bool HasLeaderProgram { get; set; }

        // The engine's state, under the names the moved methods already use.
        private TimerSettings Settings { get { return _engine.Settings; } }
        private Phase _phase { get { return _engine.Phase; } }
        private RunClock _phaseClock { get { return _engine.PhaseClock; } }
        private RunClock _sessionClock { get { return _engine.SessionClock; } }
        private WorkoutProgram _program { get { return _engine.Program; } }
        private IReadOnlyList<RuntimeBlock> _sequence { get { return _engine.Sequence; } }
        private int _index { get { return _engine.Index; } }
        private ParallelRotationPlan _parallelPlan { get { return _engine.ParallelPlan; } }
        private bool IsPaused { get { return _engine.IsPaused; } }
        private bool IsParallelActive { get { return _engine.IsParallelActive; } }
        private bool UsingLegacyStationRotation { get { return _engine.UsingLegacyStationRotation; } }
        private RuntimeBlock CurrentBlock() { return _engine.CurrentBlock(); }
        private RuntimeBlock BlockAt(int index) { return _engine.BlockAt(index); }
        private int NextIndex() { return _engine.NextIndex(); }
        private Station LegacyStationAt(int index) { return _engine.LegacyStationAt(index); }
        private Station LegacyCurrentStation() { return _engine.LegacyCurrentStation(); }
        private double RemainingSeconds() { return _engine.RemainingSeconds(); }

        /// <summary>"ROTATION 2 OF 4  -  ROTATE IN 6:42" (or the move countdown) for the Parallel dashboard header.</summary>
        public string ParallelRotationText()
        {
            if (_parallelPlan == null || _parallelPlan.Rotations <= 1) return string.Empty;
            _parallelPlan.Locate(_phaseClock.Elapsed.TotalSeconds, out int rotation, out bool moving, out double toChange);

            if (moving)
            {
                return string.Format(CultureInfo.InvariantCulture, "MOVE TO THE NEXT STATION  -  ROTATION {0} STARTS IN {1}",
                    rotation + 1, FormatClock(toChange));
            }
            if (rotation >= _parallelPlan.Rotations)
            {
                return string.Format(CultureInfo.InvariantCulture, "ROTATION {0} OF {0}  -  FINISHES IN {1}",
                    _parallelPlan.Rotations, FormatClock(toChange));
            }
            return string.Format(CultureInfo.InvariantCulture, "ROTATION {0} OF {1}  -  ROTATE IN {2}",
                rotation, _parallelPlan.Rotations, FormatClock(toChange));
        }

        /// <summary>
        /// The final-few-seconds flash, factored out so both the single-screen
        /// background and every Parallel tile apply exactly the same effect
        /// instead of two copies of the same sine-wave blend drifting apart.
        /// </summary>
        public Color ApplyPulse(Color background, double remainingSeconds, bool running)
        {
            int secondsLeft = (int)Math.Ceiling(remainingSeconds - 0.0001);
            if (!running || secondsLeft < 1 || secondsLeft > 3) return background;

            double pulse = 0.5 + (0.5 * Math.Sin(_phaseClock.Elapsed.TotalMilliseconds / 90.0));
            return Blend(background, Color.White, 0.10 + (0.20 * pulse));
        }

        /// <summary>The note to show on a Parallel tile: the station's work/rest note, or the block's own message (e.g. the move).</summary>
        public string TileNoteFor(int stationIndex, Block block)
        {
            StationDef station = stationIndex >= 0 && stationIndex < _program.Stations.Count ? _program.Stations[stationIndex] : null;
            if (!string.IsNullOrEmpty(block.Label)) return block.Label;
            if (station == null) return string.Empty;

            switch (block.Type)
            {
                case BlockType.Work: return station.WorkInstruction ?? string.Empty;
                case BlockType.Rest:
                case BlockType.Recovery: return station.RestInstruction ?? string.Empty;
                default: return string.Empty;
            }
        }

        public Color BackgroundColour()
        {
            if (IsPaused) return Settings.PausedBg;

            switch (_phase)
            {
                case Phase.Prep: return Settings.PrepBg;

                case Phase.Active:
                {
                    // No single phase colours the whole screen once N stations are
                    // each showing their own - just a neutral base behind the tiles,
                    // which draw their own colour and pulse individually.
                    if (IsParallelActive) return Settings.IdleBg;

                    RuntimeBlock current = CurrentBlock();
                    return current == null ? Settings.IdleBg : Settings.ColourFor(current.Block.Type, current.Block.Colour);
                }

                case Phase.Finished: return Settings.DoneBg;
                default: return Settings.IdleBg;
            }
        }

        public string CurrentTimeText()
        {
            try
            {
                return DateTime.Now.ToString(Settings.ClockFormat, CultureInfo.CurrentCulture);
            }
            catch
            {
                return DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
            }
        }

        public string ConfigSummaryText()
        {
            string text;

            if (_program.Mode == ExecutionMode.Parallel)
            {
                // Just the name: the stations are the tiles, and the rotation count has its own header line.
                text = _program.Name;
            }
            else if (_program.Mode == ExecutionMode.Sequential && _program.Stations.Count > 0)
            {
                int stationIndex = ActiveStationIndexForDisplay();
                text = stationIndex >= 0
                    ? string.Format(CultureInfo.InvariantCulture, "STATION {0} OF {1}", stationIndex + 1, _program.Stations.Count)
                    : _program.Name;
            }
            else if (_program.Mode == ExecutionMode.Circuit && _program.Stations.Count > 0)
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

            return text + (Settings.SoundEnabled ? string.Empty : "   (MUTED)");
        }

        /// <summary>
        /// A compact "45s WORK / 15s REST x10" summary for the common single-block
        /// shared timeline (what every legacy session and every simple Quick Setup
        /// pattern produces), falling back to the plain itemised Timeline.Summary()
        /// for anything more elaborate a teacher has hand-built.
        /// </summary>
        public string SharedTimelineSummary()
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

        public string SessionElapsedText()
        {
            if (_phase == Phase.Idle) return string.Empty;
            TimeSpan t = _sessionClock.Elapsed;
            return string.Format(CultureInfo.InvariantCulture, "Session {0:00}:{1:00}",
                (int)t.TotalMinutes, t.Seconds);
        }

        /// <summary>The station currently shown as "active" in a real Sequential-mode program, or null.</summary>
        public StationDef ActiveStation()
        {
            int index = ActiveStationIndexForDisplay();
            return (index >= 0 && index < _program.Stations.Count) ? _program.Stations[index] : null;
        }

        /// <summary>
        /// The station to treat as active for a real Sequential-mode program, or
        /// the station whose turn it currently is in Circuit mode: the current
        /// block's own station, or - while playing a between-station block with
        /// none - whichever station comes up next, so the display always points
        /// at where the class is headed. Always -1 for Shared mode (including
        /// the legacy synthesis path, which uses LegacyCurrentStation instead).
        /// </summary>
        public int ActiveStationIndexForDisplay()
        {
            if ((_program.Mode != ExecutionMode.Sequential && _program.Mode != ExecutionMode.Circuit)
                || _program.Stations.Count == 0) return -1;
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
        public bool ShowStationNameAsBigLabel(RuntimeBlock current)
        {
            if (!Settings.ShowStationNameAsDescription || current == null || current.Block.Type != BlockType.Work) return false;
            if (UsingLegacyStationRotation) return LegacyCurrentStation() != null;
            // Circuit mode has every station running at once with a different
            // group on each, so there is no single station exercise that
            // applies to the whole class - only Sequential has one true
            // "current" station worth naming here.
            if (_program.Mode != ExecutionMode.Sequential) return false;
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
        public string SecondaryDescriptionText()
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

        public string PhaseLabelText()
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

        public string BigNumberText()
        {
            if (_phase == Phase.Finished) return FormatClock(_sessionClock.Elapsed.TotalSeconds);

            // Before a Parallel run starts there is no single "first block" - show the whole run length.
            if (_phase == Phase.Idle && _parallelPlan != null) return FormatClock(_parallelPlan.TotalSeconds);
            return FormatSeconds(RemainingSeconds());
        }

        public string RoundText()
        {
            switch (_phase)
            {
                case Phase.Idle:
                    if (_parallelPlan != null && !_program.Continuous)
                    {
                        return string.Format(CultureInfo.InvariantCulture, "{0} STATIONS  -  {1} ROTATION{2} OF {3}  -  {4} MOVE",
                            _engine.ParallelStations.Count, _parallelPlan.Rotations, _parallelPlan.Rotations == 1 ? string.Empty : "S",
                            FormatClock(_parallelPlan.StationSeconds), FormatClock(_parallelPlan.MoveSeconds));
                    }
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

        public string NextUpText()
        {
            if (IsPaused)
            {
                return string.Format(CultureInfo.InvariantCulture, "Paused during {0} - press SPACE to resume",
                    PhaseNameFor(_phase));
            }

            switch (_phase)
            {
                case Phase.Idle:
                    if (IsSecondScreen) return HasLeaderProgram ? "Ready - the session starts from the main PC" : "Waiting for the main PC...";
                    return "Press SPACE to start   -   press S to change the times";

                case Phase.Finished:
                    if (IsSecondScreen) return "Session finished";
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

        public string PhaseNameFor(Phase phase)
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

        public string HintText()
        {
            if (IsSecondScreen)
            {
                return "SECOND SCREEN - the main PC has the controls  -  L link settings  -  M sound on this screen  -  "
                    + "F11 full screen  -  F6 next display  -  F3 licence  -  Q quit";
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("SPACE start/pause  -  R reset  -  A open AI assistant  -  S settings  -  L link displays  -  ");

            if (_program.Mode == ExecutionMode.Parallel)
            {
                sb.Append("← restart all stations  -  ");
            }
            else
            {
                sb.Append("←/→ back/skip  -  ");
            }

            if (_program.Mode != ExecutionMode.Parallel && _program.Stations.Count > 0)
            {
                sb.Append("1-9 jump to station  -  ");
            }

            sb.Append("M mute  -  F11 full screen  -  F6 next display  -  F3 licence  -  Q quit");
            return sb.ToString();
        }

        /// <summary>Whole seconds for short intervals, m:ss once we are past a minute.</summary>
        public static string FormatSeconds(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Ceiling(seconds - 0.0001);
            if (total >= 60) return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
            return total.ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatClock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Round(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
        }

        public static Color Blend(Color from, Color to, double amount)
        {
            if (amount < 0) amount = 0;
            if (amount > 1) amount = 1;

            return Color.FromArgb(255,
                (int)(from.R + ((to.R - from.R) * amount)),
                (int)(from.G + ((to.G - from.G) * amount)),
                (int)(from.B + ((to.B - from.B) * amount)));
        }

        /// <summary>
        /// Picks black or white text - whichever gives better contrast against a
        /// given background colour - using the standard relative-luminance
        /// formula rather than a fixed assumption that station colours are dark.
        /// </summary>
        public static Color ReadableInkFor(Color background)
        {
            double luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
            return luminance > 0.55 ? Color.Black : Color.White;
        }
    }
}
