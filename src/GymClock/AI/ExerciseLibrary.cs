using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GymClock.AI
{
    /// <summary>What an exercise trains / how it feels.</summary>
    [Flags]
    public enum ExerciseTag
    {
        None = 0,
        Cardio = 1,
        Strength = 2,
        Core = 4,
        Plyometric = 8,
        Mobility = 16,
        Upper = 32,
        Lower = 64,
        FullBody = 128,
        LowImpact = 256
    }

    /// <summary>Equipment an exercise needs (None = bodyweight only).</summary>
    [Flags]
    public enum ExerciseKit
    {
        None = 0,
        Dumbbell = 1,
        Kettlebell = 2,
        Band = 4,
        MedicineBall = 8,
        StepOrBench = 16,
        Box = 32,
        SkippingRope = 64,
        BattleRope = 128,
        CardioMachine = 256,
        Suspension = 512,
        Wall = 1024,          // not really "equipment" - allowed for no-equipment requests
        WeightPlate = 2048,
        Cones = 4096,
        SlamBall = 8192
    }

    public sealed class Exercise
    {
        public string Name { get; }
        public string Cue { get; }
        public ExerciseTag Tags { get; }
        public ExerciseKit Kit { get; }
        public bool Floor { get; }      // done on the floor / a mat
        public string Id { get; }

        public Exercise(string name, string cue, ExerciseTag tags, ExerciseKit kit = ExerciseKit.None, bool floor = false, string id = null)
        {
            Name = name; Cue = cue; Tags = tags; Kit = kit; Floor = floor; Id = id ?? name;
        }
    }

    /// <summary>A parsed "I need N … exercises" request.</summary>
    public sealed class ExerciseQuery
    {
        public int Count = 5;
        public ExerciseTag AnyOfTypes;      // cardio / strength / plyometric / mobility / core (OR)
        public ExerciseTag AnyOfRegions;    // upper / lower / core / full body (OR)
        public bool LowImpact;
        public bool NoEquipment;
        public ExerciseKit AnyOfKit;        // specific equipment asked for (OR)
        public bool FloorOnly;
        public bool WantsDifferent;         // "more", "other", "different"
    }

    /// <summary>
    /// Searches the exercise catalog (shared library + the teacher's own changes) by type, body
    /// area, equipment and floor/no-floor. Answers "I need 5 resistance exercises with basic
    /// weights" style requests instantly and reliably - no AI needed, nothing invented.
    /// </summary>
    public static class ExerciseLibrary
    {
        /// <summary>
        /// The exercises available for suggestions: the shared library (data/exercises.json, or a newer
        /// downloaded copy) plus the teacher's own edits and exercises, minus hidden ones.
        /// </summary>
        public static IReadOnlyList<Exercise> All
        {
            get { return ExerciseCatalog.Current.Visible; }
        }

        // ------------------------------------------------------------------ intent + parsing

        private static readonly Regex ExerciseNoun = new Regex(@"\b(exercises?|examples?|ideas?|options?|moves?|drills?|activities)\b", RegexOptions.IgnoreCase);
        private static readonly Regex AskWord = new Regex(
            @"\b(need|want|give|suggest|list|show|what|which|some|any|recommend|ideas?|examples?|more|other|different|can\s+you|could\s+you)\b",
            RegexOptions.IgnoreCase);
        private static readonly Regex ProgramEdit = new Regex(@"\b(replace|swap|change|rename|remove|delete|program|programme|script)\b", RegexOptions.IgnoreCase);

        private static readonly string[] NumberWords =
            { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

        /// <summary>"I need 5 examples of resistance exercises with basic weights" - but not edits to the program.</summary>
        public static bool IsIdeasRequest(string request)
        {
            string text = request ?? string.Empty;
            if (!ExerciseNoun.IsMatch(text) || !AskWord.IsMatch(text)) return false;
            if (ProgramEdit.IsMatch(text)) return false;
            return true;
        }

        public static ExerciseQuery Parse(string request)
        {
            string text = (request ?? string.Empty).ToLowerInvariant();
            ExerciseQuery q = new ExerciseQuery();

            // The count is the number that goes with the exercises ("5 examples", "four floor based exercises"),
            // not any other number in the sentence ("for year 7s").
            const string countedNoun = @"(?=(?:\s+[a-z-]+){0,4}?\s+(?:exercises?|examples?|ideas?|options?|moves?|drills?|activities)\b)";
            Match digits = Regex.Match(text, @"\b(\d{1,2})\b" + countedNoun);
            if (digits.Success) q.Count = int.Parse(digits.Groups[1].Value, CultureInfo.InvariantCulture);
            else
            {
                for (int i = 1; i < NumberWords.Length; i++)
                {
                    if (Regex.IsMatch(text, @"\b" + NumberWords[i] + @"\b" + countedNoun)) { q.Count = i; break; }
                }
            }
            q.Count = Math.Max(1, Math.Min(q.Count, 12));

            if (Regex.IsMatch(text, @"\b(cardio|aerobic|fitness|conditioning|heart rate)\b")) q.AnyOfTypes |= ExerciseTag.Cardio;
            if (Regex.IsMatch(text, @"\b(resistance|strength|strengthening|muscle|toning)\b")) q.AnyOfTypes |= ExerciseTag.Strength;
            if (Regex.IsMatch(text, @"\b(plyo|plyometric|explosive|power|jumping|jumps?)\b")) q.AnyOfTypes |= ExerciseTag.Plyometric;
            if (Regex.IsMatch(text, @"\b(stretch|stretches|stretching|mobility|flexibility|warm[\s-]?up|cool[\s-]?down)\b")) q.AnyOfTypes |= ExerciseTag.Mobility;

            if (Regex.IsMatch(text, @"\b(core|abs|abdominal|trunk)\b")) q.AnyOfRegions |= ExerciseTag.Core;
            if (Regex.IsMatch(text, @"\b(upper[\s-]?body|arms?|chest|shoulders?|back)\b")) q.AnyOfRegions |= ExerciseTag.Upper;
            if (Regex.IsMatch(text, @"\b(lower[\s-]?body|legs?|glutes?|hips?)\b")) q.AnyOfRegions |= ExerciseTag.Lower;
            if (Regex.IsMatch(text, @"\b(full[\s-]?body|whole[\s-]?body|total[\s-]?body)\b")) q.AnyOfRegions |= ExerciseTag.FullBody;

            q.LowImpact = Regex.IsMatch(text, @"\b(low[\s-]?impact|beginners?|easy|easier|quiet|gentle|no jumping)\b");
            q.FloorOnly = Regex.IsMatch(text, @"\b(floor|mat|lying|on the ground)\b");
            q.NoEquipment = Regex.IsMatch(text, @"\b(no|without|zero)\s+(any\s+)?(equipment|gear|kit|weights)\b|\bbody[\s-]?weight\b|\bequipment[\s-]?free\b");
            q.WantsDifferent = Regex.IsMatch(text, @"\b(more|other|others|different|another|new ones|else)\b");

            if (!q.NoEquipment)
            {
                if (Regex.IsMatch(text, @"\b(weights?|dumbbells?|db|free weights|hand weights)\b")) q.AnyOfKit |= ExerciseKit.Dumbbell | ExerciseKit.Kettlebell | ExerciseKit.WeightPlate;
                if (Regex.IsMatch(text, @"\bkettle\s?bells?\b")) q.AnyOfKit |= ExerciseKit.Kettlebell;
                if (Regex.IsMatch(text, @"\b(plates?)\b")) q.AnyOfKit |= ExerciseKit.WeightPlate;
                if (Regex.IsMatch(text, @"\b(bands?|resistance bands?|tubing)\b")) q.AnyOfKit |= ExerciseKit.Band;
                if (Regex.IsMatch(text, @"\b(medicine ball|med ball|slam ball|wall ball)s?\b")) q.AnyOfKit |= ExerciseKit.MedicineBall | ExerciseKit.SlamBall;
                if (Regex.IsMatch(text, @"\b(step|bench|benches|steps)\b")) q.AnyOfKit |= ExerciseKit.StepOrBench;
                if (Regex.IsMatch(text, @"\b(box|plyo box)\b")) q.AnyOfKit |= ExerciseKit.Box;
                if (Regex.IsMatch(text, @"\b(skipping|jump rope|skipping rope)s?\b")) q.AnyOfKit |= ExerciseKit.SkippingRope;
                if (Regex.IsMatch(text, @"\bbattle ropes?\b")) q.AnyOfKit |= ExerciseKit.BattleRope;
                if (Regex.IsMatch(text, @"\b(machines?|bikes?|treadmills?|rowers?|rowing|elliptical|ergs?)\b")) q.AnyOfKit |= ExerciseKit.CardioMachine;
                if (Regex.IsMatch(text, @"\b(trx|suspension)\b")) q.AnyOfKit |= ExerciseKit.Suspension;
                if (Regex.IsMatch(text, @"\bcones?\b")) q.AnyOfKit |= ExerciseKit.Cones;
            }
            return q;
        }

        // ------------------------------------------------------------------ search

        public static List<Exercise> Find(ExerciseQuery q)
        {
            ExerciseKit? owned = ExerciseCatalog.Current.OwnedKit;
            return All.Where(e => Matches(e, q, relaxed: false, owned)).ToList();
        }

        private static bool Matches(Exercise e, ExerciseQuery q, bool relaxed, ExerciseKit? owned)
        {
            // Only suggest equipment the gym has (set in the Exercises window) - unless it was asked for by name.
            ExerciseKit needs = e.Kit & ~ExerciseKit.Wall;
            if (owned.HasValue && needs != ExerciseKit.None && (needs & q.AnyOfKit) == ExerciseKit.None
                && (needs & owned.Value) == ExerciseKit.None) return false;

            if (q.NoEquipment && (e.Kit & ~ExerciseKit.Wall) != ExerciseKit.None) return false;
            if (q.AnyOfKit != ExerciseKit.None && (e.Kit & q.AnyOfKit) == ExerciseKit.None) return false;
            if (q.AnyOfTypes != ExerciseTag.None && (e.Tags & q.AnyOfTypes) == ExerciseTag.None) return false;
            if (!relaxed)
            {
                if (q.AnyOfRegions != ExerciseTag.None && (e.Tags & q.AnyOfRegions) == ExerciseTag.None) return false;
                if (q.FloorOnly && !e.Floor) return false;
                if (q.LowImpact && (e.Tags & ExerciseTag.LowImpact) == 0) return false;
            }
            return true;
        }

        /// <summary>Picks a varied set, avoiding anything just suggested when the teacher asks for more/different.</summary>
        public static List<Exercise> Pick(ExerciseQuery q, IEnumerable<string> recentlySuggested, Random random, out bool relaxed)
        {
            CatalogSnapshot catalog = ExerciseCatalog.Current;
            return Pick(q, recentlySuggested, random, catalog.Visible, catalog.OwnedKit, out relaxed);
        }

        public static List<Exercise> Pick(ExerciseQuery q, IEnumerable<string> recentlySuggested, Random random,
            IReadOnlyList<Exercise> exercises, ExerciseKit? owned, out bool relaxed)
        {
            HashSet<string> recent = new HashSet<string>(recentlySuggested ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            relaxed = false;

            List<Exercise> pool = exercises.Where(e => Matches(e, q, false, owned)).ToList();
            if (pool.Count < q.Count)
            {
                // Not enough exact matches: keep the must-haves (equipment, type) and loosen the rest.
                List<Exercise> extra = exercises.Where(e => Matches(e, q, true, owned) && !pool.Contains(e)).ToList();
                if (extra.Count > 0) relaxed = true;
                pool.AddRange(extra.OrderBy(_ => random.Next()));
            }

            IEnumerable<Exercise> fresh = q.WantsDifferent ? pool.Where(e => !recent.Contains(e.Name)) : pool;
            List<Exercise> ordered = fresh.OrderBy(_ => random.Next()).ToList();
            if (ordered.Count < q.Count) ordered.AddRange(pool.Where(e => !ordered.Contains(e)).OrderBy(_ => random.Next()));

            // Mix the body areas/types so five picks aren't five near-identical squats.
            List<Exercise> chosen = new List<Exercise>();
            foreach (Exercise e in ordered)
            {
                if (chosen.Count >= q.Count) break;
                bool tooSimilar = chosen.Count(c => (c.Tags & (ExerciseTag.Upper | ExerciseTag.Lower | ExerciseTag.Core)) == (e.Tags & (ExerciseTag.Upper | ExerciseTag.Lower | ExerciseTag.Core))) >= 2;
                if (!tooSimilar) chosen.Add(e);
            }
            foreach (Exercise e in ordered)
            {
                if (chosen.Count >= q.Count) break;
                if (!chosen.Contains(e)) chosen.Add(e);
            }
            return chosen;
        }

        // ------------------------------------------------------------------ answer

        /// <summary>Builds the chat reply and remembers the names (for "more" and for "use these" as stations).</summary>
        public static string Answer(string request, ConversationState state, Random random = null)
        {
            ExerciseQuery q = Parse(request);
            List<Exercise> picks = Pick(q, state?.LastExerciseIdeas, random ?? new Random(), out bool relaxed);

            if (picks.Count == 0)
            {
                return "I don't have any exercises that match " + Describe(q) + "." + Environment.NewLine
                    + "Try a different piece of equipment, or ask for bodyweight exercises.";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("Here " + (picks.Count == 1 ? "is " : "are ") + picks.Count + " " + Describe(q) + ":");
            for (int i = 0; i < picks.Count; i++)
            {
                sb.Append(Environment.NewLine + "  " + (i + 1) + ") " + picks[i].Name + " - " + picks[i].Cue);
            }
            if (picks.Count < q.Count)
            {
                sb.Append(Environment.NewLine + "(That's all I have for that combination.)");
            }
            else if (relaxed)
            {
                sb.Append(Environment.NewLine + "(Not enough exact matches, so some are close alternatives.)");
            }
            sb.Append(Environment.NewLine + Environment.NewLine
                + "Nothing has been changed. Ask for \"more\" for different ones, or say \"create a new program\" and reply USE THESE when I ask for the stations.");

            if (state != null) state.LastExerciseIdeas = picks.Select(p => p.Name).ToList();
            return sb.ToString();
        }

        /// <summary>"floor-based resistance exercises using basic weights" etc.</summary>
        public static string Describe(ExerciseQuery q)
        {
            List<string> before = new List<string>();
            if (q.LowImpact) before.Add("low-impact");
            if (q.FloorOnly) before.Add("floor-based");

            List<string> types = new List<string>();
            if ((q.AnyOfTypes & ExerciseTag.Cardio) != 0) types.Add("cardio");
            if ((q.AnyOfTypes & ExerciseTag.Strength) != 0) types.Add("resistance");
            if ((q.AnyOfTypes & ExerciseTag.Plyometric) != 0) types.Add("plyometric");
            if ((q.AnyOfTypes & ExerciseTag.Mobility) != 0) types.Add("mobility");
            List<string> regions = new List<string>();
            if ((q.AnyOfRegions & ExerciseTag.Upper) != 0) regions.Add("upper-body");
            if ((q.AnyOfRegions & ExerciseTag.Lower) != 0) regions.Add("lower-body");
            if ((q.AnyOfRegions & ExerciseTag.Core) != 0) regions.Add("core");
            if ((q.AnyOfRegions & ExerciseTag.FullBody) != 0) regions.Add("full-body");

            string text = string.Join(" ", before.Concat(new[] { string.Join(" / ", regions), string.Join(" / ", types) }).Where(s => s.Length > 0));
            text = (text.Length > 0 ? text + " " : string.Empty) + "exercises";

            if (q.NoEquipment) text += " with no equipment";
            else if (q.AnyOfKit != ExerciseKit.None) text += " using " + KitWords(q.AnyOfKit);
            return text;
        }

        private static string KitWords(ExerciseKit kit)
        {
            List<string> words = new List<string>();
            if ((kit & (ExerciseKit.Dumbbell | ExerciseKit.Kettlebell | ExerciseKit.WeightPlate)) == (ExerciseKit.Dumbbell | ExerciseKit.Kettlebell | ExerciseKit.WeightPlate)) words.Add("basic weights");
            else
            {
                if ((kit & ExerciseKit.Dumbbell) != 0) words.Add("dumbbells");
                if ((kit & ExerciseKit.Kettlebell) != 0) words.Add("kettlebells");
                if ((kit & ExerciseKit.WeightPlate) != 0) words.Add("weight plates");
            }
            if ((kit & ExerciseKit.Band) != 0) words.Add("resistance bands");
            if ((kit & (ExerciseKit.MedicineBall | ExerciseKit.SlamBall)) != 0) words.Add("a medicine ball");
            if ((kit & ExerciseKit.StepOrBench) != 0) words.Add("a step or bench");
            if ((kit & ExerciseKit.Box) != 0) words.Add("a box");
            if ((kit & ExerciseKit.SkippingRope) != 0) words.Add("a skipping rope");
            if ((kit & ExerciseKit.BattleRope) != 0) words.Add("battle ropes");
            if ((kit & ExerciseKit.CardioMachine) != 0) words.Add("cardio machines");
            if ((kit & ExerciseKit.Suspension) != 0) words.Add("a suspension trainer");
            if ((kit & ExerciseKit.Cones) != 0) words.Add("cones");
            return string.Join(" or ", words);
        }
    }
}
