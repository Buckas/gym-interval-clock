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

        private readonly string _originalRequest;
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
        }

        public GuidedProgramBuilder(string originalRequest,
            Func<string, int, CancellationToken, Task<IReadOnlyList<string>>> suggestNames = null)
        {
            _originalRequest = (originalRequest ?? string.Empty).Trim();
            _suggestNames = suggestNames;

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
                    return Advance(Step.Stations);
                }

                case Step.Stations:
                {
                    string error = await ReadStationsAsync(text, cancellationToken).ConfigureAwait(false);
                    if (error != null) return error + Environment.NewLine + Environment.NewLine + Question(Step.Stations);

                    // Every station already came with its own timing (e.g. a pasted whiteboard) - no need to ask again.
                    bool allTimed = _stations.Count > 0 && _stations.All(s => s.Timing != null);
                    return Advance(allTimed ? NextAfterTiming() : Step.Timing);
                }

                case Step.Timing:
                {
                    string error = ReadTiming(text);
                    if (error != null) return error + Environment.NewLine + Environment.NewLine + Question(Step.Timing);
                    return Advance(NextAfterTiming());
                }

                case Step.Move:
                {
                    int? seconds = ParseMove(text);
                    if (!seconds.HasValue) return "I didn't catch a time - reply like 30, 1 min, or 0 for no move." + Environment.NewLine + Environment.NewLine + Question(Step.Move);
                    _moveSeconds = seconds.Value;
                    return Advance(_name == null ? Step.Name : Step.Confirm);
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

                    if (Regex.IsMatch(text, @"\bstations?\b", RegexOptions.IgnoreCase)) { _stations.Clear(); _sharedTiming = null; return Advance(Step.Stations); }
                    if (Regex.IsMatch(text, @"\b(timing|times?|pattern)\b", RegexOptions.IgnoreCase)) { ClearTiming(); return Advance(Step.Timing); }
                    if (Regex.IsMatch(text, @"\bmove\b", RegexOptions.IgnoreCase)) return Advance(Step.Move);
                    if (Regex.IsMatch(text, @"\bname\b", RegexOptions.IgnoreCase)) return Advance(Step.Name);
                    if (Regex.IsMatch(text, @"\bmode\b", RegexOptions.IgnoreCase) || Regex.IsMatch(text, @"^(n|no|restart)$", RegexOptions.IgnoreCase))
                    {
                        _mode = null; _stations.Clear(); ClearTiming(); _moveSeconds = null;
                        return Advance(Step.Mode);
                    }

                    return "Reply Y to create it, or say what to change: mode, stations, timing, move or name."
                        + Environment.NewLine + Environment.NewLine + Summary();
                }
            }

            return string.Empty;
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
                        + "Reply Y to create it, or say what to change: mode, stations, timing, move or name.";
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

            Match count = Regex.Match(text, @"^\s*(\d{1,2})\s*(stations?)?\s*$", RegexOptions.IgnoreCase);
            if (count.Success)
            {
                int n = int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture);
                if (n < 1 || n > 20) return "Please choose between 1 and 20 stations.";
                IReadOnlyList<string> names = await SuggestNamesAsync(n, cancellationToken).ConfigureAwait(false);
                foreach (string name in names) _stations.Add(new StationPlan { Name = name });
                return null;
            }

            List<string> lines = text.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
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
            return null;
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
            if (_suggestNames != null)
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
            // Whiteboard capitals ("BIKE") -> "Bike"; keep mixed case as written.
            if (value == value.ToUpperInvariant()) value = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
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
            if (value.Length == 0) return false;

            int rounds = 1;
            Match roundsMatch = RoundsRegex.Match(value);
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
                StationDef station = new StationDef
                {
                    Name = plan.Name,
                    Source = TimingSource.Custom,
                    WorkInstruction = plan.WorkNote ?? (perStation && plan.Timing == null ? _sharedWorkNote : null) ?? string.Empty,
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
                    foreach (StationDef s in program.Stations)
                    {
                        if (string.IsNullOrEmpty(s.WorkInstruction)) s.WorkInstruction = _sharedWorkNote ?? string.Empty;
                        if (string.IsNullOrEmpty(s.RestInstruction)) s.RestInstruction = _sharedRestNote ?? string.Empty;
                    }
                }
            }

            return program;
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
                    sb.AppendLine("  " + (i + 1) + ". " + program.Stations[i].Name + " - " + Describe(t));
                }
            }
            else
            {
                sb.AppendLine("  Timing: " + Describe(program.SharedTimeline));
                if (program.Stations.Count > 0) sb.AppendLine("  Stations: " + string.Join(", ", program.Stations.Select(s => s.Name)));
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
