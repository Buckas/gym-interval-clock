using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using System.Text;

namespace GymClock
{
    /// <summary>
    /// The kind of timed event a Block represents. Drives both the default wording
    /// and the default colour shown on the TV display - see
    /// TimerSettings.ColourFor and Block.DefaultWord.
    /// </summary>
    public enum BlockType
    {
        Prepare,
        Work,
        Recovery,
        Rest,
        Move,
        Countdown,
        WaterBreak,
        Instruction,
        Custom
    }

    /// <summary>
    /// One timed event in a Timeline - a work interval, a rest break, a move
    /// between stations, and so on. Everything the TV display needs to show it
    /// and everything the script grammar needs to write it back out.
    /// </summary>
    public class Block
    {
        // Plain auto-properties rather than fields - PropertyGrid (the Program
        // Builder's Properties panel) only ever surfaces properties, and these
        // read and assign exactly like fields everywhere else in the codebase.
        public BlockType Type { get; set; } = BlockType.Work;
        public int Seconds { get; set; } = 30;

        /// <summary>Overrides the default wording for this block's type, e.g. "SPRINT" instead of "WORK".</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Optional smaller line shown alongside the label, e.g. "Grab a drink".</summary>
        public string Announcement { get; set; } = string.Empty;

        /// <summary>Overrides the default colour for this block's type. "#RRGGBB" or a .NET colour name.</summary>
        [Editor(typeof(ColourUITypeEditor), typeof(UITypeEditor))]
        public string Colour { get; set; } = string.Empty;

        /// <summary>Overrides the default sound cue for this block's type. Blank = the type's usual cue.</summary>
        public string Sound { get; set; } = string.Empty;

        public Block()
        {
        }

        public Block(BlockType type, int seconds)
        {
            Type = type;
            Seconds = Math.Max(0, seconds);
        }

        public Block Clone()
        {
            return new Block(Type, Seconds)
            {
                Label = Label,
                Announcement = Announcement,
                Colour = Colour,
                Sound = Sound
            };
        }

        /// <summary>The default upper-case wording for a block type, used unless a block's own Label overrides it.</summary>
        public static string DefaultWord(BlockType type)
        {
            switch (type)
            {
                case BlockType.Prepare: return "GET READY";
                case BlockType.Work: return "WORK";
                case BlockType.Recovery: return "RECOVERY";
                case BlockType.Rest: return "REST";
                case BlockType.Move: return "MOVE";
                case BlockType.Countdown: return "COUNTDOWN";
                case BlockType.WaterBreak: return "WATER BREAK";
                case BlockType.Instruction: return "INSTRUCTION";
                default: return "GO";
            }
        }

        /// <summary>The word actually shown for this block - its own Label if it has one, otherwise the type default.</summary>
        public string Word
        {
            get { return string.IsNullOrEmpty(Label) ? DefaultWord(Type) : Label.ToUpperInvariant(); }
        }

        /// <summary>The script keyword for a block type: WORK, RECOVERY, REST, MOVE, PREPARE, ...</summary>
        public static string Keyword(BlockType type)
        {
            switch (type)
            {
                case BlockType.Prepare: return "PREPARE";
                case BlockType.Work: return "WORK";
                case BlockType.Recovery: return "RECOVERY";
                case BlockType.Rest: return "REST";
                case BlockType.Move: return "MOVE";
                case BlockType.Countdown: return "COUNTDOWN";
                case BlockType.WaterBreak: return "WATER";
                case BlockType.Instruction: return "INSTRUCTION";
                default: return "CUSTOM";
            }
        }

        public static bool TryParseKeyword(string word, out BlockType type)
        {
            switch ((word ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "PREPARE": type = BlockType.Prepare; return true;
                case "WORK": type = BlockType.Work; return true;
                case "RECOVERY": type = BlockType.Recovery; return true;
                case "REST": type = BlockType.Rest; return true;
                case "MOVE": type = BlockType.Move; return true;
                case "COUNTDOWN": type = BlockType.Countdown; return true;
                case "WATER":
                case "WATERBREAK": type = BlockType.WaterBreak; return true;
                case "INSTRUCTION": type = BlockType.Instruction; return true;
                case "CUSTOM": type = BlockType.Custom; return true;
                default: type = BlockType.Work; return false;
            }
        }

        /// <summary>The script line for this block, e.g. WORK 30 or WORK 1m 30s "Sprint".</summary>
        public string ToScript()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Keyword(Type)).Append(' ').Append(FormatDuration(Seconds));
            if (!string.IsNullOrEmpty(Label)) sb.Append(" \"").Append(Label.Replace("\"", "'")).Append('"');
            return sb.ToString();
        }

