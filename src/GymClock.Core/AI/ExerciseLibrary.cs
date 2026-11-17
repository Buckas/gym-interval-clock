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

        /// <summary>Teacher-added equipment this exercise needs (ids), on top of <see cref="Kit"/>.</summary>
        public IReadOnlyCollection<string> OtherKit { get; }

        /// <summary>True when the exercise needs any equipment at all (a wall doesn't count).</summary>
        public bool NeedsEquipment
        {
            get { return (Kit & ~ExerciseKit.Wall) != ExerciseKit.None || OtherKit.Count > 0; }
        }

        public Exercise(string name, string cue, ExerciseTag tags, ExerciseKit kit = ExerciseKit.None, bool floor = false, string id = null,
            IEnumerable<string> otherKit = null)
        {
            Name = name; Cue = cue; Tags = tags; Kit = kit; Floor = floor; Id = id ?? name;
            OtherKit = new HashSet<string>(otherKit ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
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
        public HashSet<string> AnyOfOtherKit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // teacher-added equipment asked for
        public List<string> OtherKitNames = new List<string>();                                       // ...and their names, for the reply
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
            return Parse(request, ExerciseCatalog.Current.CustomEquipment);
        }

        public static ExerciseQuery Parse(string request, IEnumerable<CustomEquipment> customEquipment)
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

                // Equipment the teacher added ("sled", "agility ladder"), singular or plural.
                foreach (CustomEquipment item in customEquipment ?? Enumerable.Empty<CustomEquipment>())
                {
                    string name = (item.Name ?? string.Empty).Trim().ToLowerInvariant();
                    if (name.Length < 2) continue;
                    string stem = name.EndsWith("s", StringComparison.Ordinal) ? name.Substring(0, name.Length - 1) : name;
                    if (Regex.IsMatch(text, @"\b" + Regex.Escape(stem) + @"(s|es)?\b"))
                    {
                        q.AnyOfOtherKit.Add(item.Id);
                        q.OtherKitNames.Add(item.Name.Trim());
                    }
                }
            }
            return q;
        }

        // ------------------------------------------------------------------ search

        public static List<Exercise> Find(ExerciseQuery q)
        {
            CatalogSnapshot c = ExerciseCatalog.Current;
            return All.Where(e => Matches(e, q, relaxed: false, c.OwnedKit, c.OwnedOther)).ToList();
        }

        private static bool Matches(Exercise e, ExerciseQuery q, bool relaxed, ExerciseKit? owned, ICollection<string> ownedOther)
        {
            bool askedForThis = (e.Kit & q.AnyOfKit) != ExerciseKit.None || e.OtherKit.Any(k => q.AnyOfOtherKit.Contains(k));

            // Only suggest equipment the gym has (set in the Exercises window) - unless it was asked for by name.
            if (owned.HasValue && !askedForThis)
            {
                ExerciseKit needs = e.Kit & ~ExerciseKit.Wall;
                if (needs != ExerciseKit.None && (needs & owned.Value) == ExerciseKit.None) return false;
                if (e.OtherKit.Count > 0 && !e.OtherKit.Any(k => ownedOther != null && ownedOther.Contains(k))) return false;
            }

            if (q.NoEquipment && e.NeedsEquipment) return false;
            bool askedForEquipment = q.AnyOfKit != ExerciseKit.None || q.AnyOfOtherKit.Count > 0;
            if (askedForEquipment && !askedForThis) return false;
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
            return Pick(q, recentlySuggested, random, catalog.Visible, catalog.OwnedKit, out relaxed, catalog.OwnedOther);
        }

        public static List<Exercise> Pick(ExerciseQuery q, IEnumerable<string> recentlySuggested, Random random,
            IReadOnlyList<Exercise> exercises, ExerciseKit? owned, out bool relaxed, ICollection<string> ownedOther = null)
        {
            HashSet<string> recent = new HashSet<string>(recentlySuggested ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            relaxed = false;

            List<Exercise> pool = exercises.Where(e => Matches(e, q, false, owned, ownedOther)).ToList();
            if (pool.Count < q.Count)
            {
                // Not enough exact matches: keep the must-haves (equipment, type) and loosen the rest.
                List<Exercise> extra = exercises.Where(e => Matches(e, q, true, owned, ownedOther) && !pool.Contains(e)).ToList();
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

        // ------------------------------------------------------------------ lookup by name

        // Words that don't identify an exercise on their own ("Exercise Bike Intervals" is the bike,
        // "Step-Ups" is the step), so a teacher's "Spin bike" or "box step ups" still finds them.
        private static readonly HashSet<string> WeakWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "exercise", "interval", "machine", "trainer", "run", "running", "on", "the", "of", "with", "and",
            "a", "up", "station", "spot", "set", "hold", "work", "drill"
        };

        private static readonly HashSet<string> GenericActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "push", "pull", "press", "jump", "raise", "row", "lunge", "squat", "curl", "lift", "throw", "carry", "crawl", "dip", "swing", "slam"
        };

        private static List<string> NameTokens(string name, bool strongOnly)
        {
            List<string> tokens = new List<string>();
            foreach (string raw in Regex.Split((name ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+"))
            {
                if (raw.Length == 0) continue;
                string t = raw.Length > 3 && raw.EndsWith("s", StringComparison.Ordinal) && !raw.EndsWith("ss", StringComparison.Ordinal)
                    ? raw.Substring(0, raw.Length - 1) : raw;
                if (t == "ups") t = "up";
                if (strongOnly && WeakWords.Contains(t)) continue;
                tokens.Add(t);
            }
            return tokens;
        }

        /// <summary>
        /// The library exercise a station name refers to ("box step ups" -> Step-Ups, "Spin bike" ->
        /// Exercise Bike Intervals), or null when nothing is a safe match. Used for coaching cues.
        /// </summary>
        public static Exercise FindByName(string stationName)
        {
            return FindByName(stationName, All);
        }

        public static Exercise FindByName(string stationName, IReadOnlyList<Exercise> exercises)
        {
            string wanted = string.Join(" ", NameTokens(stationName, false));
            if (wanted.Length == 0 || exercises == null) return null;

            Exercise exact = exercises.FirstOrDefault(e => string.Join(" ", NameTokens(e.Name, false)) == wanted);
            if (exact != null) return exact;

            HashSet<string> station = new HashSet<string>(NameTokens(stationName, true));
            if (station.Count == 0) return null;

            Exercise best = null;
            int bestScore = int.MinValue;
            foreach (Exercise e in exercises)
            {
                List<string> mine = NameTokens(e.Name, true).Distinct().ToList();
                if (mine.Count == 0) continue;
                int overlap = mine.Count(station.Contains);
                if (overlap == 0) continue;

                // Safe only when the exercise's own key words are all there ("bike" in "Spin bike"), or the
                // station's words are all in a short exercise name - not "pull ups" -> "Band Pull-Aparts".
                bool coversExercise = overlap == mine.Count;

                // A name that is only an action ("Push Ups", "Lunges") must not claim "Sled push" or "Walking lunge
                // with plate" - for those every word the teacher wrote has to be part of the exercise's name.
                if (coversExercise && mine.All(GenericActions.Contains) && station.Any(t => !mine.Contains(t))) coversExercise = false;
                bool coversStation = overlap == station.Count && overlap * 2 >= mine.Count + 1;
                if (!coversExercise && !coversStation) continue;

                int score = overlap * 10 - (mine.Count - overlap) * 3 - Math.Max(0, station.Count - overlap);
                if (score > bestScore) { best = e; bestScore = score; }
            }
            return best;
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
            else if (q.AnyOfKit != ExerciseKit.None || q.OtherKitNames.Count > 0)
            {
                List<string> kit = new List<string>();
                if (q.AnyOfKit != ExerciseKit.None) kit.Add(KitWords(q.AnyOfKit));
                kit.AddRange(q.OtherKitNames.Select(n => "a " + n.ToLowerInvariant()));
                text += " using " + string.Join(" or ", kit);
            }
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
