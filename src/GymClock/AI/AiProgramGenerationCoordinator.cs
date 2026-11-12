using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GymClock.AI
{
    public sealed class AiProgramGenerationCoordinator
    {
        private readonly IAiProgramService _aiService;
        private readonly ProgramProposalValidator _validator;
        private readonly AiAssistantOptions _options;

        public AiProgramGenerationCoordinator(
            IAiProgramService aiService,
            ProgramProposalValidator validator,
            AiAssistantOptions options)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<AiProgramProposal> GenerateAsync(
            string userRequest,
            string currentValidatedScript,
            ConversationState conversation,
            CancellationToken cancellationToken = default)
        {
            string request = (userRequest ?? string.Empty).Trim();
            if (request.Length == 0) throw new ArgumentException("User request must not be empty.", nameof(userRequest));
            request = NormalizeStructuredRequest(request);
            string requestBeforeAugmentation = request;

            ConversationState state = conversation ?? new ConversationState();
            state.AddUser(request);

            if (TryBuildRevisionStationPolicyClarification(requestBeforeAugmentation, currentValidatedScript, out string revisionClarification))
            {
                state.AddAssistant(revisionClarification);
                return new AiProgramProposal
                {
                    UserRequest = requestBeforeAugmentation,
                    Script = currentValidatedScript ?? string.Empty,
                    ParsedProgram = null,
                    Issues = Array.Empty<ProgramValidationIssue>(),
                    RequiresClarification = true,
                    AssistantMessage = revisionClarification
                };
            }

            request = AugmentRevisionRequestWithBaselineConstraints(requestBeforeAugmentation, currentValidatedScript);

            if (TryBuildModeClarification(request, currentValidatedScript, out string clarification))
            {
                state.AddAssistant(clarification);
                return new AiProgramProposal
                {
                    UserRequest = request,
                    Script = currentValidatedScript ?? string.Empty,
                    ParsedProgram = null,
                    Issues = Array.Empty<ProgramValidationIssue>(),
                    RequiresClarification = true,
                    AssistantMessage = clarification
                };
            }

            int maxRepairs = Math.Max(0, _options.MaximumAutomaticRepairAttempts);
            int maxGenerations = Math.Max(1, _options.MaximumGenerationAttempts);

            string generationRequest = request;
            ProgramValidationResult lastValidation = null;

            for (int generationAttempt = 0; generationAttempt < maxGenerations; generationAttempt++)
            {
                IReadOnlyList<AiChatMessage> generationMessages = BuildGenerationMessages(generationRequest, currentValidatedScript, state);
                string first = await _aiService.GenerateTextAsync(generationMessages, cancellationToken).ConfigureAwait(false);
                string firstScript = CleanMarkdownFences(first);
                firstScript = NormalizeInlineCommands(firstScript);
                firstScript = NormalizeCommonScriptIssues(firstScript);
                firstScript = BalanceProgramAndStationQuotes(firstScript);
                ProgramValidationResult firstValidation = _validator.Validate(firstScript);
                firstValidation = ApplyRequestRequirementIssues(request, firstValidation);
                firstValidation = EnforceExplicitExerciseRequirements(request, firstValidation);
                firstValidation = EnforceMinimumScriptShape(firstValidation);
                firstValidation = EnforceRevisionBaselineShape(request, currentValidatedScript, firstValidation);

                if (firstValidation.IsValid)
                {
                    state.AddAssistant("Proposed a valid program script.");
                    return ToProposal(request, firstValidation);
                }

                ProgramValidationResult repairValidation = firstValidation;
                string repaired = firstScript;

                for (int repairAttempt = 0; repairAttempt < maxRepairs; repairAttempt++)
                {
                    IReadOnlyList<AiChatMessage> repairMessages = BuildRepairMessages(request, repaired, repairValidation.Issues, state);
                    repaired = await _aiService.GenerateTextAsync(repairMessages, cancellationToken).ConfigureAwait(false);
                    repaired = CleanMarkdownFences(repaired);
                    repaired = NormalizeInlineCommands(repaired);
                    repaired = NormalizeCommonScriptIssues(repaired);
                    repaired = BalanceProgramAndStationQuotes(repaired);
                    repairValidation = _validator.Validate(repaired);
                    repairValidation = ApplyRequestRequirementIssues(request, repairValidation);
                    repairValidation = EnforceExplicitExerciseRequirements(request, repairValidation);
                    repairValidation = EnforceMinimumScriptShape(repairValidation);
                    repairValidation = EnforceRevisionBaselineShape(request, currentValidatedScript, repairValidation);

                    if (repairValidation.IsValid)
                    {
                        state.AddAssistant("Repaired proposal into a valid program script.");
                        return ToProposal(request, repairValidation);
                    }
                }

                lastValidation = repairValidation;
                if (generationAttempt < maxGenerations - 1)
                {
                    state.AddAssistant("Previous proposal invalid; retrying generation.");
                    generationRequest = BuildRetryGenerationRequest(request, lastValidation.Issues, generationAttempt + 2, maxGenerations);
                }
            }

            if (TryBuildDeterministicTemplateFallback(request, out ProgramValidationResult fallbackValidation))
            {
                state.AddAssistant("Applied deterministic fallback template after invalid AI retries.");
                return ToProposal(request, fallbackValidation);
            }

            state.AddAssistant("Proposed script remains invalid after automatic retries.");
            return ToProposal(request, lastValidation ?? new ProgramValidationResult
            {
                Script = string.Empty,
                ParsedProgram = null,
                Issues = new[]
                {
                    new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Unable to generate a valid script after automatic retries.")
                }
            });
        }

        private static string BuildRetryGenerationRequest(
            string originalRequest,
            IReadOnlyList<ProgramValidationIssue> issues,
            int attemptNumber,
            int maxAttempts)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(originalRequest);
            sb.AppendLine();
            sb.AppendLine("Retry generation attempt " + attemptNumber + " of " + maxAttempts + ".");
            sb.AppendLine("Return only valid Gym Interval Clock script.");
            sb.AppendLine("Do not include markdown fences or explanation.");
            sb.AppendLine("Correct these errors:");

            foreach (ProgramValidationIssue issue in (issues ?? Array.Empty<ProgramValidationIssue>())
                .Where(i => i.Severity == ValidationSeverity.Error)
                .Take(10))
            {
                string line = issue.LineNumber.HasValue ? "Line " + issue.LineNumber.Value + ": " : string.Empty;
                sb.AppendLine("- " + line + issue.Message);
            }

            return sb.ToString().Trim();
        }

        private static string NormalizeStructuredRequest(string request)
        {
            string text = (request ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = text.Split('\n');

            List<string> baseLines = new List<string>();
            string modeDirective = null;
            string stationDirective = null;
            string durationDirective = null;
            string explicitDirective = null;

            foreach (string raw in lines)
            {
                string line = (raw ?? string.Empty).Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("Use MODE ", StringComparison.OrdinalIgnoreCase))
                {
                    modeDirective = line;
                    continue;
                }

                if (line.StartsWith("Use at least ", StringComparison.OrdinalIgnoreCase))
                {
                    stationDirective = line;
                    continue;
                }

                if (line.StartsWith("Keep total duration ", StringComparison.OrdinalIgnoreCase))
                {
                    durationDirective = line;
                    continue;
                }

                if (line.StartsWith("Include explicit cardio and resistance exercises", StringComparison.OrdinalIgnoreCase))
                {
                    explicitDirective = line;
                    continue;
                }

                baseLines.Add(line);
            }

            List<string> merged = new List<string>();
            merged.AddRange(baseLines);
            if (!string.IsNullOrWhiteSpace(modeDirective)) merged.Add(modeDirective);
            if (!string.IsNullOrWhiteSpace(stationDirective)) merged.Add(stationDirective);
            if (!string.IsNullOrWhiteSpace(durationDirective)) merged.Add(durationDirective);
            if (!string.IsNullOrWhiteSpace(explicitDirective)) merged.Add(explicitDirective);

            return string.Join(Environment.NewLine, merged).Trim();
        }

        private string AugmentRevisionRequestWithBaselineConstraints(string request, string currentValidatedScript)
        {
            string text = (request ?? string.Empty).Trim();
            if (text.Length == 0) return text;
            if (string.IsNullOrWhiteSpace(currentValidatedScript)) return text;

            bool looksLikeRevision = LooksLikeRevisionRequest(text)
                || text.IndexOf("current program", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("current script", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("existing program", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksLikeRevision) return text;

            ProgramValidationResult baselineValidation = _validator.Validate(currentValidatedScript);
            WorkoutProgram baseline = baselineValidation?.ParsedProgram;
            if (baseline == null) return text;

            List<string> additions = new List<string>();

            bool wantsModePreserved = text.IndexOf("without changing mode", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("keep mode", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("same mode", StringComparison.OrdinalIgnoreCase) >= 0;

            if (wantsModePreserved && !ContainsModeHint(text))
            {
                additions.Add("Use MODE " + baseline.Mode.ToString().ToUpperInvariant() + ".");
            }

            bool hasStationDirective = TryExtractRequestedCount(text, "station", out _);
            if (!hasStationDirective && baseline.Stations != null && baseline.Stations.Count > 0)
            {
                additions.Add("Use " + baseline.Stations.Count + " stations.");
            }

            if (additions.Count == 0) return text;

            StringBuilder merged = new StringBuilder(text);
            foreach (string add in additions)
            {
                merged.AppendLine();
                merged.Append(add);
            }

            return NormalizeStructuredRequest(merged.ToString());
        }

        private static string NormalizeInlineCommands(string script)
        {
            string text = script ?? string.Empty;
            if (text.Length == 0) return text;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> result = new List<string>(lines.Length * 2);

            foreach (string sourceLine in lines)
            {
                string line = sourceLine ?? string.Empty;
                if (line.Trim().Length == 0)
                {
                    result.Add(line);
                    continue;
                }

                bool inQuotes = false;
                int segmentStart = 0;

                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == '"') inQuotes = !inQuotes;
                    if (inQuotes) continue;

                    if (!IsCommandStart(line, i, out int commandLength)) continue;
                    if (i == 0)
                    {
                        i += Math.Max(0, commandLength - 1);
                        continue;
                    }

                    bool prevIsWhitespace = char.IsWhiteSpace(line[i - 1]);
                    bool segmentHasContent = line.Substring(segmentStart, i - segmentStart).Trim().Length > 0;
                    if (!prevIsWhitespace || !segmentHasContent)
                    {
                        i += Math.Max(0, commandLength - 1);
                        continue;
                    }

                    string chunk = line.Substring(segmentStart, i - segmentStart).TrimEnd();
                    if (chunk.Length > 0) result.Add(chunk);
                    segmentStart = i;
                    i += Math.Max(0, commandLength - 1);
                }

                string tail = line.Substring(segmentStart).TrimEnd();
                if (tail.Length > 0) result.Add(tail);
            }

            return string.Join(Environment.NewLine, result);
        }

        private static readonly string[] InlineCommands =
        {
            "END STATION", "END BETWEEN", "PROGRAM", "MODE", "BETWEEN", "STATION", "COLOUR", "COLOR", "REPEAT",
            "WORK", "REST", "RECOVERY", "MOVE", "PREPARE", "COUNTDOWN", "WATER", "INSTRUCTION", "END"
        };

        private static bool IsCommandStart(string line, int index, out int commandLength)
        {
            commandLength = 0;
            if (index < 0 || index >= line.Length) return false;
            if (index > 0 && !char.IsWhiteSpace(line[index - 1])) return false;

            foreach (string command in InlineCommands)
            {
                if (index + command.Length > line.Length) continue;
                if (!line.Substring(index, command.Length).Equals(command, StringComparison.OrdinalIgnoreCase)) continue;

                int end = index + command.Length;
                if (end < line.Length && !char.IsWhiteSpace(line[end])) continue;

                commandLength = command.Length;
                return true;
            }

            return false;
        }

        private static string BalanceProgramAndStationQuotes(string script)
        {
            string[] lines = (script ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("PROGRAM ", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("STATION ", StringComparison.OrdinalIgnoreCase))
                {
                    int quoteCount = line.Count(c => c == '"');
                    if (quoteCount % 2 != 0)
                    {
                        lines[i] = line + "\"";
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static bool TryBuildExerciseSourceClarification(string request, out string clarification)
        {
            clarification = string.Empty;
            string text = (request ?? string.Empty).Trim();
            if (text.Length == 0) return false;

            // If the request already contains structured mode/duration directives from
            // the form clarification flow, do not restart clarification here.
            // This prevents clarification loops where "2" is interpreted by the form's
            // mode picker after the coordinator injects a second, unrelated question.
            if (Regex.IsMatch(text, @"^\s*Use\s+MODE\s+", RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                return false;
            }

            bool mentionsCardioOrResistance =
                text.IndexOf("cardio", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("resistance", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("strength", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!mentionsCardioOrResistance) return false;
            if (ContainsAutoExerciseDirective(text)) return false;
            if (HasLikelyExerciseList(text)) return false;

            clarification = "Before generation, choose exercise source:\n"
                + "1) I will provide exercise names\n"
                + "2) AI should choose concrete exercises\n"
                + "Reply with 1 or 2.\n"
                + "Quick replies: 1, 2";
            return true;
        }

        private static bool ContainsAutoExerciseDirective(string text)
        {
            return text.IndexOf("ai choose", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("you choose", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("choose exercises", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("make up exercises", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasLikelyExerciseList(string text)
        {
            string[] exerciseHints =
            {
                "burpee", "squat", "lunge", "push-up", "push up", "plank", "row", "bike", "cycle",
                "treadmill", "jump rope", "mountain climber", "kettlebell", "deadlift", "press", "slam"
            };

            int hintCount = exerciseHints.Count(h => text.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);
            if (hintCount >= 2) return true;

            string[] commaParts = text.Split(',');
            return commaParts.Length >= 3 && hintCount >= 1;
        }

        private bool TryBuildRevisionStationPolicyClarification(string request, string currentValidatedScript, out string clarification)
        {
            clarification = string.Empty;

            string text = (request ?? string.Empty).Trim();
            if (text.Length == 0) return false;
            if (string.IsNullOrWhiteSpace(currentValidatedScript)) return false;

            bool looksLikeRevision = LooksLikeRevisionRequest(text)
                || text.IndexOf("current program", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("current script", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("existing program", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!looksLikeRevision) return false;

            if (ContainsStationPolicyDirective(text)) return false;

            ProgramValidationResult baselineValidation = _validator.Validate(currentValidatedScript);
            if (baselineValidation?.ParsedProgram == null) return false;

            bool durationChangeIntent = text.IndexOf("duration", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("minute", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("minutes", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("longer", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("shorter", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("increase total", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("reduce total", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("decrease total", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!durationChangeIntent) return false;

            if (TryExtractRequestedCount(text, "station", out _)) return false;
            if (HasExplicitStationEditIntent(text)) return false;

            clarification =
                "To revise duration safely, choose station-change policy:" + Environment.NewLine
                + "1) Keep station count and station details unchanged." + Environment.NewLine
                + "2) Keep station count, allow station detail updates." + Environment.NewLine
                + "3) Allow station count and detail changes if needed." + Environment.NewLine
                + "Reply with 1, 2, or 3." + Environment.NewLine
                + "Quick replies: 1, 2, 3";
            return true;
        }

        private static bool ContainsStationPolicyDirective(string request)
        {
            string text = request ?? string.Empty;
            return text.IndexOf("Preserve station count", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("station count and station details unchanged", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("allow station detail updates", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("allow station count and detail changes", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasExplicitStationEditIntent(string request)
        {
            string text = request ?? string.Empty;
            return text.IndexOf("replace station", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("rename station", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("change station", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("update station", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("remove station", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("add station", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("change exercise", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("update exercise", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("replace exercise", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ProgramValidationResult EnforceExplicitExerciseRequirements(string request, ProgramValidationResult validation)
        {
            if (validation == null) return new ProgramValidationResult();

            string req = request ?? string.Empty;
            bool requiresExplicit =
                req.IndexOf("explicit cardio", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("cardio and resistance", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("combination of cardio and resistance", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!requiresExplicit || string.IsNullOrWhiteSpace(validation.Script)) return validation;

            MatchCollection stationMatches = Regex.Matches(
                validation.Script,
                "^\\s*STATION\\s+\"([^\"]+)\"",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            if (stationMatches.Count == 0) return validation;

            List<ProgramValidationIssue> issues = validation.Issues.ToList();
            int genericStationCount = stationMatches
                .Cast<Match>()
                .Count(m => IsGenericStationName(m.Groups[1].Value));

            if (genericStationCount > 0)
            {
                issues.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Request requires explicit exercise names. Replace generic station names like 'Cardio' or 'Resistance' with concrete exercises."));
            }

            return new ProgramValidationResult
            {
                Script = validation.Script,
                ParsedProgram = validation.ParsedProgram,
                Issues = issues
            };
        }

        private static ProgramValidationResult EnforceRevisionBaselineShape(
            string request,
            string currentValidatedScript,
            ProgramValidationResult validation)
        {
            if (validation == null) return new ProgramValidationResult();
            if (string.IsNullOrWhiteSpace(currentValidatedScript)) return validation;
            if (validation.ParsedProgram == null) return validation;

            string req = request ?? string.Empty;
            bool looksLikeRevision = LooksLikeRevisionRequest(req)
                || req.IndexOf("current program", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("current script", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("existing program", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!looksLikeRevision) return validation;

            ProgramValidationResult baselineValidation = new ProgramProposalValidator().Validate(currentValidatedScript);
            WorkoutProgram baseline = baselineValidation?.ParsedProgram;
            if (baseline == null) return validation;

            List<ProgramValidationIssue> extra = new List<ProgramValidationIssue>();

            bool wantsModePreserved = req.IndexOf("without changing mode", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("keep mode", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("same mode", StringComparison.OrdinalIgnoreCase) >= 0;

            if (wantsModePreserved && validation.ParsedProgram.Mode != baseline.Mode)
            {
                extra.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Revision changed MODE even though the request says not to change mode."));
            }

            bool stationCountExplicitlyRequested = TryExtractRequestedCount(req, "station", out _);
            if (!stationCountExplicitlyRequested
                && baseline.Stations != null
                && validation.ParsedProgram.Stations != null
                && baseline.Stations.Count > 0
                && validation.ParsedProgram.Stations.Count != baseline.Stations.Count)
            {
                extra.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Revision changed station count from baseline without an explicit station-count request."));
            }

            if (extra.Count == 0) return validation;

            return new ProgramValidationResult
            {
                Script = validation.Script,
                ParsedProgram = validation.ParsedProgram,
                Issues = validation.Issues.Concat(extra).ToArray()
            };
        }

        private static bool IsGenericStationName(string stationName)
        {
            string value = (stationName ?? string.Empty).Trim();
            if (value.Length == 0) return true;

            return Regex.IsMatch(value, "^(cardio|resistance|strength|station\\s*\\d*|exercise\\s*\\d*)$", RegexOptions.IgnoreCase)
                || value.Equals("station", StringComparison.OrdinalIgnoreCase)
                || value.Equals("cardio station", StringComparison.OrdinalIgnoreCase)
                || value.Equals("resistance station", StringComparison.OrdinalIgnoreCase);
        }

        private static ProgramValidationResult EnforceMinimumScriptShape(ProgramValidationResult validation)
        {
            if (validation == null) return new ProgramValidationResult();

            string script = validation.Script ?? string.Empty;
            bool hasProgram = Regex.IsMatch(script, @"^\s*PROGRAM\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            bool hasMode = Regex.IsMatch(script, @"^\s*MODE\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            bool hasStation = Regex.IsMatch(script, @"^\s*STATION\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            if (hasProgram && hasMode && hasStation) return validation;

            List<ProgramValidationIssue> issues = validation.Issues.ToList();
            if (!hasProgram)
            {
                issues.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Generated script is incomplete: missing PROGRAM command."));
            }

            if (!hasMode)
            {
                issues.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Generated script is incomplete: missing MODE command."));
            }

            if (!hasStation)
            {
                issues.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Generated script is incomplete: missing at least one STATION block."));
            }

            return new ProgramValidationResult
            {
                Script = validation.Script,
                ParsedProgram = validation.ParsedProgram,
                Issues = issues
            };
        }

        private IReadOnlyList<AiChatMessage> BuildGenerationMessages(string request, string currentValidatedScript, ConversationState conversation)
        {
            List<AiChatMessage> messages = new List<AiChatMessage>
            {
                new AiChatMessage(
                    AiChatRole.Assistant,
                    BuildSystemInstruction(),
                    DateTimeOffset.Now)
            };

            if (!string.IsNullOrWhiteSpace(currentValidatedScript))
            {
                messages.Add(new AiChatMessage(
                    AiChatRole.User,
                    "Current validated script to revise:\n" + currentValidatedScript.Trim(),
                    DateTimeOffset.Now));
            }

            foreach (AiChatMessage m in conversation.TakeRecent(_options.MaximumConversationMessages))
            {
                messages.Add(m);
            }

            messages.Add(new AiChatMessage(AiChatRole.User, request, DateTimeOffset.Now));
            return messages;
        }

        private IReadOnlyList<AiChatMessage> BuildRepairMessages(
            string request,
            string invalidScript,
            IReadOnlyList<ProgramValidationIssue> issues,
            ConversationState conversation)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("The previous response is invalid Gym Interval Clock script.");
            sb.AppendLine("Fix only the validation errors listed below.");
            sb.AppendLine("Return only the complete corrected script.");
            sb.AppendLine("Do not add markdown fences or explanatory prose.");
            sb.AppendLine();
            sb.AppendLine("Original user request:");
            sb.AppendLine(request);
            sb.AppendLine();
            sb.AppendLine("Validation issues:");
            foreach (ProgramValidationIssue issue in issues.Where(i => i.Severity == ValidationSeverity.Error))
            {
                string line = issue.LineNumber.HasValue ? "Line " + issue.LineNumber.Value + ": " : string.Empty;
                sb.AppendLine("- " + line + issue.Message);
            }
            sb.AppendLine("- Ensure PROGRAM and STATION names are quoted (e.g., STATION \"Bike\").");
            sb.AppendLine("- Do not output bare COLOUR/COLOR lines; include a valid value or omit the line.");
            sb.AppendLine();
            sb.AppendLine("Invalid script to repair:");
            sb.AppendLine(invalidScript ?? string.Empty);

            List<AiChatMessage> messages = new List<AiChatMessage>
            {
                new AiChatMessage(
                    AiChatRole.Assistant,
                    BuildSystemInstruction(),
                    DateTimeOffset.Now)
            };

            foreach (AiChatMessage m in conversation.TakeRecent(_options.MaximumConversationMessages))
            {
                messages.Add(m);
            }

            messages.Add(new AiChatMessage(AiChatRole.User, sb.ToString(), DateTimeOffset.Now));
            return messages;
        }

        private static string BuildSystemInstruction()
        {
            return
                "You are the Gym Interval Clock Program Assistant. " +
                "Create or revise Gym Interval Clock program scripts. " +
                "Return only the complete script. " +
                "Do not use Markdown fences. " +
                "Do not add prose before or after the script. " +
                "Never invent unsupported commands. " +
                "Supported commands are PROGRAM, MODE, STATION, END STATION, BETWEEN, END BETWEEN, REPEAT, END, WORK, REST, RECOVERY, MOVE, COLOUR/COLOR, WORKNOTE, RESTNOTE, LINK, PANEL HIDE. " +
                "PROGRAM must be followed by a quoted name, e.g. PROGRAM \"Session Name\". " +
                "STATION must be followed by a quoted name, e.g. STATION \"Bike\". " +
                "Do not emit LABEL (it is not a valid command). Put text in station name, WORKNOTE or RESTNOTE instead. " +
                "Duration format must be numeric seconds or shorthand units only (for example: WORK 45, REST 15, WORK 2m, MOVE 30s). " +
                "Every block must be correctly closed according to the application's grammar. " +
                "In MODE SEQUENTIAL and MODE PARALLEL, each STATION must include at least one timed block (WORK/REST/RECOVERY/MOVE) before END STATION. " +
                "In MODE SHARED and MODE CIRCUIT, timed content may be defined in shared/repeat sections while stations can carry labels/colour/instructions. " +
                "Do not place standalone BETWEEN markers between station declarations. If BETWEEN is used, include timed blocks and close with END BETWEEN. " +
                "Do not emit empty STATION or BETWEEN sections. " +
                "Ensure every STATION and BETWEEN has a matching END STATION or END BETWEEN. " +
                "Never output bare tokens like PROGRAM, MODE, WORK or STATION without required values.";
        }

        private ProgramValidationResult ValidateWithAllGuards(string request, string script)
        {
            ProgramValidationResult validation = _validator.Validate(script);
            validation = ApplyRequestRequirementIssues(request, validation);
            validation = EnforceExplicitExerciseRequirements(request, validation);
            validation = EnforceMinimumScriptShape(validation);
            return validation;
        }

        private bool TryBuildDeterministicTemplateFallback(string request, out ProgramValidationResult validation)
        {
            validation = null;
            string req = request ?? string.Empty;

            bool requestedStructuredWorkout = req.IndexOf("pyramid", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("station", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("cardio", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("resistance", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("strength", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!requestedStructuredWorkout) return false;

            ExecutionMode mode = DetectRequestedMode(req);
            int stationCount = ResolveRequestedStationCount(req, mode);
            int targetMinutes = TryExtractRequestedMinutes(req, out int mins) ? mins : 30;
            bool includeCardio = req.IndexOf("cardio", StringComparison.OrdinalIgnoreCase) >= 0;
            bool includeResistance = req.IndexOf("resistance", StringComparison.OrdinalIgnoreCase) >= 0
                || req.IndexOf("strength", StringComparison.OrdinalIgnoreCase) >= 0;

            string script = BuildDeterministicTemplateScript(mode, stationCount, targetMinutes, includeCardio, includeResistance, req);

            ProgramValidationResult structural = _validator.Validate(script);
            if (!structural.IsValid)
            {
                validation = structural;
                return false;
            }

            ProgramValidationResult guarded = ValidateWithAllGuards(req, script);
            validation = guarded.IsValid ? guarded : structural;
            return true;
        }

        private static ExecutionMode DetectRequestedMode(string request)
        {
            string req = request ?? string.Empty;
            Match explicitMode = Regex.Match(req, @"MODE\s+(SHARED|SEQUENTIAL|PARALLEL|CIRCUIT)", RegexOptions.IgnoreCase);
            if (explicitMode.Success)
            {
                switch (explicitMode.Groups[1].Value.ToUpperInvariant())
                {
                    case "SEQUENTIAL": return ExecutionMode.Sequential;
                    case "PARALLEL": return ExecutionMode.Parallel;
                    case "CIRCUIT": return ExecutionMode.Circuit;
                    default: return ExecutionMode.Shared;
                }
            }

            return ExecutionMode.Shared;
        }

        private static int ResolveRequestedStationCount(string request, ExecutionMode mode)
        {
            if (TryExtractRequestedCount(request, "station", out int stations))
            {
                return Math.Max(2, stations);
            }

            bool cardio = (request ?? string.Empty).IndexOf("cardio", StringComparison.OrdinalIgnoreCase) >= 0;
            bool resistance = (request ?? string.Empty).IndexOf("resistance", StringComparison.OrdinalIgnoreCase) >= 0
                || (request ?? string.Empty).IndexOf("strength", StringComparison.OrdinalIgnoreCase) >= 0;
            bool pyramid = (request ?? string.Empty).IndexOf("pyramid", StringComparison.OrdinalIgnoreCase) >= 0;

            if (cardio && resistance && pyramid)
            {
                return mode == ExecutionMode.Sequential || mode == ExecutionMode.Parallel ? 6 : 8;
            }

            return 6;
        }

        private static string BuildDeterministicTemplateScript(
            ExecutionMode mode,
            int stationCount,
            int targetMinutes,
            bool includeCardio,
            bool includeResistance,
            string request)
        {
            string[] cardioExercises =
            {
                "Jumping Jacks", "High Knees", "Mountain Climbers", "Burpees", "Jump Rope", "Shuttle Runs", "Fast Step-Ups", "Skater Hops"
            };
            string[] resistanceExercises =
            {
                "Push Ups", "Bodyweight Squats", "Lunges", "Plank Shoulder Taps", "Glute Bridge", "Dumbbell Press", "Bent-Over Rows", "Kettlebell Deadlifts"
            };

            List<string> stationNames = new List<string>();
            int cardioIdx = 0;
            int resistanceIdx = 0;
            for (int i = 0; i < stationCount; i++)
            {
                bool useCardio = includeCardio && (!includeResistance || i % 2 == 0);
                if (!includeCardio && includeResistance) useCardio = false;
                if (!includeCardio && !includeResistance) useCardio = i % 2 == 0;

                if (useCardio)
                {
                    stationNames.Add("Cardio: " + cardioExercises[cardioIdx % cardioExercises.Length]);
                    cardioIdx++;
                }
                else
                {
                    stationNames.Add("Resistance: " + resistanceExercises[resistanceIdx % resistanceExercises.Length]);
                    resistanceIdx++;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PROGRAM \"" + Math.Max(1, targetMinutes) + " Minute Cardio & Resistance Pyramid\"");
            sb.AppendLine("MODE " + mode.ToString().ToUpperInvariant());
            sb.AppendLine();

            if (mode == ExecutionMode.Shared || mode == ExecutionMode.Circuit)
            {
                int rounds = TryExtractRequestedCount(request, "round", out int requestedRounds)
                    ? Math.Max(1, requestedRounds)
                    : 1;

                if (mode == ExecutionMode.Shared)
                {
                    int cycleSeconds = 285; // 30/45/60/45/30 with 15s recoveries
                    if (rounds == 1)
                    {
                        rounds = Math.Max(1, (int)Math.Round((targetMinutes * 60.0) / cycleSeconds));
                    }
                }

                if (mode == ExecutionMode.Circuit)
                {
                    int stationFactor = Math.Max(1, stationCount);
                    int perStationRoundSeconds = Math.Max(30,
                        (targetMinutes * 60) / Math.Max(1, stationFactor * rounds));
                    int unit = Math.Max(1, perStationRoundSeconds / 19);

                    sb.AppendLine("REPEAT " + rounds);
                    AppendSharedPyramidBlocks(sb, "    ", unit);
                    sb.AppendLine("END");
                    sb.AppendLine();

                    foreach (string station in stationNames)
                    {
                        sb.AppendLine("STATION \"" + station.Replace("\"", "'") + "\"");
                        sb.AppendLine("END STATION");
                        sb.AppendLine();
                    }

                    return sb.ToString().Trim();
                }

                sb.AppendLine("REPEAT " + rounds);
                AppendSharedPyramidBlocks(sb, "    ");
                sb.AppendLine("END");
                sb.AppendLine();

                foreach (string station in stationNames)
                {
                    sb.AppendLine("STATION \"" + station.Replace("\"", "'") + "\"");
                    sb.AppendLine("END STATION");
                    sb.AppendLine();
                }
            }
            else
            {
                int stationTargetSeconds = Math.Max(120, (targetMinutes * 60) / Math.Max(1, stationCount));
                int workA = Math.Max(20, stationTargetSeconds / 8);
                int workB = Math.Max(workA + 5, stationTargetSeconds / 6);
                int workC = Math.Max(workB + 5, stationTargetSeconds / 5);
                int rest = Math.Max(10, stationTargetSeconds / 16);

                foreach (string station in stationNames)
                {
                    sb.AppendLine("STATION \"" + station.Replace("\"", "'") + "\"");
                    sb.AppendLine("REPEAT 1");
                    sb.AppendLine("    WORK " + workA);
                    sb.AppendLine("    REST " + rest);
                    sb.AppendLine("    WORK " + workB);
                    sb.AppendLine("    REST " + rest);
                    sb.AppendLine("    WORK " + workC);
                    sb.AppendLine("    REST " + rest);
                    sb.AppendLine("    WORK " + workB);
                    sb.AppendLine("    REST " + rest);
                    sb.AppendLine("    WORK " + workA);
                    sb.AppendLine("    REST " + rest);
                    sb.AppendLine("END");
                    sb.AppendLine("END STATION");
                    sb.AppendLine();
                }
            }

            return sb.ToString().Trim();
        }

        private static void AppendSharedPyramidBlocks(StringBuilder sb, string indent, int scaleSeconds = 15)
        {
            int rest = Math.Max(5, scaleSeconds);
            sb.AppendLine(indent + "WORK " + (2 * scaleSeconds));
            sb.AppendLine(indent + "REST " + rest);
            sb.AppendLine(indent + "WORK " + (3 * scaleSeconds));
            sb.AppendLine(indent + "REST " + rest);
            sb.AppendLine(indent + "WORK " + (4 * scaleSeconds));
            sb.AppendLine(indent + "REST " + rest);
            sb.AppendLine(indent + "WORK " + (3 * scaleSeconds));
            sb.AppendLine(indent + "REST " + rest);
            sb.AppendLine(indent + "WORK " + (2 * scaleSeconds));
            sb.AppendLine(indent + "REST " + rest);
        }

        private static ProgramValidationResult ApplyRequestRequirementIssues(string request, ProgramValidationResult validation)
        {
            if (validation == null) return null;
            if (validation.ParsedProgram == null) return validation;

            List<ProgramValidationIssue> extra = new List<ProgramValidationIssue>();
            string text = request ?? string.Empty;

            if (TryExtractRequestedMinutes(text, out int minutes))
            {
                int requestedSeconds = minutes * 60;
                int estimated = EstimateDurationSeconds(validation.ParsedProgram);
                if (estimated > 0 && Math.Abs(estimated - requestedSeconds) > 300)
                {
                    extra.Add(new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Estimated duration " + FormatDuration(estimated)
                        + " does not match requested duration of about " + minutes + " minutes."));
                }
            }

            bool needsCardio = text.IndexOf("cardio", StringComparison.OrdinalIgnoreCase) >= 0;
            bool needsResistance = text.IndexOf("resistance", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("strength", StringComparison.OrdinalIgnoreCase) >= 0;
            if (TryExtractRequestedCount(text, "station", out int requestedStations))
            {
                int actualStations = validation.ParsedProgram.Stations.Count;
                if (actualStations != requestedStations)
                {
                    extra.Add(new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Requested " + requestedStations + " stations, but generated " + actualStations + "."));
                }
            }

            if (TryExtractRequestedCount(text, "exercise", out int requestedExercises))
            {
                int actualExercises = validation.ParsedProgram.Stations
                    .Count(s => !string.IsNullOrWhiteSpace(s.Name));
                if (actualExercises < requestedExercises)
                {
                    extra.Add(new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Requested at least " + requestedExercises + " exercises, but generated " + actualExercises + "."));
                }
            }

            if (text.IndexOf("pyramid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                List<int> workDurations = validation.ParsedProgram
                    .BuildRuntimeSequence()
                    .Where(x => x.Block != null && x.Block.Type == BlockType.Work)
                    .Select(x => Math.Max(0, x.Block.Seconds))
                    .Where(x => x > 0)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                if (workDurations.Count < 3)
                {
                    extra.Add(new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Requested pyramid pattern is missing. Include at least three distinct WORK durations to form a pyramid."));
                }
            }

            if (needsCardio || needsResistance)
            {
                string stationText = string.Join(" ", validation.ParsedProgram.Stations.Select(s => (s.Name ?? string.Empty) + " " + (s.WorkInstruction ?? string.Empty)));
                string sharedText = string.Join(" ", validation.ParsedProgram.SharedTimeline.Items.Select(i => i.Block?.Label ?? string.Empty));
                string combined = stationText + " " + sharedText;

                if (needsCardio && !ContainsAny(combined, CardioKeywords))
                {
                    extra.Add(new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Requested cardio content is missing. Include cardio exercises in station names or notes."));
                }

                if (needsResistance && !ContainsAny(combined, ResistanceKeywords))
                {
                    extra.Add(new ProgramValidationIssue(
                        ValidationSeverity.Error,
                        null,
                        "Requested resistance/strength content is missing. Include resistance exercises in station names or notes."));
                }
            }

            if (extra.Count == 0) return validation;

            return new ProgramValidationResult
            {
                Script = validation.Script,
                ParsedProgram = validation.ParsedProgram,
                Issues = validation.Issues.Concat(extra).ToArray()
            };
        }

        private static readonly string[] CardioKeywords =
        {
            "cardio", "run", "sprint", "jog", "high knees", "jump", "jumping jacks", "mountain climber", "burpee", "row", "bike", "cycle", "skipping"
        };

        private static readonly string[] ResistanceKeywords =
        {
            "resistance", "strength", "push up", "squat", "lunge", "plank", "deadlift", "press", "pull", "row", "kettlebell", "dumbbell"
        };

        private static bool TryExtractRequestedMinutes(string request, out int minutes)
        {
            minutes = 0;
            Match m = Regex.Match(request ?? string.Empty, @"\b(\d{1,3})\s*(minute|minutes|min|mins)\b", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            return int.TryParse(m.Groups[1].Value, out minutes) && minutes > 0;
        }

        private static bool TryExtractRequestedCount(string request, string nounRoot, out int count)
        {
            count = 0;
            if (string.IsNullOrWhiteSpace(nounRoot)) return false;

            string pattern = @"\b(\d{1,3})\s*" + Regex.Escape(nounRoot) + @"(?:s)?\b";
            Match m = Regex.Match(request ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            return int.TryParse(m.Groups[1].Value, out count) && count > 0;
        }

        private static int EstimateDurationSeconds(WorkoutProgram program)
        {
            if (program == null) return 0;

            if (program.Mode == ExecutionMode.Parallel)
            {
                int max = 0;
                foreach (List<ResolvedBlock> lane in program.BuildParallelSequences())
                {
                    int laneSeconds = lane.Sum(x => Math.Max(0, x.Block.Seconds));
                    if (laneSeconds > max) max = laneSeconds;
                }

                return max;
            }

            return program.BuildRuntimeSequence().Sum(x => Math.Max(0, x.Block.Seconds));
        }

        private static string FormatDuration(int seconds)
        {
            int mins = Math.Max(0, seconds) / 60;
            int secs = Math.Max(0, seconds) % 60;
            return mins + "m " + secs + "s";
        }

        private static bool ContainsAny(string text, IEnumerable<string> terms)
        {
            string value = text ?? string.Empty;
            return terms.Any(t => value.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TryBuildModeClarification(string request, string currentValidatedScript, out string clarification)
        {
            clarification = string.Empty;

            string text = (request ?? string.Empty).Trim();
            if (text.Length == 0) return false;

            if (LooksLikeRevisionRequest(text)) return false;

            if (ContainsModeHint(text)) return false;

            bool looksLikePyramidRequest = text.IndexOf("pyramid", StringComparison.OrdinalIgnoreCase) >= 0;
            bool looksLikeIntervalRequest = text.IndexOf("interval", StringComparison.OrdinalIgnoreCase) >= 0;
            bool looksLikeCircuitRequest = text.IndexOf("circuit", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasStationHint = text.IndexOf("station", StringComparison.OrdinalIgnoreCase) >= 0;
            bool genericProgramRequest = IsGenericProgramRequest(text);

            bool shouldClarify = looksLikePyramidRequest
                || looksLikeIntervalRequest
                || (looksLikeCircuitRequest && !hasStationHint)
                || genericProgramRequest;

            if (!shouldClarify) return false;

            clarification =
                "Before I generate, choose the mode so the script validates correctly:" + Environment.NewLine
                + "1) MODE SHARED - one timeline for the whole group." + Environment.NewLine
                + "2) MODE SEQUENTIAL - station-by-station rotation (each station needs timing blocks)." + Environment.NewLine
                + "3) MODE PARALLEL - station lanes run at the same time." + Environment.NewLine
                + "4) MODE CIRCUIT - shared rounds repeated across a station list." + Environment.NewLine
                + "Reply with a number (1-4) or type the mode name.";
            return true;
        }

        private static bool ContainsModeHint(string request)
        {
            string value = request ?? string.Empty;
            return value.IndexOf("MODE SHARED", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("MODE SEQUENTIAL", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("MODE PARALLEL", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("MODE CIRCUIT", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("shared", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("sequential", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("parallel", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("mode shared", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("mode sequential", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("mode parallel", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("mode circuit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeRevisionRequest(string request)
        {
            string value = request ?? string.Empty;
            return value.IndexOf("revise", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("modify", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("edit", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("fix", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("current", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("existing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGenericProgramRequest(string request)
        {
            string value = request ?? string.Empty;
            bool mentionsProgramShape = value.IndexOf("program", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("workout", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("class", StringComparison.OrdinalIgnoreCase) >= 0;

            bool mentionsSpecificStructure = value.IndexOf("station", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("repeat", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("round", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("between", StringComparison.OrdinalIgnoreCase) >= 0;

            return mentionsProgramShape && !mentionsSpecificStructure;
        }

        private static AiProgramProposal ToProposal(string userRequest, ProgramValidationResult validation)
        {
            return new AiProgramProposal
            {
                UserRequest = userRequest,
                Script = validation.Script,
                ParsedProgram = validation.ParsedProgram,
                Issues = validation.Issues
            };
        }

        internal static string CleanMarkdownFences(string text)
        {
            string value = text ?? string.Empty;
            string trimmed = value.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

            string[] lines = trimmed.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0) return string.Empty;

            int start = lines[0].StartsWith("```", StringComparison.Ordinal) ? 1 : 0;
            int end = lines.Length;
            if (end > start && lines[end - 1].Trim().StartsWith("```", StringComparison.Ordinal)) end--;

            return string.Join(Environment.NewLine, lines.Skip(start).Take(Math.Max(0, end - start))).Trim();
        }

        private static string NormalizeCommonScriptIssues(string script)
        {
            string text = script ?? string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> normalized = new List<string>(lines.Length);

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                string raw = line ?? string.Empty;
                string trimmed = raw.Trim();

                if (trimmed.Equals("STATION", StringComparison.OrdinalIgnoreCase))
                {
                    int next = lineIndex + 1;
                    while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next])) next++;
                    if (next < lines.Length
                        && lines[next].TrimStart().StartsWith("STATION ", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (trimmed.Equals("END", StringComparison.OrdinalIgnoreCase))
                {
                    int next = lineIndex + 1;
                    while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next])) next++;
                    if (next < lines.Length
                        && lines[next].Trim().StartsWith("END STATION", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (trimmed.Equals("COLOUR", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Equals("COLOR", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int indentLen = raw.Length - raw.TrimStart().Length;
                string indent = indentLen > 0 ? raw.Substring(0, indentLen) : string.Empty;

                if (trimmed.StartsWith("STATION ", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = trimmed.Substring(8).Trim();
                    if (rest.Length > 0)
                    {
                        if (!rest.StartsWith("\"", StringComparison.Ordinal))
                        {
                            raw = indent + "STATION \"" + rest.Replace("\"", "'") + "\"";
                        }
                        else if (rest.StartsWith("\"", StringComparison.Ordinal)
                            && rest.Count(c => c == '"') == 1)
                        {
                            raw = indent + "STATION " + rest + "\"";
                        }
                    }
                }
                if (trimmed.StartsWith("PROGRAM ", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = trimmed.Substring(8).Trim();
                    if (rest.Length > 0 && !rest.StartsWith("\"", StringComparison.Ordinal))
                    {
                        raw = indent + "PROGRAM \"" + rest.Replace("\"", "'") + "\"";
                    }
                }

                normalized.Add(raw);
            }

            List<string> balanced = new List<string>(normalized.Count + 8);
            int openStations = 0;
            int openBetween = 0;
            int openRepeat = 0;
            foreach (string line in normalized)
            {
                string trimmed = (line ?? string.Empty).Trim();

                if ((trimmed.StartsWith("STATION ", StringComparison.OrdinalIgnoreCase)
                     || trimmed.StartsWith("BETWEEN", StringComparison.OrdinalIgnoreCase))
                    && openRepeat > 0)
                {
                    balanced.Add("END");
                    openRepeat = 0;
                }

                if (trimmed.StartsWith("STATION ", StringComparison.OrdinalIgnoreCase) && openStations > 0)
                {
                    balanced.Add("END STATION");
                    openStations = 0;
                }

                if (trimmed.StartsWith("BETWEEN", StringComparison.OrdinalIgnoreCase) && openBetween > 0)
                {
                    balanced.Add("END BETWEEN");
                    openBetween = 0;
                }

                balanced.Add(line);

                if (trimmed.StartsWith("STATION ", StringComparison.OrdinalIgnoreCase)) openStations++;
                else if (trimmed.StartsWith("END STATION", StringComparison.OrdinalIgnoreCase) && openStations > 0) openStations--;

                if (trimmed.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase)) openBetween++;
                else if (trimmed.StartsWith("END BETWEEN", StringComparison.OrdinalIgnoreCase) && openBetween > 0) openBetween--;

                if (trimmed.StartsWith("REPEAT ", StringComparison.OrdinalIgnoreCase)) openRepeat++;
                else if (trimmed.Equals("END", StringComparison.OrdinalIgnoreCase) && openRepeat > 0) openRepeat--;
            }

            while (openStations-- > 0) balanced.Add("END STATION");
            while (openBetween-- > 0) balanced.Add("END BETWEEN");
            while (openRepeat-- > 0) balanced.Add("END");

            return string.Join(Environment.NewLine, balanced);
        }
    }
}