        /// <summary>Compact duration text for the script grammar: 20s, 1m, 1m 30s.</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "0s";
            if (seconds % 60 == 0) return (seconds / 60).ToString(CultureInfo.InvariantCulture) + "m";
            if (seconds < 60) return seconds.ToString(CultureInfo.InvariantCulture) + "s";
            return (seconds / 60).ToString(CultureInfo.InvariantCulture) + "m "
                + (seconds % 60).ToString(CultureInfo.InvariantCulture) + "s";
        }
    }

    /// <summary>
    /// A group of blocks that repeat, e.g. REPEAT 3 { WORK 50, RECOVERY 10 } END.
    /// Single level only - a repeat group cannot itself contain another repeat
    /// group, matching every example in the spec.
    /// </summary>
    public class RepeatGroup
    {
        public int Count { get; set; } = 1;
        public List<Block> Blocks = new List<Block>();

        public RepeatGroup Clone()
        {
            RepeatGroup group = new RepeatGroup { Count = Count };
            foreach (Block block in Blocks) group.Blocks.Add(block.Clone());
            return group;
        }

        /// <summary>Readable form, e.g. "3x 50/10".</summary>
        public string Summary()
        {
            if (Blocks.Count == 0) return Count.ToString(CultureInfo.InvariantCulture) + "x (empty)";

            List<string> parts = new List<string>();
            foreach (Block block in Blocks) parts.Add(Block.FormatDuration(block.Seconds));
            return Count.ToString(CultureInfo.InvariantCulture) + "x " + string.Join("/", parts.ToArray());
        }

        public int TotalSeconds()
        {
            int sum = 0;
            foreach (Block block in Blocks) sum += block.Seconds;
            return sum * Math.Max(1, Count);
        }
    }

    /// <summary>
    /// One item in a Timeline: either a bare Block or a RepeatGroup. Exactly one
    /// of Block/Group is set - there is no plain base class hierarchy here since
    /// a switch on which reference is non-null reads just as clearly for two cases.
    /// </summary>
    public class TimelineItem
    {
        public Block Block;
        public RepeatGroup Group;

        public static TimelineItem Of(Block block) { return new TimelineItem { Block = block }; }
        public static TimelineItem Of(RepeatGroup group) { return new TimelineItem { Group = group }; }

        public TimelineItem Clone()
        {
            return Block != null ? Of(Block.Clone()) : Of(Group.Clone());
        }
    }

    /// <summary>
    /// A single block once repeat groups have been flattened out, with enough
    /// context to describe "round 2 of 3" style progress - the same idea as
    /// PlanRound in IntervalPlan, generalised to any block type.
    /// </summary>
    public class ResolvedBlock
    {
        public Block Block;
        public int GroupNumber;        // 1-based index of the enclosing repeat group, 0 = not in a group
        public int GroupCount;         // how many repeat groups the timeline has in total
        public int PositionInGroup;    // 1-based position within the current pass of the group
        public int GroupLength;        // total blocks per pass of the group
        public int RepeatIndex;        // 1-based - which pass through the group this is
        public int RepeatTotal;        // how many passes the group runs for
    }

    /// <summary>
    /// An ordered sequence of blocks and repeat groups - a station's own workout,
    /// or the one shared sequence used in Shared Timing mode. Rest and Move are
    /// ordinary blocks here, so they can sit anywhere rather than being their own
    /// dedicated settings.
    /// </summary>
    public class Timeline
    {
        public List<TimelineItem> Items = new List<TimelineItem>();
        private List<ResolvedBlock> _resolved;

        public bool IsEmpty { get { return Items.Count == 0; } }

        public void Add(Block block) { Items.Add(TimelineItem.Of(block)); _resolved = null; }
        public void Add(RepeatGroup group) { Items.Add(TimelineItem.Of(group)); _resolved = null; }

        public Timeline Clone()
        {
            Timeline timeline = new Timeline();
            foreach (TimelineItem item in Items) timeline.Items.Add(item.Clone());
            return timeline;
        }

        /// <summary>
        /// Flattens repeat groups into an ordered block list - the same idea as
        /// IntervalPlan.Rounds. Built once and cached until Items is next changed
        /// through Add; edit Items directly (e.g. from the builder) and call
        /// Invalidate() afterwards.
        /// </summary>
        public List<ResolvedBlock> Resolve()
        {
            if (_resolved != null) return _resolved;
            _resolved = new List<ResolvedBlock>();

            int groupCount = 0;
            foreach (TimelineItem item in Items) if (item.Group != null) groupCount++;

            int groupNumber = 0;
            foreach (TimelineItem item in Items)
            {
                if (item.Block != null)
                {
                    _resolved.Add(new ResolvedBlock { Block = item.Block });
                    continue;
                }

                RepeatGroup group = item.Group;
                groupNumber++;
                int reps = Math.Max(1, group.Count);

                for (int r = 0; r < reps; r++)
                {
                    for (int p = 0; p < group.Blocks.Count; p++)
                    {
                        _resolved.Add(new ResolvedBlock
                        {
                            Block = group.Blocks[p],
                            GroupNumber = groupNumber,
                            GroupCount = groupCount,
                            PositionInGroup = p + 1,
                            GroupLength = group.Blocks.Count,
                            RepeatIndex = r + 1,
                            RepeatTotal = reps
                        });
                    }
                }
            }

            return _resolved;
        }

        /// <summary>Clears the cached flattened list - call after editing Items directly.</summary>
        public void Invalidate() { _resolved = null; }

        public int TotalSeconds()
        {
            int total = 0;
            foreach (ResolvedBlock rb in Resolve()) total += rb.Block.Seconds;
            return total;
        }

        /// <summary>Readable form for previews, e.g. "3x WORK 50/RECOVERY 10 + REST 60".</summary>
        public string Summary()
        {
            if (IsEmpty) return "empty";

            List<string> parts = new List<string>();
            foreach (TimelineItem item in Items)
            {
                if (item.Block != null) parts.Add(item.Block.Word + " " + Block.FormatDuration(item.Block.Seconds));
                else parts.Add(item.Group.Summary());
            }
            return string.Join(" + ", parts.ToArray());
        }
    }

    /// <summary>Where a station gets its timing from.</summary>
    public enum TimingSource
    {
        ProgramDefault,
        Linked,
        Custom
    }

    /// <summary>
    /// One station in a program - a name, optional look and per-phase
    /// instructions, and where its timing comes from.
    /// </summary>
    public class StationDef
    {
        public string Name { get; set; } = string.Empty;

        [Editor(typeof(ColourUITypeEditor), typeof(UITypeEditor))]
        public string Colour { get; set; } = string.Empty;
        public string WorkInstruction { get; set; } = string.Empty;
        public string RestInstruction { get; set; } = string.Empty;

        /// <summary>Whether this station appears in the on-screen station panel at all - independent of the panel's own show/hide setting.</summary>
        public bool ShowInPanel { get; set; } = true;

        public TimingSource Source { get; set; } = TimingSource.ProgramDefault;
        public int LinkedStationIndex { get; set; } = -1;
        public Timeline CustomTimeline = new Timeline();

        public StationDef Clone()
        {
            return new StationDef
            {
                Name = Name,
                Colour = Colour,
                WorkInstruction = WorkInstruction,
                RestInstruction = RestInstruction,
                ShowInPanel = ShowInPanel,
                Source = Source,
                LinkedStationIndex = LinkedStationIndex,
                CustomTimeline = CustomTimeline.Clone()
            };
        }
    }

    /// <summary>How the program's stations relate to the timing.</summary>
    public enum ExecutionMode
    {
        Shared,
        Sequential,

        /// <summary>Every station runs its own pattern simultaneously and independently - see ParallelClock.</summary>
        Parallel,

        /// <summary>
        /// One shared timing set (e.g. 4x WORK 45/REST 15) is used identically by every
        /// station at once - a different group works at each station in unison - and
        /// when it finishes, everybody moves on to the next station together. The
        /// on-screen station list is purely informational (no highlighting, since
        /// every station is active at the same time), while the header counts
        /// "STATION x OF y" as the whole class advances.
        /// </summary>
        Circuit
    }

    /// <summary>Where a station is right now, located by elapsed time rather than by a stepped index - see ParallelClock.</summary>
    public class StationPosition
    {
        public int Index = -1;
        public ResolvedBlock Block;
        public double RemainingSeconds;
        public bool Finished;
    }

    /// <summary>
    /// Locates a Parallel-mode station's current block by elapsed time rather
    /// than by stepping through events one at a time. This is what lets every
    /// station share one clock (MainForm's _phaseClock) instead of needing its
    /// own independent stopwatch and state machine: "what block is station i in
    /// right now" becomes a pure function of how long the run has been going,
    /// so pausing, resuming and resetting the one shared clock does the same for
    /// every station automatically, with no drift between them.
    /// </summary>
    public static class ParallelClock
    {
        public static StationPosition Locate(List<ResolvedBlock> blocks, double elapsedSeconds, bool continuous)
        {
            StationPosition position = new StationPosition();
            if (blocks == null || blocks.Count == 0) { position.Finished = true; return position; }

            int total = 0;
            foreach (ResolvedBlock rb in blocks) total += Math.Max(0, rb.Block.Seconds);
            if (total <= 0) { position.Finished = true; return position; }

            double t = elapsedSeconds;
            if (continuous)
            {
                t = t % total;
                if (t < 0) t += total;
            }
            else if (t >= total)
            {
                position.Index = blocks.Count - 1;
                position.Block = blocks[position.Index];
                position.RemainingSeconds = 0;
                position.Finished = true;
                return position;
            }
            else if (t < 0)
            {
                t = 0;
            }

            double accumulated = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                double length = Math.Max(0, blocks[i].Block.Seconds);
                if (t < accumulated + length)
                {
                    position.Index = i;
                    position.Block = blocks[i];
                    position.RemainingSeconds = (accumulated + length) - t;
                    return position;
                }
                accumulated += length;
            }

            // Floating-point edge case landing exactly on the total - treat as the last block, just finished.
            position.Index = blocks.Count - 1;
            position.Block = blocks[position.Index];
            position.RemainingSeconds = 0;
            position.Finished = !continuous;
            return position;
        }
    }

    /// <summary>
    /// One block in the flattened run order for the TV display, tagged with
    /// which station (if any) is active while it plays. Built by
    /// WorkoutProgram.BuildRuntimeSequence - this is what MainForm actually
    /// steps through, for both Shared and Sequential execution modes.
    /// </summary>
    public class RuntimeBlock
    {
        public Block Block;
        public int StationIndex = -1;   // -1 = not tied to one station (Shared mode, or a between-station block)
        public int GroupNumber, GroupCount, PositionInGroup, GroupLength, RepeatIndex, RepeatTotal;

        public static RuntimeBlock From(ResolvedBlock rb, int stationIndex)
        {
            return new RuntimeBlock
            {
                Block = rb.Block,
                StationIndex = stationIndex,
                GroupNumber = rb.GroupNumber,
                GroupCount = rb.GroupCount,
                PositionInGroup = rb.PositionInGroup,
                GroupLength = rb.GroupLength,
                RepeatIndex = rb.RepeatIndex,
                RepeatTotal = rb.RepeatTotal
            };
        }
    }

    /// <summary>
    /// A complete, reusable workout: an execution mode, a shared timeline and/or
    /// a list of stations, plus whatever sits between stations in Sequential mode.
    /// </summary>
    public class WorkoutProgram
    {
        public string Name = "Workout";
        public ExecutionMode Mode = ExecutionMode.Shared;
        public Timeline SharedTimeline = new Timeline();
        public List<StationDef> Stations = new List<StationDef>();

        /// <summary>Standalone Rest/Move blocks (and repeat groups) that play between every pair of stations in Sequential mode.</summary>
        public Timeline BetweenStationsTimeline = new Timeline();

        /// <summary>When true, the whole run order loops forever instead of finishing - the same idea as "rounds=0" today.</summary>
        public bool Continuous;

        public WorkoutProgram Clone()
        {
            WorkoutProgram program = new WorkoutProgram
            {
                Name = Name,
                Mode = Mode,
                SharedTimeline = SharedTimeline.Clone(),
                Continuous = Continuous
            };
            foreach (StationDef station in Stations) program.Stations.Add(station.Clone());
            program.BetweenStationsTimeline = BetweenStationsTimeline.Clone();
            return program;
        }

        /// <summary>The timeline actually in force for a station, resolving Linked/Custom/ProgramDefault.</summary>
        public Timeline TimelineForStation(int index)
        {
            return TimelineForStation(index, null);
        }

        private Timeline TimelineForStation(int index, HashSet<int> visited)
        {
            if (index < 0 || index >= Stations.Count) return SharedTimeline;
            StationDef station = Stations[index];

            switch (station.Source)
            {
                case TimingSource.Custom:
                    return station.CustomTimeline.IsEmpty ? SharedTimeline : station.CustomTimeline;

                case TimingSource.Linked:
                    if (station.LinkedStationIndex >= 0 && station.LinkedStationIndex < Stations.Count
                        && station.LinkedStationIndex != index)
                    {
                        if (visited == null) visited = new HashSet<int> { index };
                        if (!visited.Add(station.LinkedStationIndex)) return SharedTimeline;
                        return TimelineForStation(station.LinkedStationIndex, visited);
                    }
                    return SharedTimeline;

                default:
                    return SharedTimeline;
            }
        }

        /// <summary>
        /// The flattened run order the TV display steps through - every station's
        /// timeline back to back with the between-station blocks in Sequential
        /// mode, or just the shared timeline in Shared mode. Rebuild whenever the
        /// program changes; nothing here is cached.
        /// </summary>
        public List<RuntimeBlock> BuildRuntimeSequence()
        {
            List<RuntimeBlock> sequence = new List<RuntimeBlock>();

            if (Mode == ExecutionMode.Sequential && Stations.Count > 0)
            {
                for (int i = 0; i < Stations.Count; i++)
                {
                    foreach (ResolvedBlock rb in TimelineForStation(i).Resolve()) sequence.Add(RuntimeBlock.From(rb, i));

                    if (i < Stations.Count - 1)
                    {
                        foreach (ResolvedBlock rb in BetweenStationsTimeline.Resolve()) sequence.Add(RuntimeBlock.From(rb, -1));
                    }
                }
            }
            else if (Mode == ExecutionMode.Circuit && Stations.Count > 0)
            {
                // The exact same shared timing (reps, work/rest) plays once per
                // station in turn, each pass tagged with that station's index so
                // the header can count "station x of y" - then the shared MOVE
                // between every pair of stations, same as Sequential's between
                // block, so the whole class advances together.
                for (int i = 0; i < Stations.Count; i++)
                {
                    foreach (ResolvedBlock rb in SharedTimeline.Resolve()) sequence.Add(RuntimeBlock.From(rb, i));

                    if (i < Stations.Count - 1)
                    {
                        foreach (ResolvedBlock rb in BetweenStationsTimeline.Resolve()) sequence.Add(RuntimeBlock.From(rb, -1));
                    }
                }
            }
            else
            {
                foreach (ResolvedBlock rb in SharedTimeline.Resolve()) sequence.Add(RuntimeBlock.From(rb, -1));
            }

            return sequence;
        }

        /// <summary>
        /// One resolved block list per station, for Parallel mode - the
        /// equivalent of BuildRuntimeSequence() for a mode where every station
        /// runs independently instead of one after another. Falls back to a
        /// single "station" wrapping SharedTimeline when none have been added
        /// yet, the same way BuildRuntimeSequence() falls back for Sequential.
        /// </summary>
        public List<List<ResolvedBlock>> BuildParallelSequences()
        {
            List<List<ResolvedBlock>> result = new List<List<ResolvedBlock>>();

            if (Stations.Count == 0)
            {
                result.Add(SharedTimeline.Resolve());
                return result;
            }

            for (int i = 0; i < Stations.Count; i++) result.Add(TimelineForStation(i).Resolve());
            return result;
        }
    }
}
