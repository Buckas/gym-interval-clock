using System;
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
                        program.Mode = ExecutionMode.Sequential;
                        break;
                    }

                    case "BETWEEN":
                        inBetween = true;
                        break;

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

            return program;
        }

        /// <summary>Writes a program back out as script text.</summary>
        public static string ToScript(WorkoutProgram program)
        {
            if (program == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append("PROGRAM \"").Append((program.Name ?? "Workout").Replace("\"", "'")).Append('"').Append('\n');
            sb.Append('\n');

            if (program.Mode == ExecutionMode.Shared || program.Stations.Count == 0)
            {
                AppendTimeline(sb, program.SharedTimeline, string.Empty);
            }
            else
            {
                // Written once, up front - it is one shared timeline played between
                // every pair of stations, not a separate copy per gap, so writing it
                // more than once would double it up the next time this is parsed.
                if (!program.BetweenStationsTimeline.IsEmpty)
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
                    AppendTimeline(sb, program.TimelineForStation(i), "    ");
                    sb.Append("END STATION").Append('\n');
                    sb.Append('\n');
                }
            }

            return sb.ToString();
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
