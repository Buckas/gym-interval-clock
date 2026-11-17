using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GymClock
{
    /// <summary>One stretch of identical rounds, e.g. four rounds of 50 on, 10 off.</summary>
    public class IntervalBlock
    {
        public int Repeat = 1;
        public int Work = 45;
        public int Rest = 15;

        public IntervalBlock(int repeat, int work, int rest)
        {
            Repeat = Clamp(repeat, 1, 199);
            Work = Clamp(work, 1, 3600);
            Rest = Clamp(rest, 0, 3600);
        }

        /// <summary>The written form: 50/10x4</summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}/{1}x{2}", Work, Rest, Repeat);
        }

        public string Label
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}x {1}/{2}", Repeat, Work, Rest);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }

    /// <summary>A single round once the blocks have been flattened out.</summary>
    public class PlanRound
    {
        public int Work;
        public int Rest;
        public int BlockNumber;        // 1-based, for display
        public int PositionInBlock;    // 1-based
        public int BlockLength;
    }

    /// <summary>
    /// A session made of one or more blocks, so a workout can change pace part way
    /// through: two rounds of 45/15 then four of 50/10, rather than ten identical ones.
    ///
    /// Written form uses the same grammar as the quick presets, with blocks joined by
    /// a plus sign:
    ///
    ///     45/15x2 + 50/10x4 + 60/30x1
    ///
    /// A plan with a single block is exactly equivalent to the old uniform settings,
    /// which is what keeps existing settings files working untouched.
    /// </summary>
    public class IntervalPlan
    {
        public readonly List<IntervalBlock> Blocks = new List<IntervalBlock>();
        private List<PlanRound> _rounds;

        public static IntervalPlan Uniform(int work, int rest, int rounds)
        {
            IntervalPlan plan = new IntervalPlan();
            plan.Blocks.Add(new IntervalBlock(rounds < 1 ? 1 : rounds, work, rest));
            return plan;
        }

        /// <summary>
        /// Accepts "45/15x2 + 50/10x4". Plus signs and commas both separate blocks, and
        /// a bare "45/15" means one round. Anything unparseable is skipped rather than
        /// throwing, so a typo in a hand-edited settings file degrades gracefully.
        /// </summary>
        public static IntervalPlan Parse(string text)
        {
            IntervalPlan plan = new IntervalPlan();
            if (string.IsNullOrEmpty(text)) return plan;

            string[] parts = text.Split(new[] { '+', ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                Preset block;
                if (!Preset.TryParse(part, out block)) continue;

                plan.Blocks.Add(new IntervalBlock(block.Rounds < 1 ? 1 : block.Rounds, block.Work, block.Rest));
            }

            return plan;
        }

        public bool IsEmpty
        {
            get { return Blocks.Count == 0; }
        }

        public bool IsUniform
        {
            get { return Blocks.Count <= 1; }
        }

        /// <summary>The rounds, flattened out in order.</summary>
        public List<PlanRound> Rounds
        {
            get
            {
                if (_rounds != null) return _rounds;

                _rounds = new List<PlanRound>();

                for (int b = 0; b < Blocks.Count; b++)
                {
                    IntervalBlock block = Blocks[b];

                    for (int i = 0; i < block.Repeat; i++)
                    {
                        PlanRound round = new PlanRound();
                        round.Work = block.Work;
                        round.Rest = block.Rest;
                        round.BlockNumber = b + 1;
                        round.PositionInBlock = i + 1;
                        round.BlockLength = block.Repeat;
                        _rounds.Add(round);
                    }
                }

                return _rounds;
            }
        }

        public int RoundCount
        {
            get { return Rounds.Count; }
        }

        public int BlockCount
        {
            get { return Blocks.Count; }
        }

        /// <summary>
        /// The round at a 1-based position. Wraps around, so a continuous session
        /// (rounds=0) cycles the whole plan rather than repeating its last round.
        /// </summary>
        public PlanRound RoundAt(int round)
        {
            if (RoundCount == 0) return null;

            int index = (round - 1) % RoundCount;
            if (index < 0) index += RoundCount;
            return Rounds[index];
        }

        /// <summary>The written form, for the settings file.</summary>
        public string ToText()
        {
            List<string> parts = new List<string>();
            foreach (IntervalBlock block in Blocks) parts.Add(block.ToString());
            return string.Join("+", parts.ToArray());
        }

        /// <summary>Readable form for the display, e.g. "2x 45/15 + 4x 50/10".</summary>
        public string Summary()
        {
            if (IsEmpty) return "no rounds";

            List<string> parts = new List<string>();
            foreach (IntervalBlock block in Blocks) parts.Add(block.Label);
            return string.Join(" + ", parts.ToArray());
        }

        /// <summary>
        /// Total running time in seconds, so the settings dialog can tell you how long
        /// the session will actually take before you commit to it.
        /// </summary>
        public int TotalSeconds(int prepSeconds, bool restAfterFinalRound)
        {
            int total = prepSeconds > 0 ? prepSeconds : 0;
            List<PlanRound> rounds = Rounds;

            for (int i = 0; i < rounds.Count; i++)
            {
                total += rounds[i].Work;

                bool last = i == rounds.Count - 1;
                if (!last || restAfterFinalRound) total += rounds[i].Rest;
            }

            return total;
        }

        public static string FormatDuration(int seconds)
        {
            if (seconds < 0) seconds = 0;

            int hours = seconds / 3600;
            int minutes = (seconds % 3600) / 60;
            int remainder = seconds % 60;

            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, remainder)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, remainder);
        }
    }
}
