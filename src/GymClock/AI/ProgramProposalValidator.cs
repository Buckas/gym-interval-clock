using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GymClock.AI
{
    public sealed class ProgramProposalValidator
    {
        private static readonly Regex DurationToken = new Regex(@"^(\d+)(m|s|min|mins|minute|minutes|sec|secs|second|seconds)?$", RegexOptions.IgnoreCase);

        public ProgramValidationResult Validate(string script)
        {
            List<ProgramValidationIssue> issues = new List<ProgramValidationIssue>();
            string text = script ?? string.Empty;

            string[] sourceLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            bool hasProgram = false;
            bool hasMode = false;
            ExecutionMode mode = ExecutionMode.Shared;

            bool inStation = false;
            bool inBetween = false;
            bool inRepeat = false;
            int currentStationTimedCommands = 0;
            int currentStationStartLine = 0;

            bool hasTimedCommand = false;
            int stationCount = 0;
            List<string> stationNames = new List<string>();
            HashSet<string> stationNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> usedColours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int duplicateColourCount = 0;
            int totalTimedSeconds = 0;

            for (int i = 0; i < sourceLines.Length; i++)
            {
                string raw = sourceLines[i];
                string line = raw.Trim();
                int lineNo = i + 1;

                if (line.Length == 0 || line.StartsWith("#")) continue;

                int sp = line.IndexOf(' ');
                string command = (sp < 0 ? line : line.Substring(0, sp)).Trim().ToUpperInvariant();
                string rest = (sp < 0 ? string.Empty : line.Substring(sp + 1)).Trim();

                if (Block.TryParseKeyword(command, out _))
                {
                    int quoteAt = rest.IndexOf('"');
                    string body = quoteAt >= 0 ? rest.Substring(0, quoteAt).Trim() : rest;
                    int seconds = ParseDurationText(body);
                    if (seconds <= 0)
                    {
                        issues.Add(new ProgramValidationIssue(
                            ValidationSeverity.Error,
                            lineNo,
                            command + " duration must be greater than zero."));
                    }
                    else
                    {
                        hasTimedCommand = true;
                        totalTimedSeconds += seconds;
                        if (inStation)
                        {
                            currentStationTimedCommands++;
                        }
                    }

                    continue;
                }

                switch (command)
                {
                    case "PROGRAM":
                    {
                        string name = ExtractQuoted(rest);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "PROGRAM requires a non-empty quoted name."));
                        }
                        else
                        {
                            hasProgram = true;
                        }
                        break;
                    }

                    case "MODE":
                    {
                        if (string.IsNullOrWhiteSpace(rest))
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "MODE requires a value: SHARED, SEQUENTIAL, PARALLEL or CIRCUIT."));
                            break;
                        }

                        switch (rest.Trim().ToUpperInvariant())
                        {
                            case "SHARED": mode = ExecutionMode.Shared; hasMode = true; break;
                            case "SEQUENTIAL": mode = ExecutionMode.Sequential; hasMode = true; break;
                            case "PARALLEL": mode = ExecutionMode.Parallel; hasMode = true; break;
                            case "CIRCUIT": mode = ExecutionMode.Circuit; hasMode = true; break;
                            default:
                                issues.Add(new ProgramValidationIssue(
                                    ValidationSeverity.Error,
                                    lineNo,
                                    "Unknown MODE '" + rest + "'."));
                                break;
                        }
                        break;
                    }

                    case "STATION":
                    {
                        if (inStation)
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "Nested STATION blocks are not supported. Missing END STATION?"));
                        }

                        string stationName = ExtractQuoted(rest);
                        if (string.IsNullOrWhiteSpace(stationName))
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "STATION requires a non-empty quoted name."));
                        }
                        else
                        {
                            stationCount++;
                            stationNames.Add(stationName);
                            stationNameSet.Add(stationName);
                        }

                        inStation = true;
                        currentStationTimedCommands = 0;
                        currentStationStartLine = lineNo;
                        break;
                    }

                    case "BETWEEN":
                    {
                        if (inBetween)
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "Nested BETWEEN blocks are not supported."));
                        }
                        inBetween = true;
                        break;
                    }

                    case "REPEAT":
                    {
                        int count;
                        if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "REPEAT requires an integer count."));
                        }
                        else if (count <= 0)
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "REPEAT count must be greater than zero."));
                        }

                        if (inRepeat)
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "Nested REPEAT groups are not supported."));
                        }
                        inRepeat = true;
                        break;
                    }

                    case "END":
                    {
                        if (string.Equals(rest, "STATION", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!inStation)
                            {
                                issues.Add(new ProgramValidationIssue(
                                    ValidationSeverity.Error,
                                    lineNo,
                                    "END STATION has no matching STATION."));
                            }
                            else if ((mode == ExecutionMode.Sequential || mode == ExecutionMode.Parallel)
                                     && currentStationTimedCommands <= 0)
                            {
                                issues.Add(new ProgramValidationIssue(
                                    ValidationSeverity.Error,
                                    currentStationStartLine,
                                    "STATION must contain at least one timed block (WORK/REST/RECOVERY/MOVE) before END STATION."));
                            }
                            inStation = false;
                            currentStationTimedCommands = 0;
                            currentStationStartLine = 0;
                            break;
                        }

                        if (string.Equals(rest, "BETWEEN", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!inBetween)
                            {
                                issues.Add(new ProgramValidationIssue(
                                    ValidationSeverity.Error,
                                    lineNo,
                                    "END BETWEEN has no matching BETWEEN."));
                            }
                            inBetween = false;
                            break;
                        }

                        if (rest.Length == 0)
                        {
                            if (!inRepeat)
                            {
                                issues.Add(new ProgramValidationIssue(
                                    ValidationSeverity.Error,
                                    lineNo,
                                    "END has no matching REPEAT."));
                            }
                            inRepeat = false;
                            break;
                        }

                        issues.Add(new ProgramValidationIssue(
                            ValidationSeverity.Error,
                            lineNo,
                            "Unknown END form 'END " + rest + "'."));
                        break;
                    }

                    case "PANEL":
                    {
                        if (rest.Length > 0 && !string.Equals(rest, "HIDE", StringComparison.OrdinalIgnoreCase))
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "PANEL supports only 'PANEL HIDE'."));
                        }
                        break;
                    }

                    case "COLOUR":
                    case "COLOR":
                    {
                        string colour = rest.Trim();
                        if (!IsValidColour(colour))
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                "Invalid colour '" + rest + "'. Use #RRGGBB or a known .NET colour name."));
                        }
                        else
                        {
                            if (!usedColours.Add(colour)) duplicateColourCount++;
                        }
                        break;
                    }

                    case "WORKNOTE":
                    case "RESTNOTE":
                    case "LINK":
                    {
                        string quoted = ExtractQuoted(rest);
                        if (string.IsNullOrWhiteSpace(quoted))
                        {
                            issues.Add(new ProgramValidationIssue(
                                ValidationSeverity.Error,
                                lineNo,
                                command + " requires a non-empty quoted value."));
                        }
                        break;
                    }

                    default:
                    {
                        issues.Add(new ProgramValidationIssue(
                            ValidationSeverity.Error,
                            lineNo,
                            "Unknown command '" + command + "'."));
                        break;
                    }
                }
            }

            if (!hasProgram)
            {
                issues.Add(new ProgramValidationIssue(ValidationSeverity.Error, null, "Missing PROGRAM declaration."));
            }

            if (!hasMode)
            {
                issues.Add(new ProgramValidationIssue(ValidationSeverity.Error, null, "Missing MODE declaration."));
            }

            if (inRepeat)
            {
                issues.Add(new ProgramValidationIssue(ValidationSeverity.Error, null, "Unclosed REPEAT block. Missing END."));
            }

            if (inStation)
            {
                issues.Add(new ProgramValidationIssue(ValidationSeverity.Error, null, "Unclosed STATION block. Missing END STATION."));
            }

            if (inBetween)
            {
                issues.Add(new ProgramValidationIssue(ValidationSeverity.Error, null, "Unclosed BETWEEN block. Missing END BETWEEN."));
            }

            if (duplicateColourCount > 0)
            {
                issues.Add(new ProgramValidationIssue(
                    ValidationSeverity.Warning,
                    null,
                    "Duplicate station colours found. Consider unique colours for clearer display."));
            }

            if (stationCount == 0)
            {
                issues.Add(new ProgramValidationIssue(
                    ValidationSeverity.Warning,
                    null,
                    "Program has no stations. This is legal but unusual for station-based sessions."));
            }

            if (totalTimedSeconds > 0 && totalTimedSeconds < 120)
            {
                issues.Add(new ProgramValidationIssue(
                    ValidationSeverity.Warning,
                    null,
                    "Very short timed content (< 2 minutes)."));
            }

            if (totalTimedSeconds > 7200)
            {
                issues.Add(new ProgramValidationIssue(
                    ValidationSeverity.Warning,
                    null,
                    "Very long timed content (> 2 hours)."));
            }

            WorkoutProgram parsed = null;
            if (!issues.Any(x => x.Severity == ValidationSeverity.Error))
            {
                try
                {
                    parsed = ProgramScriptFormat.Parse(text);
                }
                catch (Exception ex)
                {
                    issues.Add(new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Parser failure: " + ex.Message));
                }

                if (parsed == null)
                {
                    issues.Add(new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Parser produced no program model."));
                }
                else
                {
                    // model-level sanity checks
                    bool executable = false;
                    if (parsed.Mode == ExecutionMode.Parallel)
                    {
                        List<List<ResolvedBlock>> lanes = parsed.BuildParallelSequences();
                        executable = lanes.Any(l => l != null && l.Any(b => b.Block != null && b.Block.Seconds > 0));
                    }
                    else
                    {
                        List<RuntimeBlock> sequence = parsed.BuildRuntimeSequence();
                        executable = sequence.Any(b => b.Block != null && b.Block.Seconds > 0);
                    }

                    if (!executable)
                    {
                        issues.Add(new ProgramValidationIssue(
                            ValidationSeverity.Error,
                            null,
                            "Program has no executable timed content."));
                    }

                    if (parsed.Stations.Count > 0
                        && parsed.Stations.All(s => string.IsNullOrWhiteSpace(s.WorkInstruction) && string.IsNullOrWhiteSpace(s.RestInstruction)))
                    {
                        issues.Add(new ProgramValidationIssue(
                            ValidationSeverity.Warning,
                            null,
                            "No coaching notes are set on stations."));
                    }
                }
            }

            return new ProgramValidationResult
            {
                Script = text,
                ParsedProgram = parsed,
                Issues = issues
            };
        }

        private static string ExtractQuoted(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            int first = s.IndexOf('"');
            if (first < 0) return string.Empty;
            int second = s.IndexOf('"', first + 1);
            if (second < 0) return string.Empty;
            return s.Substring(first + 1, second - first - 1);
        }

        private static int ParseDurationText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            int seconds = 0;
            foreach (string token in text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match m = DurationToken.Match(token.Trim());
                if (!m.Success) return 0;

                int n;
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return 0;
                string u = m.Groups[2].Value.ToLowerInvariant();
                if (u == "m" || u == "min" || u == "mins" || u == "minute" || u == "minutes")
                {
                    seconds += n * 60;
                }
                else
                {
                    seconds += n;
                }
            }

            return seconds;
        }

        private static bool IsValidColour(string colour)
        {
            if (string.IsNullOrWhiteSpace(colour)) return false;

            string value = colour.Trim();

            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                return Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$");
            }

            try
            {
                System.Drawing.Color c = System.Drawing.Color.FromName(value);
                return c.IsKnownColor || c.IsNamedColor || c.IsSystemColor;
            }
            catch
            {
                return false;
            }
        }
    }
}
