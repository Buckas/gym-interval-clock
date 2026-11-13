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

        public Exercise(string name, string cue, ExerciseTag tags, ExerciseKit kit = ExerciseKit.None, bool floor = false)
        {
            Name = name; Cue = cue; Tags = tags; Kit = kit; Floor = floor;
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
    /// A built-in, curated set of school-gym exercises with coaching cues, searchable by type,
    /// body area, equipment and floor/no-floor. Answers "I need 5 resistance exercises with basic
    /// weights" style requests instantly and reliably - no AI needed, nothing invented.
    /// </summary>
    public static class ExerciseLibrary
    {
        private const ExerciseTag C = ExerciseTag.Cardio, S = ExerciseTag.Strength, Co = ExerciseTag.Core,
            P = ExerciseTag.Plyometric, M = ExerciseTag.Mobility, U = ExerciseTag.Upper, L = ExerciseTag.Lower,
            F = ExerciseTag.FullBody, Lo = ExerciseTag.LowImpact;

        public static readonly IReadOnlyList<Exercise> All = new List<Exercise>
        {
            // ---- bodyweight cardio / plyometric
            new Exercise("Jumping Jacks", "Land softly, arms fully overhead", C | F),
            new Exercise("Star Jumps", "Explode up into a star shape, land with soft knees", C | P | F),
            new Exercise("High Knees", "Drive knees to hip height, quick feet", C | L),
            new Exercise("Butt Kicks", "Heels to glutes, stay light on your toes", C | L | Lo),
            new Exercise("Mountain Climbers", "Hips level with shoulders, drive knees to chest", C | Co | F, floor: true),
            new Exercise("Burpees", "Chest to floor, jump and clap overhead", C | P | F, floor: true),
            new Exercise("Squat Jumps", "Sit back into the squat, explode up, land softly", P | L | C),
            new Exercise("Tuck Jumps", "Drive knees to chest, land quietly", P | L | C),
            new Exercise("Skater Hops", "Bound side to side, land on one leg and hold balance", C | P | L),
            new Exercise("Lateral Shuffles", "Stay low, don't cross your feet", C | L | Lo),
            new Exercise("Shuttle Runs", "Touch the line, turn fast, accelerate out", C | L),
            new Exercise("Fast Feet", "Quick small steps on the balls of your feet", C | L | Lo),
            new Exercise("Bear Crawl", "Knees just off the floor, opposite hand and foot", C | Co | F, floor: true),
            new Exercise("Inchworms", "Walk hands out to plank, walk feet back in", M | Co | F, floor: true),
            new Exercise("Plank Jacks", "Hold a strong plank, jump feet in and out", C | Co, floor: true),
            new Exercise("Seal Jacks", "Clap arms wide then together, keep a steady rhythm", C | U | Lo),
            new Exercise("Marching on the Spot", "Lift knees high, swing the arms", C | Lo | L),
            new Exercise("Step Jacks", "Step one foot out at a time, arms overhead", C | Lo | F),
            new Exercise("Broad Jumps", "Swing arms, jump forward, stick the landing", P | L),
            new Exercise("Lunge Jumps", "Switch legs in the air, back knee just off the floor", P | L | C),
            new Exercise("Sprint on the Spot", "Pump the arms, fast feet, stay tall", C | L),
            new Exercise("Shadow Boxing", "Light feet, fast punches, keep guard up", C | U | Lo),

            // ---- bodyweight strength / core
            new Exercise("Push Ups", "Body in a straight line, chest to the floor", S | U | Co, floor: true),
            new Exercise("Knee Push Ups", "Straight line from knees to shoulders", S | U | Lo, floor: true),
            new Exercise("Wide Push Ups", "Hands wider than shoulders, elbows out", S | U, floor: true),
            new Exercise("Tricep Dips (on floor)", "Fingers to heels, lower slowly, push up", S | U, floor: true),
            new Exercise("Bodyweight Squats", "Chest up, knees over toes, sit back", S | L | Lo),
            new Exercise("Sumo Squats", "Wide stance, toes out, sit straight down", S | L | Lo),
            new Exercise("Lunges", "Long step, back knee just above the floor", S | L),
            new Exercise("Reverse Lunges", "Step back, front knee stays over the ankle", S | L | Lo),
            new Exercise("Side Lunges", "Sit into one hip, other leg straight", S | L | Lo),
            new Exercise("Glute Bridges", "Squeeze glutes at the top, ribs down", S | L | Lo, floor: true),
            new Exercise("Single-Leg Glute Bridges", "Hips level, push through the heel", S | L, floor: true),
            new Exercise("Calf Raises", "Rise high onto your toes, lower slowly", S | L | Lo),
            new Exercise("Wall Sit", "Thighs parallel to the floor, back flat on the wall", S | L | Lo, ExerciseKit.Wall),
            new Exercise("Plank", "Elbows under shoulders, squeeze glutes and abs", Co | Lo, floor: true),
            new Exercise("Side Plank", "Hips high, body in one line", Co | Lo, floor: true),
            new Exercise("Plank Shoulder Taps", "Keep hips still while you tap", Co | U, floor: true),
            new Exercise("Dead Bugs", "Lower back pressed into the floor, move slowly", Co | Lo, floor: true),
            new Exercise("Bird Dogs", "Reach long, keep hips level", Co | Lo | M, floor: true),
            new Exercise("Crunches", "Lift shoulders, chin off the chest", Co | Lo, floor: true),
            new Exercise("Bicycle Crunches", "Elbow towards opposite knee, slow and controlled", Co, floor: true),
            new Exercise("Russian Twists", "Lean back slightly, rotate from the ribs", Co, floor: true),
            new Exercise("Leg Raises", "Lower back down, lower legs slowly", Co, floor: true),
            new Exercise("Flutter Kicks", "Small fast kicks, lower back pressed down", Co, floor: true),
            new Exercise("V-Hold", "Balance on your sit bones, chest up", Co, floor: true),
            new Exercise("Superman Hold", "Lift arms and legs, look at the floor", Co | Lo, floor: true),
            new Exercise("Hollow Hold", "Lower back glued to the floor, arms by ears", Co, floor: true),
            new Exercise("Sit-Ups", "Feet anchored, roll up with control", Co, floor: true),
            new Exercise("Bear Hold", "Knees an inch off the floor, back flat", Co | Lo, floor: true),

            // ---- mobility / warm-up / cool-down
            new Exercise("World's Greatest Stretch", "Lunge, elbow to instep, rotate and reach", M | F | Lo, floor: true),
            new Exercise("Cat-Cow", "Move slowly through the whole spine", M | Co | Lo, floor: true),
            new Exercise("Arm Circles", "Small to big circles, both directions", M | U | Lo),
            new Exercise("Leg Swings", "Hold a wall, swing front to back then side to side", M | L | Lo, ExerciseKit.Wall),
            new Exercise("Hip Openers", "Big slow knee circles, stay tall", M | L | Lo),
            new Exercise("Hamstring Walkouts", "Soft knees, walk hands down and back", M | L | Lo, floor: true),
            new Exercise("Child's Pose", "Sit back to heels, breathe slowly", M | Lo, floor: true),

            // ---- dumbbells (basic weights)
            new Exercise("Goblet Squats", "Hold the weight at your chest, elbows inside knees", S | L, ExerciseKit.Dumbbell | ExerciseKit.Kettlebell),
            new Exercise("Dumbbell Romanian Deadlifts", "Soft knees, push hips back, flat back", S | L, ExerciseKit.Dumbbell),
            new Exercise("Dumbbell Shoulder Press", "Ribs down, press straight overhead", S | U, ExerciseKit.Dumbbell),
            new Exercise("Dumbbell Bent-Over Rows", "Flat back, pull elbows to your hips", S | U, ExerciseKit.Dumbbell),
            new Exercise("Dumbbell Bicep Curls", "Elbows tucked in, no swinging", S | U | Lo, ExerciseKit.Dumbbell),
            new Exercise("Dumbbell Floor Press", "Elbows at 45 degrees, press up together", S | U, ExerciseKit.Dumbbell, floor: true),
            new Exercise("Dumbbell Reverse Lunges", "Weights by your sides, stay tall", S | L, ExerciseKit.Dumbbell),
            new Exercise("Dumbbell Step-Ups", "Whole foot on the step, drive through the heel", S | L, ExerciseKit.Dumbbell | ExerciseKit.StepOrBench),
            new Exercise("Dumbbell Thrusters", "Squat down, drive up and press overhead in one move", S | F | C, ExerciseKit.Dumbbell),
            new Exercise("Renegade Rows", "Wide feet, row without twisting the hips", S | U | Co, ExerciseKit.Dumbbell, floor: true),
            new Exercise("Dumbbell Lateral Raises", "Slight bend in the elbows, lift to shoulder height", S | U | Lo, ExerciseKit.Dumbbell),
            new Exercise("Weighted Calf Raises", "Pause at the top, lower slowly", S | L | Lo, ExerciseKit.Dumbbell),
            new Exercise("Farmer's Carry", "Stand tall, shoulders back, walk slowly", S | Co | F, ExerciseKit.Dumbbell | ExerciseKit.Kettlebell),
            new Exercise("Weighted Glute Bridges", "Weight on the hips, squeeze at the top", S | L, ExerciseKit.Dumbbell | ExerciseKit.WeightPlate, floor: true),
            new Exercise("Dumbbell Russian Twists", "Hold one weight, rotate from the ribs", Co | S, ExerciseKit.Dumbbell | ExerciseKit.WeightPlate | ExerciseKit.MedicineBall, floor: true),

            // ---- kettlebells
            new Exercise("Kettlebell Swings", "Hinge at the hips, snap them forward, arms relaxed", S | C | F, ExerciseKit.Kettlebell),
            new Exercise("Kettlebell Deadlifts", "Bell between the feet, flat back, stand tall", S | L, ExerciseKit.Kettlebell),
            new Exercise("Kettlebell Halos", "Circle the bell close around your head", S | U | M, ExerciseKit.Kettlebell),
            new Exercise("Kettlebell Bird Dogs", "Drag the bell under your body without twisting", Co | S, ExerciseKit.Kettlebell, floor: true),

            // ---- weight plates
            new Exercise("Plate Front Raises", "Arms straight, lift to eye level", S | U | Lo, ExerciseKit.WeightPlate),
            new Exercise("Plate Overhead Lunges", "Plate locked out overhead, core tight", S | L | Co, ExerciseKit.WeightPlate),

            // ---- resistance bands
            new Exercise("Band Pull-Aparts", "Arms straight, squeeze shoulder blades together", S | U | Lo, ExerciseKit.Band),
            new Exercise("Banded Squats", "Band above the knees, push knees out", S | L | Lo, ExerciseKit.Band),
            new Exercise("Band Rows", "Band around a post, pull elbows back", S | U | Lo, ExerciseKit.Band),
            new Exercise("Banded Lateral Walks", "Stay low, keep tension in the band", S | L | Lo, ExerciseKit.Band),
            new Exercise("Band Chest Press", "Band behind your back, press forward", S | U | Lo, ExerciseKit.Band),

            // ---- medicine ball / slam ball
            new Exercise("Medicine Ball Slams", "Reach tall, slam hard, catch on the bounce", P | F | C, ExerciseKit.MedicineBall | ExerciseKit.SlamBall),
            new Exercise("Wall Balls", "Squat deep, throw to the target, catch and repeat", P | F | C, ExerciseKit.MedicineBall | ExerciseKit.Wall),
            new Exercise("Medicine Ball Chest Pass (wall)", "Explosive push, catch with soft hands", P | U, ExerciseKit.MedicineBall | ExerciseKit.Wall),

            // ---- step / bench / box
            new Exercise("Step-Ups", "Whole foot on the step, swap lead legs", S | L | C, ExerciseKit.StepOrBench),
            new Exercise("Bench Dips", "Shoulders down, elbows point back", S | U, ExerciseKit.StepOrBench),
            new Exercise("Incline Push Ups", "Hands on the bench, body in a straight line", S | U | Lo, ExerciseKit.StepOrBench),
            new Exercise("Bulgarian Split Squats", "Back foot on the bench, drop the back knee", S | L, ExerciseKit.StepOrBench),
            new Exercise("Box Jumps", "Swing arms, land softly, step down", P | L | C, ExerciseKit.Box),

            // ---- other equipment
            new Exercise("Skipping", "Small jumps, turn the rope from the wrists", C | Lo, ExerciseKit.SkippingRope),
            new Exercise("Double Unders", "Jump a little higher, fast wrists", C | P, ExerciseKit.SkippingRope),
            new Exercise("Battle Rope Waves", "Athletic stance, fast alternating arms", C | U | Co, ExerciseKit.BattleRope),
            new Exercise("Battle Rope Slams", "Lift both arms high, slam together", C | P | U, ExerciseKit.BattleRope),
            new Exercise("Exercise Bike Intervals", "High cadence on work, easy spin on rest", C | L | Lo, ExerciseKit.CardioMachine),
            new Exercise("Treadmill Run", "Run at about 80% effort, walk to recover", C | L, ExerciseKit.CardioMachine),
            new Exercise("Rowing Machine", "Legs, body, arms - then arms, body, legs", C | F | Lo, ExerciseKit.CardioMachine),
            new Exercise("Elliptical Trainer", "Stand tall, push and pull with the arms", C | F | Lo, ExerciseKit.CardioMachine),
            new Exercise("TRX Rows", "Body straight, pull chest to hands", S | U | Lo, ExerciseKit.Suspension),
            new Exercise("TRX Squats", "Hold the handles, sit back deep", S | L | Lo, ExerciseKit.Suspension),
            new Exercise("Cone Sprints", "Sprint to the cone, back-pedal to the start", C | L, ExerciseKit.Cones),
            new Exercise("Agility Weave", "Weave through the cones, low centre of gravity", C | L, ExerciseKit.Cones),
        };

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
            return All.Where(e => Matches(e, q, relaxed: false)).ToList();
        }

        private static bool Matches(Exercise e, ExerciseQuery q, bool relaxed)
        {
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
            HashSet<string> recent = new HashSet<string>(recentlySuggested ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            relaxed = false;

            List<Exercise> pool = All.Where(e => Matches(e, q, false)).ToList();
            if (pool.Count < q.Count)
            {
                // Not enough exact matches: keep the must-haves (equipment, type) and loosen the rest.
                List<Exercise> extra = All.Where(e => Matches(e, q, true) && !pool.Contains(e)).ToList();
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
