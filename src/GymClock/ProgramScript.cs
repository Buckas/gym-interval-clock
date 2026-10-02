using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GymClock
{
    /// <summary>
    /// Reads and writes the plain-text script grammar advanced users can type
    /// directly, e.g.:
    ///
    ///     BETWEEN
    ///         MOVE 15
    ///     END BETWEEN
    ///
    ///     STATION "Battle Ropes"
    ///     COLOUR #B23A78
    ///     WORKNOTE "Fast reps, both arms"
    ///     RESTNOTE "Shake it out"
    ///     REPEAT 3
    ///         WORK 50
    ///         RECOVERY 10
    ///         REST 20
    ///     END
    ///     END STATION
    ///
    /// This is also the on-disk form used for TimerSettings.ProgramScript and for
    /// exporting/importing a reusable program file, so a program built in the
    /// Quick Setup screen or the Program Builder can always be read back as text
    /// and vice versa.
    ///
    /// Named ProgramScriptFormat rather than ProgramScript so it never collides
    /// with the TimerSettings.ProgramScript field of the same short name.
    /// </summary>
    public static class ProgramScriptFormat
    {
        private static readonly Regex DurationToken = new Regex(@"^(\d+)(m|s)?$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Parses the script grammar. Unrecognised or malformed lines are skipped
        /// rather than throwing, so a typo degrades gracefully instead of losing
        /// the whole program.
        /// </summary>
        public static WorkoutProgram Parse(string text)
        {
            WorkoutProgram program = new WorkoutProgram();
            if (string.IsNullOrEmpty(text)) return program;

            StationDef currentStation = null;
            RepeatGroup currentGroup = null;   // single level only

            // LINK refers to another station by name, which may not have been
            // added yet (or may be reordered later) - resolved by name once every
            // STATION line has been seen, rather than tracked by index as we go.
            Dictionary<StationDef, string> pendingLinks = new Dictionary<StationDef, string>();

            // An explicit MODE line always wins over the implicit "seeing a
            // STATION line means Sequential" default below, regardless of which
            // comes first in the script.
            bool modeExplicit = false;

            // Between-stations content is only ever written once (see ToScript) and
            // read back inside its own BETWEEN...END BETWEEN block - never inferred
            // from position - so a script can be round-tripped through Save/Load or
            // the Script tab any number of times without doubling it up.
            bool inBetween = false;

            Action<Block> addBlock = delegate(Block block)
            {
                if (currentGroup != null) { currentGroup.Blocks.Add(block); return; }
                if (currentStation != null) { currentStation.CustomTimeline.Add(block); return; }
                if (inBetween) { program.BetweenStationsTimeline.Add(block); return; }
                program.SharedTimeline.Add(block);
            };

            foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int sp = line.IndexOf(' ');
                string command = (sp < 0 ? line : line.Substring(0, sp)).Trim().ToUpperInvariant();
                string rest = (sp < 0 ? string.Empty : line.Substring(sp + 1)).Trim();

                switch (command)
                {
                    case "PROGRAM":
                    {
                        string name = ExtractQuoted(rest);
                        if (!string.IsNullOrEmpty(name)) program.Name = name;
                        break;
                    }

                    case "STATION":
                    {
                        currentStation = new StationDef { Name = ExtractQuoted(rest), Source = TimingSource.Custom };
                        program.Stations.Add(currentStation);
                        if (!modeExplicit) program.Mode = ExecutionMode.Sequential;
                        break;
                    }

                    case "PANEL":
                    {
                        if (currentStation != null)
                        {
                            currentStation.ShowInPanel = !string.Equals(rest.Trim(), "HIDE", StringComparison.OrdinalIgnoreCase);
                        }
                        break;
                    }

                    case "COLOUR":
                    case "COLOR":
                    {
                        if (currentStation != null) currentStation.Colour = rest.Trim();
                        break;
                    }

                    case "WORKNOTE":
                    {
                        if (currentStation != null) currentStation.WorkInstruction = ExtractQuoted(rest);
                        break;
                    }

                    case "RESTNOTE":
                    {
                        if (currentStation != null) currentStation.RestInstruction = ExtractQuoted(rest);
                        break;
                    }

                    case "LINK":
                    {
                        if (currentStation != null)
                        {
                            currentStation.Source = TimingSource.Linked;
                            pendingLinks[currentStation] = ExtractQuoted(rest);
                        }
                        break;
                    }

                    case "BETWEEN":
                        inBetween = true;
                        break;

                    case "MODE":
                    {
                        switch (rest.Trim().ToUpperInvariant())
                        {
                            case "SHARED": program.Mode = ExecutionMode.Shared; modeExplicit = true; break;
                            case "SEQUENTIAL": program.Mode = ExecutionMode.Sequential; modeExplicit = true; break;
                            case "PARALLEL": program.Mode = ExecutionMode.Parallel; modeExplicit = true; break;
                            case "CIRCUIT": program.Mode = ExecutionMode.Circuit; modeExplicit = true; break;
                        }
                        break;
                    }

                    case "REPEAT":
                    {
                        int count;
                        int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);

                        // A REPEAT met while already inside one folds its blocks into
                        // the outer group instead of nesting - single level only.
                        if (currentGroup == null) currentGroup = new RepeatGroup { Count = Math.Max(1, count) };
                        break;
                    }

                    case "END":
                    {
                        if (string.Equals(rest, "STATION", StringComparison.OrdinalIgnoreCase))
                        {
                            currentStation = null;
                        }
                        else if (string.Equals(rest, "BETWEEN", StringComparison.OrdinalIgnoreCase))
                        {
                            inBetween = false;
                        }
                        else if (currentGroup != null)
                        {
                            RepeatGroup finished = currentGroup;
                            currentGroup = null;
                            if (currentStation != null) currentStation.CustomTimeline.Add(finished);
                            else if (inBetween) program.BetweenStationsTimeline.Add(finished);
                            else program.SharedTimeline.Add(finished);
                        }
                        break;
                    }

                    default:
                    {
                        BlockType type;
                        if (!Block.TryParseKeyword(command, out type)) break;

                        string body = rest;
                        string label = string.Empty;
                        int quoteAt = body.IndexOf('"');
                        if (quoteAt >= 0)
                        {
                            label = ExtractQuoted(body.Substring(quoteAt));
                            body = body.Substring(0, quoteAt).Trim();
                        }

                        Block block = new Block(type, ParseDurationText(body)) { Label = label };
                        addBlock(block);
                        break;
                    }
                }
            }

            if (pendingLinks.Count > 0)
            {
                foreach (KeyValuePair<StationDef, string> link in pendingLinks)
                {
                    int index = program.Stations.FindIndex(s =>
                        string.Equals(s.Name, link.Value, StringComparison.OrdinalIgnoreCase));
                    link.Key.LinkedStationIndex = index;
                    if (index < 0) link.Key.Source = TimingSource.ProgramDefault;
                }
            }

            return program;
        }

        /// <summary>Writes a program back out as script text.</summary>
        public static string ToScript(WorkoutProgram program)
        {
            if (program == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append("PROGRAM \"").Append((program.Name ?? "Workout").Replace("\"", "'")).Append('"').Append('\n');

            // Only Sequential/Parallel with at least one station actually have a
            // per-station timeline to write out; Parallel with none falls back to
            // the plain shared-timeline form below, same as Sequential always did.
            // Circuit mode never has per-station timelines - every station plays
            // the identical shared timeline in turn - so it is written the same
            // way as Shared-with-stations below.
            bool perStationTimelines = (program.Mode == ExecutionMode.Sequential || program.Mode == ExecutionMode.Parallel)
                && program.Stations.Count > 0;

            if (program.Mode == ExecutionMode.Sequential) sb.Append("MODE SEQUENTIAL").Append('\n');
            else if (program.Mode == ExecutionMode.Parallel) sb.Append("MODE PARALLEL").Append('\n');
            else if (program.Mode == ExecutionMode.Circuit) sb.Append("MODE CIRCUIT").Append('\n');
            else if (program.Stations.Count > 0)
            {
                // Shared timing normally stays implicit/undecorated, but a STATION
                // line below (needed for the informational panel - e.g. a rotating
                // circuit where several groups are on different stations at once)
                // would otherwise imply Sequential on reparse, so it has to be spelled out here.
                sb.Append("MODE SHARED").Append('\n');
            }

            sb.Append('\n');

            if (perStationTimelines)
            {
                // Written once, up front - it is one shared timeline played between
                // every pair of stations, not a separate copy per gap, so writing it
                // more than once would double it up the next time this is parsed.
                if (program.Mode == ExecutionMode.Sequential && !program.BetweenStationsTimeline.IsEmpty)
                {
                    sb.Append("BETWEEN").Append('\n');
                    AppendTimeline(sb, program.BetweenStationsTimeline, "    ");
                    sb.Append("END BETWEEN").Append('\n');
                    sb.Append('\n');
                }

                for (int i = 0; i < program.Stations.Count; i++)
                {
                    StationDef station = program.Stations[i];
                    sb.Append("STATION \"").Append(station.Name.Replace("\"", "'")).Append('"').Append('\n');
                    AppendStationMeta(sb, program, station, "    ");
                    AppendTimeline(sb, program.TimelineForStation(i), "    ");
                    sb.Append("END STATION").Append('\n');
                    sb.Append('\n');
                }
            }
            else
            {
                AppendTimeline(sb, program.SharedTimeline, string.Empty);

                // Circuit mode's shared "move to next station" timing is written
                // as its own BETWEEN block, same as Sequential, so it is only
                // ever played between stations rather than folded into the
                // per-station reps.
                if (program.Mode == ExecutionMode.Circuit && !program.BetweenStationsTimeline.IsEmpty)
                {
                    sb.Append('\n');
                    sb.Append("BETWEEN").Append('\n');
                    AppendTimeline(sb, program.BetweenStationsTimeline, "    ");
                    sb.Append("END BETWEEN").Append('\n');
                }

                // An informational station list - e.g. a rotating circuit where
                // several groups are on different stations at once, all following
                // this same shared timeline. No timeline of its own to write per
                // station here, since Shared/Circuit mode only ever has the one above.
                if (program.Stations.Count > 0)
                {
                    sb.Append('\n');
                    foreach (StationDef station in program.Stations)
                    {
                        sb.Append("STATION \"").Append(station.Name.Replace("\"", "'")).Append('"').Append('\n');
                        AppendStationMeta(sb, program, station, "    ");
                        sb.Append("END STATION").Append('\n');
                        sb.Append('\n');
                    }
                }
            }

            // Built up with plain '\n' throughout for simplicity; normalised to
            // Environment.NewLine here so a plain multiline TextBox (which needs
            // \r\n to break lines at all) actually displays this indented rather
            // than as one long run-on line.
            return sb.ToString().Replace("\n", Environment.NewLine);
        }

        /// <summary>Writes a station's non-timeline metadata: panel visibility, accent colour, per-phase notes, and a timing link.</summary>
        private static void AppendStationMeta(StringBuilder sb, WorkoutProgram program, StationDef station, string indent)
        {
            if (!station.ShowInPanel) sb.Append(indent).Append("PANEL HIDE").Append('\n');
            if (!string.IsNullOrEmpty(station.Colour)) sb.Append(indent).Append("COLOUR ").Append(station.Colour).Append('\n');
            if (!string.IsNullOrEmpty(station.WorkInstruction))
                sb.Append(indent).Append("WORKNOTE \"").Append(station.WorkInstruction.Replace("\"", "'")).Append('"').Append('\n');
            if (!string.IsNullOrEmpty(station.RestInstruction))
                sb.Append(indent).Append("RESTNOTE \"").Append(station.RestInstruction.Replace("\"", "'")).Append('"').Append('\n');

            if (station.Source == TimingSource.Linked && station.LinkedStationIndex >= 0
                && station.LinkedStationIndex < program.Stations.Count)
            {
                string linkedName = program.Stations[station.LinkedStationIndex].Name;
                sb.Append(indent).Append("LINK \"").Append((linkedName ?? string.Empty).Replace("\"", "'")).Append('"').Append('\n');
            }
        }

        private static void AppendTimeline(StringBuilder sb, Timeline timeline, string indent)
        {
            foreach (TimelineItem item in timeline.Items)
            {
                if (item.Block != null)
                {
                    sb.Append(indent).Append(item.Block.ToScript()).Append('\n');
                    continue;
                }

                sb.Append(indent).Append("REPEAT ").Append(item.Group.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (Block block in item.Group.Blocks) sb.Append(indent).Append("    ").Append(block.ToScript()).Append('\n');
                sb.Append(indent).Append("END").Append('\n');
            }
        }

        /// <summary>Pulls the text inside the first pair of double quotes, or the trimmed input if there is none.</summary>
        private static string ExtractQuoted(string s)
        {
            s = (s ?? string.Empty).Trim();
            if (s.Length >= 2 && s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 0) return s.Substring(1, end - 1);
            }
            return s;
        }

        /// <summary>Sums duration tokens like "20", "20s", "1m", "1m 30s" into whole seconds.</summary>
        private static int ParseDurationText(string text)
        {
            int seconds = 0;
            bool any = false;

            foreach (string tok in (text ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match m = DurationToken.Match(tok);
                if (!m.Success) continue;

                int value;
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) continue;

                string unit = m.Groups[2].Success ? m.Groups[2].Value.ToLowerInvariant() : string.Empty;
                seconds += unit == "m" ? value * 60 : value;
                any = true;
            }

            return any ? seconds : 0;
        }
    }
}
