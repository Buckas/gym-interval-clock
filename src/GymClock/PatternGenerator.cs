using System;
using System.Collections.Generic;

namespace GymClock
{
    /// <summary>The workout shapes the Quick Setup screen can build from a few numbers.</summary>
    public enum PatternType
    {
        Standard,
        Tabata,
        Pyramid,
        ReversePyramid,
        LadderUp,
        LadderDown,
        Wave,
        AscendingWork,
        DescendingWork,
        Emom,
        E2mom,
        Custom
    }

    /// <summary>The handful of numbers every generator draws from - not every field is used by every pattern.</summary>
    public class PatternSettings
    {
        public int WorkSeconds = 30;
        public int RecoverySeconds = 15;
        public int Rounds = 8;
        public int StepSeconds = 10;     // Pyramid/Ladder/Wave/Ascending/Descending step size
        public int TotalMinutes = 10;    // EMOM/E2MOM
    }

    /// <summary>
    /// Builds a Timeline for a named pattern from a few numbers. Every generator
    /// hands back ordinary, fully-editable blocks (wrapped in a RepeatGroup where
    /// that is the natural shape) - never a locked template, exactly as the spec
    /// requires.
    /// </summary>
    public static class PatternGenerator
    {
        public static Timeline Generate(PatternType type, PatternSettings s)
        {
            Timeline timeline = new Timeline();
            foreach (TimelineItem item in Build(type, s ?? new PatternSettings())) timeline.Items.Add(item);
            return timeline;
        }

        private static List<TimelineItem> Build(PatternType type, PatternSettings s)
        {
            switch (type)
            {
                case PatternType.Standard: return Standard(s);
                case PatternType.Tabata: return Tabata(s);
                case PatternType.Pyramid: return Pyramid(s, false);
                case PatternType.ReversePyramid: return Pyramid(s, true);
                case PatternType.LadderUp: return Ladder(s, true);
                case PatternType.LadderDown: return Ladder(s, false);
                case PatternType.Wave: return Wave(s);
                case PatternType.AscendingWork: return Ramp(s, true);
                case PatternType.DescendingWork: return Ramp(s, false);
                case PatternType.Emom: return Emom(s, 1);
                case PatternType.E2mom: return Emom(s, 2);
                default: return Custom(s);
            }
        }

        private static List<TimelineItem> Standard(PatternSettings s)
        {
            RepeatGroup group = new RepeatGroup { Count = Math.Max(1, s.Rounds) };
            group.Blocks.Add(new Block(BlockType.Work, s.WorkSeconds));
            if (s.RecoverySeconds > 0) group.Blocks.Add(new Block(BlockType.Recovery, s.RecoverySeconds));
            return new List<TimelineItem> { TimelineItem.Of(group) };
        }

        private static List<TimelineItem> Tabata(PatternSettings s)
        {
            RepeatGroup group = new RepeatGroup { Count = s.Rounds > 0 ? s.Rounds : 8 };
            group.Blocks.Add(new Block(BlockType.Work, 20));
            group.Blocks.Add(new Block(BlockType.Recovery, 10));
            return new List<TimelineItem> { TimelineItem.Of(group) };
        }

        /// <summary>
        /// Steps work time up by StepSeconds each round then symmetrically back down
        /// again, e.g. 20/30/40/30/20. The reverse pyramid starts at the peak and
        /// works its way down and back up instead, e.g. 40/30/20/30/40.
        /// </summary>
        private static List<TimelineItem> Pyramid(PatternSettings s, bool reverse)
        {
            int steps = Math.Max(1, s.Rounds);
            int start = reverse ? s.WorkSeconds + (s.StepSeconds * (steps - 1)) : s.WorkSeconds;
            int step = reverse ? -s.StepSeconds : s.StepSeconds;

            List<int> up = new List<int>();
            for (int i = 0; i < steps; i++) up.Add(Math.Max(1, start + (step * i)));

            List<int> whole = new List<int>(up);
            for (int i = steps - 2; i >= 0; i--) whole.Add(up[i]);

            return BlocksFrom(whole, s.RecoverySeconds);
        }

        private static List<TimelineItem> Ladder(PatternSettings s, bool up)
        {
            int steps = Math.Max(1, s.Rounds);
            List<int> work = new List<int>();
            for (int i = 0; i < steps; i++)
            {
                int seconds = up ? s.WorkSeconds + (s.StepSeconds * i) : s.WorkSeconds - (s.StepSeconds * i);
                work.Add(Math.Max(1, seconds));
            }
            return BlocksFrom(work, s.RecoverySeconds);
        }

        /// <summary>Rises and falls in a repeating wave, e.g. 20/30/20/30, rather than settling on one peak like a pyramid.</summary>
        private static List<TimelineItem> Wave(PatternSettings s)
        {
            int cycles = Math.Max(1, s.Rounds);
            List<int> work = new List<int>();
            for (int i = 0; i < cycles; i++) work.Add(i % 2 == 0 ? s.WorkSeconds : s.WorkSeconds + s.StepSeconds);
            return BlocksFrom(work, s.RecoverySeconds);
        }

        private static List<TimelineItem> Ramp(PatternSettings s, bool ascending)
        {
            int steps = Math.Max(1, s.Rounds);
            List<int> work = new List<int>();
            for (int i = 0; i < steps; i++)
            {
                int seconds = ascending ? s.WorkSeconds + (s.StepSeconds * i) : s.WorkSeconds - (s.StepSeconds * i);
                work.Add(Math.Max(1, seconds));
            }
            return BlocksFrom(work, s.RecoverySeconds);
        }

        /// <summary>Every-minute-on-the-minute: one Work block per interval, whatever is left over becomes Recovery.</summary>
        private static List<TimelineItem> Emom(PatternSettings s, int intervalMinutes)
        {
            int intervalSeconds = Math.Max(1, intervalMinutes) * 60;
            int rounds = Math.Max(1, (Math.Max(1, s.TotalMinutes) * 60) / intervalSeconds);
            int work = Math.Min(s.WorkSeconds > 0 ? s.WorkSeconds : intervalSeconds, intervalSeconds);
            int recovery = intervalSeconds - work;

            RepeatGroup group = new RepeatGroup { Count = rounds };
            group.Blocks.Add(new Block(BlockType.Work, work));
            if (recovery > 0) group.Blocks.Add(new Block(BlockType.Recovery, recovery));
            return new List<TimelineItem> { TimelineItem.Of(group) };
        }

        private static List<TimelineItem> Custom(PatternSettings s)
        {
            return new List<TimelineItem> { TimelineItem.Of(new Block(BlockType.Work, s.WorkSeconds)) };
        }

        private static List<TimelineItem> BlocksFrom(List<int> workSeconds, int recoverySeconds)
        {
            List<TimelineItem> items = new List<TimelineItem>();
            foreach (int seconds in workSeconds)
            {
                items.Add(TimelineItem.Of(new Block(BlockType.Work, seconds)));
                if (recoverySeconds > 0) items.Add(TimelineItem.Of(new Block(BlockType.Recovery, recoverySeconds)));
            }
            return items;
        }
    }
}
