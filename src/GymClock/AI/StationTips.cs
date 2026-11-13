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
    /// <summary>A suggested work/rest note for one station of the loaded program.</summary>
    public sealed class StationTip
    {
        public int StationIndex { get; set; }
        public string StationName { get; set; }
        public string Work { get; set; }
        public string Rest { get; set; }
    }

    /// <summary>
    /// "Give me coaching tips for each station" on a loaded program: a work note and a rest note per
    /// station, from the exercise library (or the local AI for stations it doesn't know). Shown first;
    /// written into the program only when the teacher says APPLY, or asked for it as an instruction.
    /// </summary>
    public static class StationTips
    {
        private static readonly Regex TipWord = new Regex(
            @"\b(tips?|cues?|coaching|notes?|messages?|text\s+to\s+show|instructions?)\b", RegexOptions.IgnoreCase);
        private static readonly Regex ProgramWord = new Regex(
            @"\b(stations?|work|rest|each|every|all|program|programme|current|these|this|my|our)\b", RegexOptions.IgnoreCase);
        private static readonly Regex ApplyNow = new Regex(
            @"^\s*(please\s+)?(add|put|apply|update|set|write|insert|fill\s+in|replace)\b", RegexOptions.IgnoreCase);

        /// <summary>"Can you give me better coaching tips for work and rest", "tips for each station", "add notes to the stations".</summary>
        public static bool IsTipsRequest(string request)
        {
            string text = request ?? string.Empty;
            if (Regex.IsMatch(text, @"^\s*\d")) return false; // "3 - make sure there are notes" picks an offered option
            // The teacher's own wording ("...note for station 2 with 'Keep your back flat'") is an edit, not a request for ideas.
            if (Regex.IsMatch(text, "[\"“”]|\\b(with|to\\s+say|to\\s+read|saying|that\\s+says)\\s+\\S", RegexOptions.IgnoreCase)) return false;
            return TipWord.IsMatch(text) && ProgramWord.IsMatch(text);
        }

        /// <summary>True when the request tells us to write them in rather than show them.</summary>
        public static bool WantsThemApplied(string request)
        {
            return ApplyNow.IsMatch(request ?? string.Empty);
        }

        /// <summary>"APPLY", "yes", "add them", "apply 1, 3" after tips were shown. Null = not a reply to the tips.</summary>
        public static IReadOnlyList<int> ParseApplyReply(string reply, int stationCount)
        {
            string text = (reply ?? string.Empty).Trim();
            Match m = Regex.Match(text, @"^(apply|yes|y|ok|okay|sure|please|do\s+it|use\s+(them|these|those)|add\s+(them|these|those))\b(.*)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return null;

            List<int> picks = Regex.Matches(m.Groups[4].Value, @"\d{1,2}").Cast<Match>()
                .Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture) - 1)
                .Where(i => i >= 0 && i < stationCount).Distinct().ToList();
            return picks.Count > 0 ? picks : Enumerable.Range(0, stationCount).ToList();
        }

        /// <summary>
        /// Tips for the stations the request names ("tips for station 2", "for the treadmill"), or all of them.
        /// The AI is asked only for stations the library doesn't know, and only when it is available.
        /// </summary>
        public static async Task<List<StationTip>> SuggestAsync(WorkoutProgram program, string request,
            Func<string, CancellationToken, Task<string>> aiCue, CancellationToken cancellationToken)
        {
            List<StationTip> tips = new List<StationTip>();
            if (program == null) return tips;

            List<int> wanted = StationsNamed(program, request);
            foreach (int i in wanted)
            {
                StationDef station = program.Stations[i];
                Exercise known = ExerciseLibrary.FindByName(station.Name);

                SplitCue(known?.Cue, out string work, out string restPart);
                if (string.IsNullOrWhiteSpace(work) && aiCue != null)
                {
                    try { work = await aiCue(station.Name, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch { work = null; }
                }
                if (string.IsNullOrWhiteSpace(work)) work = GenericWorkTip(station.Name);
                else
                {
                    SplitCue(work, out work, out string aiRest);
                    if (!string.IsNullOrEmpty(aiRest)) restPart = aiRest;
                }

                tips.Add(new StationTip
                {
                    StationIndex = i,
                    StationName = station.Name,
                    Work = Tidy(work),
                    Rest = string.IsNullOrEmpty(restPart) ? RestTip(known, station.Name, i) : restPart
                });
            }
            return tips;
        }

        private static List<int> StationsNamed(WorkoutProgram program, string request)
        {
            string text = request ?? string.Empty;
            List<int> named = new List<int>();

            foreach (Match m in Regex.Matches(text, @"\bstations?\s+(\d{1,2})\b", RegexOptions.IgnoreCase))
            {
                int i = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                if (i >= 0 && i < program.Stations.Count && !named.Contains(i)) named.Add(i);
            }
            for (int i = 0; i < program.Stations.Count; i++)
            {
                string name = program.Stations[i].Name ?? string.Empty;
                if (name.Length > 2 && text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && !named.Contains(i)) named.Add(i);
            }
            return named.Count > 0 ? named.OrderBy(i => i).ToList() : Enumerable.Range(0, program.Stations.Count).ToList();
        }

        /// <summary>
        /// Splits a cue that covers both halves into the work part and the rest part:
        /// "High cadence on work, easy spin on rest" -> "High cadence" / "Easy spin";
        /// "Run at about 80% effort, walk to recover" -> "Run at about 80% effort" / "Walk to recover".
        /// A cue with no rest part comes back whole, with rest = null.
        /// </summary>
        public static void SplitCue(string cue, out string work, out string rest)
        {
            work = null;
            rest = null;
            string text = (cue ?? string.Empty).Trim();
            if (text.Length == 0) return;

            List<string> workParts = new List<string>();
            List<string> restParts = new List<string>();
            foreach (string part in Regex.Split(text, @"\s*(?:,|;|\s-\s|\bthen\b)\s*"))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                if (Regex.IsMatch(p, @"\b(on\s+(the\s+)?rest|to\s+recover|in\s+(the\s+)?rest|during\s+(the\s+)?rest|recovery|between\s+(rounds|sets))\b", RegexOptions.IgnoreCase))
                    restParts.Add(Regex.Replace(p, @"\s+(on|in|during)\s+(the\s+)?rest$", string.Empty, RegexOptions.IgnoreCase));
                else
                    workParts.Add(Regex.Replace(p, @"\s+(on|in|during)\s+(the\s+)?work$", string.Empty, RegexOptions.IgnoreCase));
            }

            if (restParts.Count == 0 || workParts.Count == 0) { work = Tidy(text); return; }
            work = Tidy(string.Join(", ", workParts));
            rest = Tidy(string.Join(", ", restParts));
        }

        /// <summary>A work note and a rest note for a station from the exercise library (work = null if it's not in the library).</summary>
        public static void ForStation(string stationName, int index, out string work, out string rest)
        {
            Exercise known = ExerciseLibrary.FindByName(stationName);
            SplitCue(known?.Cue, out work, out string restPart);
            rest = string.IsNullOrEmpty(restPart) ? RestTip(known, stationName, index) : restPart;
        }

        private static string GenericWorkTip(string name)
        {
            return "Good form first, then build the pace";
        }

        // Rest notes by the kind of exercise - something useful to do in the rest, not just "rest".
        private static string RestTip(Exercise known, string name, int index)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            if (known != null && (known.Kit & ExerciseKit.CardioMachine) != 0 || Regex.IsMatch(n, @"\b(bike|treadmill|rower|rowing|erg|elliptical|cross\s*trainer)\b"))
                return "Easy pace - let your breathing settle";
            if (known != null && (known.Kit & ExerciseKit.SkippingRope) != 0 || n.Contains("skip"))
                return "Rope down, shake out the wrists, deep breaths";

            ExerciseTag tags = known?.Tags ?? ExerciseTag.None;
            string[] options;
            if ((tags & ExerciseTag.Plyometric) != 0) options = new[] { "Walk it off and shake out the legs", "Slow breaths, land soft next round" };
            else if ((tags & ExerciseTag.Strength) != 0) options = new[] { "Put it down, shake out, reset your stance", "Breathe and check your form for the next set" };
            else if ((tags & ExerciseTag.Core) != 0) options = new[] { "Lie back, relax the stomach, breathe deep", "Breathe deep and reset your position" };
            else if ((tags & ExerciseTag.Mobility) != 0) options = new[] { "Breathe slowly and stand tall" };
            else if ((tags & ExerciseTag.Cardio) != 0) options = new[] { "Hands on head, slow deep breaths", "Walk it off - big breaths in through the nose" };
            else options = new[] { "Breathe deep and get ready to go again", "Shake out and get set for the next round" };
            return options[index % options.Length];
        }

        public static string Tidy(string tip)
        {
            string t = Regex.Replace((tip ?? string.Empty).Trim().Trim('"', '.', ' '), @"\s+", " ");
            return t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t.Substring(1);
        }

        /// <summary>The chat reply listing the tips.</summary>
        public static string Describe(IReadOnlyList<StationTip> tips, bool applied)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(applied ? "Done - I've added these notes to the program:" : "Here are work and rest notes for each station:");
            foreach (StationTip t in tips)
            {
                sb.Append(Environment.NewLine).Append(t.StationIndex + 1).Append(") ").Append(t.StationName);
                sb.Append(Environment.NewLine).Append("   Work: ").Append(t.Work);
                sb.Append(Environment.NewLine).Append("   Rest: ").Append(t.Rest);
            }
            sb.Append(Environment.NewLine).Append(Environment.NewLine);
            sb.Append(applied
                ? "They're in the Proposed Script box - click Apply to Editor to use them, and you can edit any note there."
                : "Nothing has been changed yet. Reply APPLY to add them all, APPLY 1, 3 for just those stations, "
                  + "or write your own, e.g. 'Replace the work note for station 2 with ...'.");
            return sb.ToString();
        }

        /// <summary>Writes the chosen tips into the program (pass a freshly parsed copy) as WORKNOTE/RESTNOTE.</summary>
        public static WorkoutProgram Apply(WorkoutProgram copy, IEnumerable<StationTip> tips, IEnumerable<int> stationIndexes = null)
        {
            HashSet<int> only = stationIndexes == null ? null : new HashSet<int>(stationIndexes);
            foreach (StationTip t in tips)
            {
                if (only != null && !only.Contains(t.StationIndex)) continue;
                if (t.StationIndex < 0 || t.StationIndex >= copy.Stations.Count) continue;
                copy.Stations[t.StationIndex].WorkInstruction = t.Work;
                copy.Stations[t.StationIndex].RestInstruction = t.Rest;
            }
            return copy;
        }
    }
}
