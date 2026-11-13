using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GymClock.AI
{
    /// <summary>
    /// Builds a new program by asking the teacher a few plain questions (how the session
    /// runs, the stations, the timing, the move time, a name) and then writing the script
    /// itself - so the result always validates and matches what was asked. The local AI is
    /// only used, optionally, to suggest exercise names when the teacher asks for them.
    ///
    /// Timing answers use whiteboard notation, e.g. "40/20 x 8", "40 sec on / 20 sec off x 8",
    /// "1 min run / 1 min walk x 4", "tabata", "pyramid 20-30-40 / 20 x 2". Stations can be
    /// given with their own timing, one per line: "Bike: 40 sec high intensity / 20 sec spin recovery x 8".
    /// </summary>
    public sealed class GuidedProgramBuilder
    {
        public enum Step { Mode, Stations, Timing, Move, Name, Confirm, Done }

        /// <summary>Optional exercise-name suggester (e.g. the local AI): (request, count) -> names.</summary>
        private readonly Func<string, int, CancellationToken, Task<IReadOnlyList<string>>> _suggestNames;

        /// <summary>Optional coaching-cue writer (e.g. the local AI) for stations the exercise library doesn't know.</summary>
        private readonly Func<string, CancellationToken, Task<string>> _suggestCue;

        /// <summary>True once the summary has been shown: an answered question goes straight back to it.</summary>
        private bool _editing;

        private readonly string _originalRequest;

        /// <summary>Exercises just suggested in the chat, usable as the stations ("use these").</summary>
        private readonly IReadOnlyList<string> _recentIdeas;
        private ExecutionMode? _mode;
        private readonly List<StationPlan> _stations = new List<StationPlan>();
        private Timeline _sharedTiming;
        private string _sharedWorkNote;
        private string _sharedRestNote;
        private int? _moveSeconds;
        private string _name;

        public Step CurrentStep { get; private set; } = Step.Mode;

        /// <summary>The finished script once the teacher confirms (CurrentStep == Done).</summary>
        public string Script { get; private set; }

        private sealed class StationPlan
        {
            public string Name;
            public Timeline Timing;     // null = use the shared timing
            public string WorkNote;
            public string RestNote;
            public string Cue;          // coaching tip shown during work (library, AI, or the teacher's own)
        }

        public GuidedProgramBuilder(string originalRequest,
            Func<string, int, CancellationToken, Task<IReadOnlyList<string>>> suggestNames = null,
            IReadOnlyList<string> recentIdeas = null,
            Func<string, CancellationToken, Task<string>> suggestCue = null)
        {
            _originalRequest = (originalRequest ?? string.Empty).Trim();
            _suggestNames = suggestNames;
            _suggestCue = suggestCue;
            _recentIdeas = recentIdeas ?? Array.Empty<string>();

            // Pick up anything the first message already settled.
            ExecutionMode? hinted = ModeFromWords(_originalRequest);
            if (hinted.HasValue) _mode = hinted;
            Match named = Regex.Match(_originalRequest, "(?:called|named)\\s+[\"“]?([^\"”\\n]{2,60})[\"”]?", RegexOptions.IgnoreCase);
            if (named.Success) _name = named.Groups[1].Value.Trim().TrimEnd('.');
        }

        // ------------------------------------------------------------------ intent

        private static readonly Regex CreateVerb = new Regex(
            @"\b(create|make|build|design|generate|set\s+up|plan|write|new|want|need|give\s+me|help\s+me)\b", RegexOptions.IgnoreCase);
        private static readonly Regex ProgramNoun = new Regex(
            @"\b(program|programme|workout|session|circuit|class|interval|tabata|hiit|pyramid|stations?)\b", RegexOptions.IgnoreCase);

        /// <summary>True for "help me create a new program" style requests (and any create request when nothing is loaded).</summary>
        public static bool IsCreationRequest(string request, bool hasLoadedProgram)
        {
            string text = request ?? string.Empty;
            if (!CreateVerb.IsMatch(text) || !ProgramNoun.IsMatch(text)) return false;
            if (!hasLoadedProgram) return true;
            return Regex.IsMatch(text, @"\b(new|from\s+scratch|start\s+(over|again)|another|different)\b", RegexOptions.IgnoreCase);
        }

        // ------------------------------------------------------------------ conversation

        /// <summary>The first question (skipping any the opening request already answered).</summary>
        public string Start()
        {
            CurrentStep = _mode.HasValue ? Step.Stations : Step.Mode;
            string intro = "Let's build it together - I'll ask a few quick questions." + Environment.NewLine + Environment.NewLine;
            return intro + Question(CurrentStep);
        }

        /// <summary>Handles the teacher's reply and returns the next question, a correction, or the final message.</summary>
        public async Task<string> AnswerAsync(string reply, CancellationToken cancellationToken = default)
        {
            string text = (reply ?? string.Empty).Trim();

            if (Regex.IsMatch(text, @"^(cancel|stop|quit)$", RegexOptions.IgnoreCase))
            {
                CurrentStep = Step.Done;
                Script = null;
                return "OK, cancelled. Nothing has been changed.";
            }

            switch (CurrentStep)
            {
                case Step.Mode:
                {
                    ExecutionMode? mode = ModeFromChoice(text);
                    if (!mode.HasValue) return "Please reply with 1, 2, 3 or 4." + Environment.NewLine + Environment.NewLine + Question(Step.Mode);
                    _mode = mode;
                    if (_editing && _stations.Count > 0)
                    {
                        // Changing the mode keeps the stations; per-station timing only exists in Sequential/Parallel.
                        if (_sharedTiming == null && _stations.Any(st => st.Timing == null)) return Advance(Step.Timing);
                        return Advance(Next(Step.Confirm));
                    }
                    return Advance(Step.Stations);
                }

                case Step.Stations:
                {
                    string error = await ReadStationsAsync(text, cancellationToken).ConfigureAwait(false);
                    if (error != null) return error + Environment.NewLine + Environment.NewLine + Question(Step.Stations);

                    // Every station already came with its own timing (e.g. a pasted whiteboard) - no need to ask again.
                    bool timed = _stations.Count > 0 && _stations.All(s => s.Timing != null);
                    if (_editing && _sharedTiming != null) timed = true;
                    return Advance(timed ? Next(NextAfterTiming()) : Step.Timing);
                }

                case Step.Timing:
                {
                    string error = ReadTiming(text);
                    if (error != null) return error + Environment.NewLine + Environment.NewLine + Question(Step.Timing);
                    return Advance(Next(NextAfterTiming()));
                }

                case Step.Move:
                {
                    int? seconds = ParseMove(text);
                    if (!seconds.HasValue) return "I didn't catch a time - reply like 30, 1 min, or 0 for no move." + Environment.NewLine + Environment.NewLine + Question(Step.Move);
                    _moveSeconds = seconds.Value;
                    return Advance(Next(_name == null ? Step.Name : Step.Confirm));
                }

                case Step.Name:
                {
                    _name = Regex.IsMatch(text, @"^(skip|none|-|no)$", RegexOptions.IgnoreCase) || text.Length == 0
                        ? DefaultName()
                        : text.Trim('"', '\'', ' ');
                    return Advance(Step.Confirm);
                }

                case Step.Confirm:
                {
                    if (Regex.IsMatch(text, @"^(y|yes|ok|go|create|create it|looks good)$", RegexOptions.IgnoreCase))
                    {
                        Script = BuildScript();
                        CurrentStep = Step.Done;
                        return "Done - the program is in the Proposed Script box." + Environment.NewLine
                            + "Click Apply to Editor to open it in the program editor (you can fine-tune it there and Save), "
                            + "or Save As to keep a copy as a file.";
                    }

                    string change = await ApplyChangeAsync(text, cancellationToken).ConfigureAwait(false);
                    if (change != null) return change;

                    return "Reply Y to create it, or say what to change: mode, stations, timing, move, name or tips."
                        + Environment.NewLine + Environment.NewLine + Summary();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// A change asked for at the summary. The new value can come in the same message ("change the
        /// stations to: Bike, Rower", "timing 40/20 x 6", "move 30", "tip for Bike: stay seated");
        /// otherwise that one question is asked and the summary comes straight back - the other answers are kept.
        /// </summary>
        private async Task<string> ApplyChangeAsync(string text, CancellationToken cancellationToken)
        {
            // Coaching tips: "tip for Bike: stay seated", "tip 2: ...", "no tips".
            if (Regex.IsMatch(text, @"^(no|remove|clear|without)\s+(the\s+)?(tips?|cues?|notes?)$", RegexOptions.IgnoreCase))
            {
                foreach (StationPlan st in _stations) { st.Cue = null; st.WorkNote = null; }
                _sharedWorkNote = null;
                return "Tips removed." + Environment.NewLine + Environment.NewLine + Question(Step.Confirm);
            }
            Match tip = Regex.Match(text, @"^(?:change\s+(?:the\s+)?)?(?:tip|cue|note|coaching(?:\s+tip)?)s?\s+(?:for\s+|at\s+)?(?:station\s+)?(.+?)\s*(?::|=|\bto\b|\s-\s)\s*(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (tip.Success)
            {
                StationPlan target = FindStation(tip.Groups[1].Value);
                if (target == null) return "I couldn't find a station called \"" + tip.Groups[1].Value.Trim() + "\". The stations are: "
                    + string.Join(", ", _stations.Select((st, i) => (i + 1) + ") " + st.Name)) + ".";
                target.Cue = tip.Groups[2].Value.Trim().Trim('"');
                target.WorkNote = null;
                return "Tip updated." + Environment.NewLine + Environment.NewLine + Question(Step.Confirm);
            }

            Match stations = Regex.Match(text, @"\bstations?\b", RegexOptions.IgnoreCase);
            if (stations.Success)
            {
                string list = ValueAfter(text, stations, @"(?:to\s+be|to|are|should\s+be|with|=|:)");
                _editing = true;
                if (list.Length == 0) return Advance(Step.Stations);

                string error = await ReadStationsAsync(list, cancellationToken).ConfigureAwait(false);
                if (error != null) return error + Environment.NewLine + Environment.NewLine + Advance(Step.Stations);
                if (_sharedTiming == null && _stations.Any(st => st.Timing == null)) return Advance(Step.Timing);
                return Advance(Next(Step.Confirm));
            }

            Match timing = Regex.Match(text, @"\b(timing|times?|pattern|intervals?)\b", RegexOptions.IgnoreCase);
            if (timing.Success)
            {
                _editing = true;
                string pattern = ValueAfter(text, timing, @"(?:to\s+be|to|is|should\s+be|=|:)");
                ClearTiming();
                if (pattern.Length > 0 && LooksLikeTiming(pattern))
                {
                    string error = ReadTiming(pattern);
                    if (error == null) return Advance(Next(Step.Confirm));
                    return error + Environment.NewLine + Environment.NewLine + Advance(Step.Timing);
                }
                return Advance(Step.Timing);
            }

            Match move = Regex.Match(text, @"\b(move|rotation|rotate|changeover|transition)\b", RegexOptions.IgnoreCase);
            if (move.Success)
            {
                _editing = true;
                int? seconds = ParseMove(ValueAfter(text, move, @"(?:time\s+)?(?:to\s+be|to|is|=|:)"));
                if (seconds.HasValue) { _moveSeconds = seconds.Value; return Advance(Step.Confirm); }
                return Advance(Step.Move);
            }

            Match name = Regex.Match(text, @"\b(name|call\s+it|called|title)\b", RegexOptions.IgnoreCase);
            if (name.Success)
            {
                _editing = true;
                string value = ValueAfter(text, name, @"(?:to\s+be|to|is|=|:)").Trim('"', '\'', ' ');
                if (value.Length > 0) { _name = value; return Advance(Step.Confirm); }
                return Advance(Step.Name);
            }

            if (Regex.IsMatch(text, @"\bmode\b", RegexOptions.IgnoreCase))
            {
                _editing = true;
                return Advance(Step.Mode);
            }
            if (Regex.IsMatch(text, @"^(n|no|restart|start again|start over)$", RegexOptions.IgnoreCase))
            {
                _editing = false;
                _mode = null; _stations.Clear(); ClearTiming(); _moveSeconds = null;
                CurrentStep = Step.Mode;
                return Question(Step.Mode);
            }
            return null;
        }

        /// <summary>What follows the keyword: "change stations to be:\n1. Bike..." -> "1. Bike...".</summary>
        private static string ValueAfter(string text, Match keyword, string joiner)
        {
            string rest = text.Substring(keyword.Index + keyword.Length);
            rest = Regex.Replace(rest, @"^\s*(?:" + joiner + @")?\s*:?", string.Empty, RegexOptions.IgnoreCase);
            return rest.Trim();
        }

        private StationPlan FindStation(string reference)
        {
            string value = (reference ?? string.Empty).Trim().Trim('"');
            Match number = Regex.Match(value, @"^(\d{1,2})$");
            if (number.Success)
            {
                int index = int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                return index >= 0 && index < _stations.Count ? _stations[index] : null;
            }
            string clean = CleanStationName(value);
            if (clean.Length == 0) return null;
            return _stations.FirstOrDefault(st => string.Equals(st.Name, clean, StringComparison.OrdinalIgnoreCase))
                ?? _stations.FirstOrDefault(st => st.Name.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>While changing one thing from the summary, go back to the summary once nothing is missing.</summary>
        private Step Next(Step normal)
        {
            if (!_editing) return normal;
            bool needsMove = _mode != ExecutionMode.Shared && _stations.Count >= 2 && !_moveSeconds.HasValue;
            if (needsMove) return Step.Move;
            if (_name == null) return Step.Name;
            return Step.Confirm;
        }

        private Step NextAfterTiming()
        {
            if (_mode == ExecutionMode.Shared) return _name == null ? Step.Name : Step.Confirm;
            if (_stations.Count < 2) return _name == null ? Step.Name : Step.Confirm;
            return Step.Move;
        }

        private string Advance(Step next)
        {
            CurrentStep = next;
            if (next == Step.Confirm) _editing = true;
            return Question(next);
        }

        private void ClearTiming()
        {
            _sharedTiming = null;
            _sharedWorkNote = _sharedRestNote = null;
            foreach (StationPlan s in _stations) { s.Timing = null; }
        }

        // ------------------------------------------------------------------ questions

        public static readonly string ModeQuestionText = AiProgramGenerationCoordinator.ModeQuestion;

        private string Question(Step step)
        {
            switch (step)
            {
                case Step.Mode:
                    return ModeQuestionText;

                case Step.Stations:
                    return ModeLabel() + Environment.NewLine
                        + "What are the stations? List them one per line or separated by commas"
                        + (_mode == ExecutionMode.Shared ? ", or reply NONE." : ".") + Environment.NewLine
                        + "  e.g. Bike, Treadmill, Skipping, Floor" + Environment.NewLine
                        + "Or reply with a number (e.g. 6) and I'll suggest exercises."
                        + (_recentIdeas.Count > 0 ? Environment.NewLine + "Or reply USE THESE for the " + _recentIdeas.Count + " exercises I just suggested." : string.Empty)
                        + (_mode == ExecutionMode.Sequential || _mode == ExecutionMode.Parallel
                            ? Environment.NewLine + "You can also give each station its own timing, e.g.  Bike: 40/20 x 8"
                            : string.Empty);

                case Step.Timing:
                    if (_mode == ExecutionMode.Sequential || _mode == ExecutionMode.Parallel)
                    {
                        return "What's the timing? One pattern for every station, or one line per station:" + Environment.NewLine
                            + "  40/20 x 8                (40s work, 20s rest, 8 rounds)" + Environment.NewLine
                            + "  Treadmill: 1 min run / 1 min walk x 4" + Environment.NewLine
                            + "  tabata   or   pyramid 20-30-40 / 20 x 2";
                    }
                    return "What's the timing" + (_mode == ExecutionMode.Circuit ? " at each station" : string.Empty) + "?" + Environment.NewLine
                        + "  40/20 x 8                (40s work, 20s rest, 8 rounds)" + Environment.NewLine
                        + "  1 min on / 30 sec off x 3" + Environment.NewLine
                        + "  tabata   or   pyramid 20-30-40 / 20 x 2";

                case Step.Move:
                    return _mode == ExecutionMode.Parallel
                        ? "How long is the move between rotations? (e.g. 30, 1 min)"
                        : "How long is the move to the next station? (e.g. 30, 1 min, or 0 for none)";

                case Step.Name:
                    return "What should the program be called? (or reply SKIP for \"" + DefaultName() + "\")";

                case Step.Confirm:
                    return Summary() + Environment.NewLine + Environment.NewLine
                        + "Reply Y to create it, or say what to change: mode, stations, timing, move, name or tips."
                        + Environment.NewLine + "  e.g.  timing 40/20 x 6    or    tip for 2: drive through the heel";
            }
            return string.Empty;
        }

        private string ModeLabel()
        {
            switch (_mode)
            {
                case ExecutionMode.Shared: return "Fixed stations, one clock.";
                case ExecutionMode.Sequential: return "Whole class together, station by station.";
                case ExecutionMode.Parallel: return "Groups rotate, each station with its own clock.";
                case ExecutionMode.Circuit: return "Groups rotate, one clock.";
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------ answers

        private async Task<string> ReadStationsAsync(string text, CancellationToken cancellationToken)
        {
            _stations.Clear();

            if (_mode == ExecutionMode.Shared && Regex.IsMatch(text, @"^(none|no|skip|0)$", RegexOptions.IgnoreCase)) return null;

            // "use these" - the exercises just suggested in the chat become the stations.
            if (_recentIdeas.Count > 0 && Regex.IsMatch(text, @"^(use\s+)?(these|those|them|the\s+list|that\s+list|the\s+suggestions|the\s+exercises)\b", RegexOptions.IgnoreCase))
            {
                foreach (string idea in _recentIdeas) _stations.Add(new StationPlan { Name = idea });
                await FillCuesAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            Match count = Regex.Match(text, @"^\s*(\d{1,2})\s*(stations?)?\s*$", RegexOptions.IgnoreCase);
            if (count.Success)
            {
                int n = int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture);
                if (n < 1 || n > 20) return "Please choose between 1 and 20 stations.";
                IReadOnlyList<string> names = await SuggestNamesAsync(n, cancellationToken).ConfigureAwait(false);
                foreach (string name in names) _stations.Add(new StationPlan { Name = name });
                await FillCuesAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            List<string> lines = text.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            // "Change the stations to be:" / "Stations:" above a list is an instruction, not a station.
            if (lines.Count > 1 && Regex.IsMatch(lines[0], @"(:|\b(be|are|to))$", RegexOptions.IgnoreCase) && !LooksLikeTiming(lines[0])) lines.RemoveAt(0);
            if (lines.Count == 1 && !LooksLikeTiming(lines[0])) lines = lines[0].Split(',', ';').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            foreach (string line in lines)
            {
                SplitNameAndTiming(line, out string name, out string timing);
                if (string.IsNullOrWhiteSpace(name)) continue;
                StationPlan plan = new StationPlan { Name = name };

                if (!string.IsNullOrEmpty(timing) && (_mode == ExecutionMode.Sequential || _mode == ExecutionMode.Parallel))
                {
                    if (TryParsePattern(timing, out Timeline t, out string workNote, out string restNote))
                    {
                        plan.Timing = t; plan.WorkNote = workNote; plan.RestNote = restNote;
                    }
                }
                _stations.Add(plan);
            }

            if (_stations.Count == 0) return "I couldn't find any station names.";
            if (_stations.Count > 20) return "That's more than 20 stations - please list up to 20.";
            await FillCuesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// A coaching tip for every station: the exercise library's cue when the name matches one
        /// ("box step ups" -> Step-Ups), otherwise one from the local AI if it is running.
        /// </summary>
        private async Task FillCuesAsync(CancellationToken cancellationToken)
        {
            foreach (StationPlan station in _stations)
            {
                if (!string.IsNullOrWhiteSpace(station.Cue)) continue;
                Exercise known = ExerciseLibrary.FindByName(station.Name);
                if (known != null && !string.IsNullOrWhiteSpace(known.Cue)) { station.Cue = known.Cue; continue; }

                if (_suggestCue == null) continue;
                try
                {
                    string cue = await _suggestCue(station.Name, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(cue)) station.Cue = cue.Trim();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { /* no tip for this one - the teacher can add it */ }
            }
        }

        private string ReadTiming(string text)
        {
            List<string> lines = text.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            bool perStationAllowed = _mode == ExecutionMode.Sequential || _mode == ExecutionMode.Parallel;

            // "Name: pattern" lines for individual stations (Sequential / Parallel).
            if (perStationAllowed && lines.Count > 1 || (perStationAllowed && lines.Count == 1 && MatchStation(lines[0]) != null))
            {
                foreach (string line in lines)
                {
                    StationPlan station = MatchStation(line);
                    SplitNameAndTiming(line, out _, out string timing);
                    string pattern = station != null ? timing : line;
                    if (!TryParsePattern(pattern, out Timeline t, out string w, out string r))
                    {
                        return "I couldn't read the timing \"" + line + "\".";
                    }
                    if (station != null) { station.Timing = t; station.WorkNote = w; station.RestNote = r; }
                    else { _sharedTiming = t; _sharedWorkNote = w; _sharedRestNote = r; }
                }

                if (_sharedTiming == null && _stations.Any(s => s.Timing == null))
                {
                    return "Some stations still have no timing: " + string.Join(", ", _stations.Where(s => s.Timing == null).Select(s => s.Name))
                        + ". Add a line for each, or one line without a station name for all the rest.";
                }
                return null;
            }

            if (!TryParsePattern(text, out Timeline shared, out string workNote, out string restNote))
            {
                return "I couldn't read that timing.";
            }
            _sharedTiming = shared;
            _sharedWorkNote = workNote;
            _sharedRestNote = restNote;
            return null;
        }

        private StationPlan MatchStation(string line)
        {
            SplitNameAndTiming(line, out string name, out string timing);
            if (string.IsNullOrEmpty(timing)) return null;
            return _stations.FirstOrDefault(s => string.Equals(CleanStationName(s.Name), CleanStationName(name), StringComparison.OrdinalIgnoreCase));
        }

        private async Task<IReadOnlyList<string>> SuggestNamesAsync(int count, CancellationToken cancellationToken)
        {
            List<string> names = new List<string>();

            // The exercise library first: instant, fits the request ("cardio focused", "no equipment"),
            // only uses equipment the gym has, and every pick comes with a coaching cue.
            ExerciseQuery query = ExerciseLibrary.Parse(_originalRequest);
            query.Count = count;
            foreach (Exercise e in ExerciseLibrary.Pick(query, _recentIdeas, new Random(), out _))
            {
                if (names.Count >= count) break;
                if (!names.Contains(e.Name, StringComparer.OrdinalIgnoreCase)) names.Add(e.Name);
            }

            if (names.Count < count && _suggestNames != null)
            {
                try
                {
                    IReadOnlyList<string> suggested = await _suggestNames(_originalRequest, count, cancellationToken).ConfigureAwait(false);
                    foreach (string s in suggested ?? Array.Empty<string>())
                    {
                        string clean = CleanStationName(s);
                        if (clean.Length > 1 && !names.Contains(clean, StringComparer.OrdinalIgnoreCase)) names.Add(clean);
                        if (names.Count == count) break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* fall back to the built-in list */ }
            }

            foreach (string fallback in FallbackExercises)
            {
                if (names.Count >= count) break;
                if (!names.Contains(fallback, StringComparer.OrdinalIgnoreCase)) names.Add(fallback);
            }
            return names;
        }

        private static readonly string[] FallbackExercises =
        {
            "Jumping Jacks", "Push Ups", "High Knees", "Bodyweight Squats", "Mountain Climbers", "Lunges",
            "Burpees", "Plank", "Skipping", "Step-Ups", "Shuttle Runs", "Glute Bridges",
            "Star Jumps", "Wall Sit", "Tuck Jumps", "Russian Twists", "Bear Crawl", "Squat Jumps", "Dead Bugs", "Calf Raises"
        };

        // ------------------------------------------------------------------ parsing helpers

        private static readonly Regex DurationRegex = new Regex(
            @"(?<![\w.])(\d+(?:\.\d+)?)(?::(\d{2}))?\s*(minutes|minute|mins|min|m|seconds|second|secs|sec|s)?(?![\w])(?!\s*%)",
            RegexOptions.IgnoreCase);

        private static readonly Regex RoundsRegex = new Regex(
            @"(?:[x×*]\s*(\d{1,3})\b(?!\s*(?:sec|s\b|min|m\b)))|(?:\b(\d{1,3})\s*(?:rounds?|sets?|times|reps? of)\b)",
            RegexOptions.IgnoreCase);

        private static bool LooksLikeTiming(string text)
        {
            return Regex.IsMatch(text ?? string.Empty, @"\d") || Regex.IsMatch(text ?? string.Empty, @"\b(tabata|pyramid)\b", RegexOptions.IgnoreCase);
        }

        /// <summary>"Bike station -> 40 sec on / 20 off x 8" => ("Bike", "40 sec on / 20 off x 8").</summary>
        public static void SplitNameAndTiming(string line, out string name, out string timing)
        {
            string text = Regex.Replace((line ?? string.Empty).Trim(), @"^\s*(\d{1,2}[.)]|[-*•])\s*", string.Empty);
            Match sep = Regex.Match(text, @"\s*(->|→|=>|:|\s-\s|\s–\s|\()\s*");
            if (sep.Success && sep.Index > 0 && LooksLikeTiming(text.Substring(sep.Index)))
            {
                name = CleanStationName(text.Substring(0, sep.Index));
                timing = text.Substring(sep.Index + sep.Length).Trim().TrimEnd(')');
                return;
            }

            if (LooksLikeTiming(text) && Regex.IsMatch(text, @"^\D*\d") && !Regex.IsMatch(text, @"^[A-Za-z][A-Za-z '&+/-]*$"))
            {
                // A bare pattern, or "Name 40/20 x 8" where the name ends at the first number.
                Match firstDigit = Regex.Match(text, @"\d|\b(tabata|pyramid)\b", RegexOptions.IgnoreCase);
                name = CleanStationName(text.Substring(0, firstDigit.Index));
                timing = text.Substring(firstDigit.Index).Trim();
                return;
            }

            name = CleanStationName(text);
            timing = string.Empty;
        }

        public static string CleanStationName(string name)
        {
            string value = Regex.Replace(name ?? string.Empty, @"\bstation\b", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\s+", " ").Trim().Trim('"', '\'', '-', ':', '.', ',');
            if (value.Length == 0) return string.Empty;
            // Whiteboard capitals ("BIKE") or all lower case ("box step ups") -> "Box Step Ups"; keep mixed case as written.
            if (value == value.ToUpperInvariant() || value == value.ToLowerInvariant())
                value = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
            return value;
        }

        /// <summary>
        /// Reads whiteboard timing notation into a timeline, plus any words that describe the
        /// work and rest halves ("high intensity" / "spin recovery") as coaching notes.
        /// </summary>
        public static bool TryParsePattern(string text, out Timeline timeline, out string workNote, out string restNote)
        {
            timeline = null;
            workNote = null;
            restNote = null;
            string value = (text ?? string.Empty).Trim();
            // An echoed summary line: "Timing: 4x 45s/15s = 4:00" -> "4x 45s/15s".
            value = Regex.Replace(value, @"^\s*(timing|pattern|intervals?)\s*:\s*", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\s*=.*$", string.Empty, RegexOptions.Singleline).Trim();
            if (value.Length == 0) return false;

            int rounds = 1;
            Match roundsMatch = Regex.Match(value, @"^\s*(\d{1,3})\s*[x×*]\s*(?=\d|tabata|pyramid)", RegexOptions.IgnoreCase);
            if (!roundsMatch.Success) roundsMatch = Regex.Match(value, @"^\s*(\d{1,3})\s*(?:rounds?|sets?|times)\s*(?:of\s*)?(?=\d|tabata|pyramid)", RegexOptions.IgnoreCase);
            if (!roundsMatch.Success) roundsMatch = RoundsRegex.Match(value);
            if (roundsMatch.Success)
            {
                rounds = int.Parse(roundsMatch.Groups[1].Success ? roundsMatch.Groups[1].Value : roundsMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                value = value.Remove(roundsMatch.Index, roundsMatch.Length);
            }
            rounds = Math.Max(1, Math.Min(rounds, 200));

            bool recovery = Regex.IsMatch(value, @"\brecover", RegexOptions.IgnoreCase);
            BlockType restType = recovery ? BlockType.Recovery : BlockType.Rest;
            List<Block> blocks = new List<Block>();

            if (Regex.IsMatch(value, @"\btabata\b", RegexOptions.IgnoreCase))
            {
                if (!roundsMatch.Success) rounds = 8;
                blocks.Add(new Block(BlockType.Work, 20));
                blocks.Add(new Block(restType, 10));
            }
            else if (Regex.IsMatch(value, @"\bpyramid\b", RegexOptions.IgnoreCase))
            {
                string[] halves = value.Split(new[] { '/' }, 2);
                List<int> steps = DurationRegex.Matches(halves[0]).Cast<Match>().Select(ToSeconds).Where(s => s > 0).ToList();
                int rest = halves.Length > 1 ? DurationRegex.Matches(halves[1]).Cast<Match>().Select(ToSeconds).FirstOrDefault() : 0;
                if (rest == 0)
                {
                    Match restWord = Regex.Match(value, @"(?:rest|off|recovery)\s*(\d+)", RegexOptions.IgnoreCase);
                    if (restWord.Success) { rest = int.Parse(restWord.Groups[1].Value, CultureInfo.InvariantCulture); steps.Remove(rest); }
                }
                if (steps.Count == 0 && !Regex.IsMatch(value, @"\d"))
                {
                    // Just "pyramid": the usual 20-30-40 seconds up and back down, 20 s rest.
                    steps = new List<int> { 20, 30, 40 };
                    if (rest == 0) rest = 20;
                }
                if (steps.Count < 2) return false;

                // Up and back down: 20-30-40 -> 20, 30, 40, 30, 20 (unless already written out symmetrically).
                List<int> sequence = new List<int>(steps);
                bool alreadySymmetric = steps.Count >= 3 && steps.SequenceEqual(Enumerable.Reverse(steps));
                if (!alreadySymmetric) sequence.AddRange(Enumerable.Reverse(steps).Skip(1));
                foreach (int work in sequence)
                {
                    blocks.Add(new Block(BlockType.Work, work));
                    if (rest > 0) blocks.Add(new Block(restType, rest));
                }
            }
            else
            {
                string[] halves = value.Split(new[] { '/' }, 2);
                List<Match> all = DurationRegex.Matches(value).Cast<Match>().ToList();
                if (all.Count == 0) return false;

                int work = ToSeconds(all[0]);
                int rest = all.Count > 1 ? ToSeconds(all[1]) : 0;
                if (work <= 0) return false;

                blocks.Add(new Block(BlockType.Work, work));
                if (rest > 0) blocks.Add(new Block(restType, rest));

                if (halves.Length == 2)
                {
                    workNote = NoteFrom(halves[0]);
                    restNote = NoteFrom(halves[1]);
                }
            }

            if (blocks.Count == 0) return false;

            timeline = new Timeline();
            if (rounds > 1)
            {
                RepeatGroup group = new RepeatGroup { Count = rounds };
                group.Blocks.AddRange(blocks);
                timeline.Add(group);
            }
            else
            {
                foreach (Block block in blocks) timeline.Add(block);
            }
            return true;
        }

        private static int ToSeconds(Match m)
        {
            double number = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string unit = m.Groups[3].Value.ToLowerInvariant();
            if (m.Groups[2].Success) return (int)(number * 60) + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture); // 1:30
            if (unit.StartsWith("m", StringComparison.Ordinal)) return (int)Math.Round(number * 60);
            return (int)Math.Round(number);
        }

        /// <summary>Words left in one half of "40 sec high intensity / 20 sec spin recovery" once the numbers are removed.</summary>
        private static string NoteFrom(string half)
        {
            // Remove the times (percentages like "80%" are kept - they're part of the cue).
            string note = DurationRegex.Replace(half ?? string.Empty, " ");
            note = note.Replace("@", " at ");
            note = Regex.Replace(note, @"^\s*(on|off|of)\b|\b(on|off)\s*$", " ", RegexOptions.IgnoreCase);
            note = Regex.Replace(note, @"[()]", " ");
            note = Regex.Replace(note, @"\b(reps?|rounds?|sets?|times|each|per\s+station|x)\b", " ", RegexOptions.IgnoreCase);
            note = Regex.Replace(note, @"\s+", " ").Trim().Trim('-', ',', '.', ':');

            // A bare "rest" / "work" / "recovery" says nothing the screen doesn't already show.
            if (Regex.IsMatch(note, @"^(on|off|work|rest|recovery|recover)?$", RegexOptions.IgnoreCase) || note.Length < 3) return null;
            return char.ToUpperInvariant(note[0]) + note.Substring(1);
        }

        private static int? ParseMove(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (Regex.IsMatch(value, @"^(none|no|0|zero|skip)$", RegexOptions.IgnoreCase)) return 0;
            Match m = DurationRegex.Match(value);
            if (!m.Success) return null;
            int seconds = ToSeconds(m);
            return seconds >= 0 && seconds <= 900 ? seconds : (int?)null;
        }

        private static ExecutionMode? ModeFromChoice(string text)
        {
            Match n = Regex.Match(text ?? string.Empty, @"^\s*([1-4])\b");
            if (n.Success)
            {
                switch (n.Groups[1].Value)
                {
                    case "1": return ExecutionMode.Shared;
                    case "2": return ExecutionMode.Sequential;
                    case "3": return ExecutionMode.Parallel;
                    default: return ExecutionMode.Circuit;
                }
            }
            return ModeFromWords(text);
        }

        private static ExecutionMode? ModeFromWords(string text)
        {
            string value = text ?? string.Empty;
            if (Regex.IsMatch(value, @"\bparallel\b", RegexOptions.IgnoreCase)) return ExecutionMode.Parallel;
            if (Regex.IsMatch(value, @"\bsequential\b", RegexOptions.IgnoreCase)) return ExecutionMode.Sequential;
            if (Regex.IsMatch(value, @"\bcircuit\b", RegexOptions.IgnoreCase) && Regex.IsMatch(value, @"\bmode\s+circuit\b|\bshared\s+circuit\b", RegexOptions.IgnoreCase)) return ExecutionMode.Circuit;
            if (Regex.IsMatch(value, @"\bmode\s+shared\b|\bshared\s+timing\b", RegexOptions.IgnoreCase)) return ExecutionMode.Shared;
            return null;
        }

        private string DefaultName()
        {
            if (!string.IsNullOrWhiteSpace(_name)) return _name;
            switch (_mode)
            {
                case ExecutionMode.Parallel: return "Rotating Stations";
                case ExecutionMode.Circuit: return "Circuit";
                case ExecutionMode.Sequential: return "Station Session";
                default: return "Group Workout";
            }
        }

        // ------------------------------------------------------------------ output

        /// <summary>The program as the editor would build it.</summary>
        public WorkoutProgram BuildProgram()
        {
            ExecutionMode mode = _mode ?? ExecutionMode.Shared;
            WorkoutProgram program = new WorkoutProgram { Name = string.IsNullOrWhiteSpace(_name) ? DefaultName() : _name, Mode = mode };

            bool perStation = mode == ExecutionMode.Sequential || mode == ExecutionMode.Parallel;
            if (!perStation && _sharedTiming != null) program.SharedTimeline = _sharedTiming.Clone();

            foreach (StationPlan plan in _stations)
            {
                string timingNote = plan.WorkNote ?? (perStation && plan.Timing == null ? _sharedWorkNote : null);
                StationDef station = new StationDef
                {
                    Name = plan.Name,
                    Source = TimingSource.Custom,
                    WorkInstruction = JoinNotes(timingNote, plan.Cue),
                    RestInstruction = plan.RestNote ?? (perStation && plan.Timing == null ? _sharedRestNote : null) ?? string.Empty
                };
                if (perStation)
                {
                    Timeline timing = plan.Timing ?? _sharedTiming;
                    if (timing != null) station.CustomTimeline = timing.Clone();
                }
                program.Stations.Add(station);
            }

            if (_moveSeconds.HasValue && _moveSeconds.Value > 0 && mode != ExecutionMode.Shared && program.Stations.Count >= 2)
            {
                program.BetweenStationsTimeline.Add(new Block(BlockType.Move, _moveSeconds.Value)
                {
                    Label = mode == ExecutionMode.Parallel || mode == ExecutionMode.Circuit ? "Rotate to the next station" : "Move to the next station"
                });
            }

            // Shared/Circuit with stations: give each a colour so the panel is easy to read.
            if (!perStation)
            {
                string[] palette = { "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#3949AB", "#8E24AA", "#D81B60" };
                for (int i = 0; i < program.Stations.Count; i++) program.Stations[i].Colour = palette[i % palette.Length];
                if (!string.IsNullOrEmpty(_sharedWorkNote) || !string.IsNullOrEmpty(_sharedRestNote))
                {
                    for (int i = 0; i < program.Stations.Count; i++)
                    {
                        StationDef s = program.Stations[i];
                        s.WorkInstruction = JoinNotes(_sharedWorkNote, _stations[i].Cue);
                        if (string.IsNullOrEmpty(s.RestInstruction)) s.RestInstruction = _sharedRestNote ?? string.Empty;
                    }
                }
            }

            return program;
        }

        /// <summary>The work note: the teacher's words from the timing ("high intensity") if any, otherwise the coaching tip.</summary>
        private static string JoinNotes(string timingNote, string cue)
        {
            string a = (timingNote ?? string.Empty).Trim();
            string b = (cue ?? string.Empty).Trim();
            // The teacher's own words from the whiteboard win; the library/AI tip fills the gap.
            return a.Length > 0 ? a : b;
        }

        public string BuildScript()
        {
            return ProgramScriptFormat.ToScript(BuildProgram());
        }

        /// <summary>Plain-English description with real, computed durations.</summary>
        public string Summary()
        {
            WorkoutProgram program = BuildProgram();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Here's the plan for \"" + program.Name + "\":");
            sb.AppendLine("  " + ModeLabel());

            if (program.Mode == ExecutionMode.Sequential || program.Mode == ExecutionMode.Parallel)
            {
                for (int i = 0; i < program.Stations.Count; i++)
                {
                    Timeline t = program.TimelineForStation(i);
                    sb.AppendLine("  " + (i + 1) + ". " + program.Stations[i].Name + " - " + Describe(t) + TipText(program.Stations[i]));
                }
            }
            else
            {
                sb.AppendLine("  Timing: " + Describe(program.SharedTimeline));
                if (program.Stations.Count > 0) sb.AppendLine("  Stations:");
                for (int i = 0; i < program.Stations.Count; i++)
                {
                    sb.AppendLine("  " + (i + 1) + ". " + program.Stations[i].Name + TipText(program.Stations[i]));
                }
            }

            if (program.Mode != ExecutionMode.Shared && program.Stations.Count >= 2)
            {
                int move = program.BetweenStationsTimeline.TotalSeconds();
                sb.AppendLine("  Move: " + (move > 0 ? Clock(move) : "none"));
            }

            int total;
            if (program.Mode == ExecutionMode.Parallel)
            {
                ParallelRotationPlan plan = program.GetParallelRotationPlan();
                total = plan.TotalSeconds;
                sb.AppendLine("  Rotations: " + plan.Rotations + " (one per station), " + Clock(plan.StationSeconds) + " each - early finishers recover until the longest station is done");
            }
            else
            {
                total = program.BuildRuntimeSequence().Sum(b => Math.Max(0, b.Block.Seconds));
            }
            sb.Append("  Total: " + Clock(total));
            return sb.ToString();
        }

        private static string TipText(StationDef station)
        {
            return string.IsNullOrWhiteSpace(station.WorkInstruction) ? string.Empty
                : Environment.NewLine + "       Tip: " + station.WorkInstruction;
        }

        private static string Describe(Timeline t)
        {
            if (t == null || t.IsEmpty) return "no timing";
            return t.Summary() + " = " + Clock(t.TotalSeconds());
        }

        private static string Clock(int seconds)
        {
            return (seconds / 60) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
