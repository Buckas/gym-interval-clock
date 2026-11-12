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

            ProgramValidationResult baselineResult = string.IsNullOrWhiteSpace(currentValidatedScript)
                ? null
                : _validator.Validate(currentValidatedScript);
            WorkoutProgram baselineProgram = baselineResult != null && baselineResult.IsValid
                ? baselineResult.ParsedProgram
                : null;
            bool isRevision = baselineProgram != null && !IsFreshProgramRequest(requestBeforeAugmentation);

            // A reply like "2" or "go with Jump Rope" right after we offered options.
            PendingStationSuggestion pending = state.PendingSuggestion;
            state.PendingSuggestion = null;
            if (pending != null && isRevision && TryPickPendingOption(requestBeforeAugmentation, pending, baselineProgram, out StationOption pick))
            {
                int pickIndex = baselineProgram.Stations.FindIndex(s =>
                    string.Equals(s.Name, pending.StationName, StringComparison.OrdinalIgnoreCase));
                if (pickIndex >= 0)
                {
                    return await SwapStationAsync(
                        requestBeforeAugmentation, baselineResult, baselineProgram, pickIndex, pick.Name, pick.Note, state, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (isRevision && TryBuildUnknownStationReply(requestBeforeAugmentation, baselineProgram, out string unknownStationReply))
            {
                state.AddAssistant(unknownStationReply);
                return UnchangedProposal(requestBeforeAugmentation, baselineResult, unknownStationReply);
            }

            if (isRevision && TryDetectStationSwap(requestBeforeAugmentation, baselineProgram, out int swapIndex, out string explicitReplacement))
            {
                if (explicitReplacement == null && IsSuggestionRequest(requestBeforeAugmentation))
                {
                    // The user asked for ideas, not a change: offer options and leave the program alone.
                    return await SuggestStationOptionsAsync(
                        requestBeforeAugmentation, baselineResult, baselineProgram, swapIndex, state, cancellationToken)
                        .ConfigureAwait(false);
                }

                return await SwapStationAsync(
                    requestBeforeAugmentation, baselineResult, baselineProgram, swapIndex, explicitReplacement, null, state, cancellationToken)
                    .ConfigureAwait(false);
            }

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
                IReadOnlyList<AiChatMessage> generationMessages = BuildGenerationMessages(generationRequest, currentValidatedScript, isRevision, state);
                string first = await _aiService.GenerateTextAsync(generationMessages, cancellationToken).ConfigureAwait(false);
                string firstScript = CleanMarkdownFences(first);
                firstScript = NormalizeInlineCommands(firstScript);
                firstScript = NormalizeCommonScriptIssues(firstScript);
                firstScript = BalanceProgramAndStationQuotes(firstScript);
                ProgramValidationResult firstValidation = _validator.Validate(firstScript);
                firstValidation = ApplyRequestRequirementIssues(request, firstValidation);
                firstValidation = EnforceExplicitExerciseRequirements(request, firstValidation);
                firstValidation = EnforceMinimumScriptShape(firstValidation);
                firstValidation = EnforceRevisionBaselineShape(requestBeforeAugmentation, isRevision ? baselineProgram : null, firstValidation);

                if (firstValidation.IsValid)
                {
                    state.AddAssistant("Proposed a valid program script.");
                    return ToProposal(request, firstValidation, isRevision ? baselineProgram : null);
                }

                ProgramValidationResult repairValidation = firstValidation;
                string repaired = firstScript;

                for (int repairAttempt = 0; repairAttempt < maxRepairs; repairAttempt++)
                {
                    IReadOnlyList<AiChatMessage> repairMessages = BuildRepairMessages(
                        request, repaired, repairValidation.Issues, isRevision ? currentValidatedScript : null, state);
                    repaired = await _aiService.GenerateTextAsync(repairMessages, cancellationToken).ConfigureAwait(false);
                    repaired = CleanMarkdownFences(repaired);
                    repaired = NormalizeInlineCommands(repaired);
                    repaired = NormalizeCommonScriptIssues(repaired);
                    repaired = BalanceProgramAndStationQuotes(repaired);
                    repairValidation = _validator.Validate(repaired);
                    repairValidation = ApplyRequestRequirementIssues(request, repairValidation);
                    repairValidation = EnforceExplicitExerciseRequirements(request, repairValidation);
                    repairValidation = EnforceMinimumScriptShape(repairValidation);
                    repairValidation = EnforceRevisionBaselineShape(requestBeforeAugmentation, isRevision ? baselineProgram : null, repairValidation);

                    if (repairValidation.IsValid)
                    {
                        state.AddAssistant("Repaired proposal into a valid program script.");
                        return ToProposal(request, repairValidation, isRevision ? baselineProgram : null);
                    }
                }

                lastValidation = repairValidation;
                if (generationAttempt < maxGenerations - 1)
                {
                    state.AddAssistant("Previous proposal invalid; retrying generation.");
                    generationRequest = BuildRetryGenerationRequest(request, lastValidation.Issues, generationAttempt + 2, maxGenerations);
                }
            }

            if (baselineProgram != null)
            {
                // Never replace an existing program with a canned template: that silently
                // discards the user's mode, timings, colours and stations. Leave it alone instead.
                string reason = DescribeTopIssues(lastValidation);
                string message =
                    "I couldn't produce a safe change to your program, so it has been left unchanged."
                    + (reason.Length > 0 ? Environment.NewLine + "Problems with my attempt: " + reason : string.Empty)
                    + Environment.NewLine + "Try rephrasing, e.g. 'Replace \""
                    + (baselineProgram.Stations.Count > 0 ? baselineProgram.Stations[baselineProgram.Stations.Count - 1].Name : "Station 1")
                    + "\" with Burpees'.";
                state.AddAssistant("Left the current program unchanged after invalid AI attempts.");
                return UnchangedProposal(request, baselineResult, message);
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

        // ---- Station swap fast path -------------------------------------------------------
        // Replacing one station is the most common edit. Instead of asking a small local model
        // to rewrite the whole script (slow, and it tends to change other things), ask it only
        // for an exercise name and apply the rename to the script text ourselves.

        private static readonly Regex SwapIntent = new Regex(
            @"\b(replace|replacing|replacement|swap|swapping|substitute|instead\s+of|alternative\s+(to|for)|change|changing|rename|something\s+else|different\s+(exercise|station|one|activity))\b",
            RegexOptions.IgnoreCase);

        // Edits to a station's look or position are never exercise swaps.
        private static readonly Regex NotASwap = new Regex(
            @"\b(colou?r|order|position|move\s+.*\s+(up|down|before|after))\b",
            RegexOptions.IgnoreCase);

        // Mentioning notes is only a swap when a replacement exercise is named
        // ("replace X with Y and add a note"), not for "change the note on X".
        private static readonly Regex NotesMention = new Regex(
            @"\b(note|notes|worknote|restnote|cue|cues|instruction|instructions)\b",
            RegexOptions.IgnoreCase);

        private static readonly Regex VagueReplacement = new Regex(
            @"\b(something|anything|else|suggest|suggestion|alternative|another|different|other|better|you)\b",
            RegexOptions.IgnoreCase);

        public static bool TryDetectStationSwap(string request, WorkoutProgram baseline, out int stationIndex, out string explicitReplacement)
        {
            stationIndex = -1;
            explicitReplacement = null;
            string text = request ?? string.Empty;
            if (baseline == null || baseline.Stations.Count == 0) return false;
            if (!SwapIntent.IsMatch(text) || NotASwap.IsMatch(text)) return false;
            if (MentionsTiming(text) || MentionsMode(text)) return false; // more than a simple swap

            // Find the longest station name mentioned in the request (quoted or not).
            int bestLength = 0;
            int mentionEnd = -1;
            for (int i = 0; i < baseline.Stations.Count; i++)
            {
                string name = baseline.Stations[i].Name ?? string.Empty;
                if (name.Length == 0) continue;
                int at = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                {
                    name = StripPrefix(name); // "High Knees" for "Cardio: High Knees"
                    at = name.Length >= 3 ? text.IndexOf(name, StringComparison.OrdinalIgnoreCase) : -1;
                }
                if (at < 0 || name.Length <= bestLength) continue;
                bestLength = name.Length;
                stationIndex = i;
                mentionEnd = at + name.Length;
            }

            if (stationIndex < 0) return false;

            // "replace X with Y" / "swap X for Y" -> Y is given, no AI needed.
            string after = text.Substring(mentionEnd);
            Match m = Regex.Match(after, "^[\"”']?\\s*,?\\s*(?:with|for|by|to)\\s+[\"“']?([^\"“”?!.,\\n]{2,60})", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                // Stop the name at a joined instruction: "Tuck Jumps and add a note" -> "Tuck Jumps".
                string candidate = Regex.Split(m.Groups[1].Value, @"\s+(?:and|then|but|plus|also|please|with)\b", RegexOptions.IgnoreCase)[0].Trim();
                if (candidate.Length >= 2
                    && !VagueReplacement.IsMatch(candidate)
                    && !candidate.StartsWith("a ", StringComparison.OrdinalIgnoreCase))
                {
                    explicitReplacement = candidate;
                }
            }

            if (explicitReplacement == null && NotesMention.IsMatch(text)) return false;
            return true;
        }

        private async Task<AiProgramProposal> SwapStationAsync(
            string request,
            ProgramValidationResult baselineResult,
            WorkoutProgram baseline,
            int stationIndex,
            string explicitReplacement,
            string explicitNote,
            ConversationState state,
            CancellationToken cancellationToken)
        {
            StationDef target = baseline.Stations[stationIndex];
            string newName = null;
            string note = null;

            if (!string.IsNullOrWhiteSpace(explicitReplacement))
            {
                newName = explicitReplacement;
                note = explicitNote;

                bool wantsNotes = Regex.IsMatch(request ?? string.Empty, @"\b(notes?|cues?|instructions?|coaching)\b", RegexOptions.IgnoreCase);
                if (string.IsNullOrWhiteSpace(note) && wantsNotes)
                {
                    note = await SuggestCueAsync(newName, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                for (int attempt = 0; attempt < 2 && newName == null; attempt++)
                {
                    string reply = await _aiService.GenerateTextAsync(
                        BuildSwapSuggestionMessages(request, baseline, target, attempt > 0), cancellationToken).ConfigureAwait(false);
                    ParseSwapSuggestion(reply, baseline, out newName, out note);
                }
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                string message = "I couldn't come up with a usable replacement for \"" + target.Name
                    + "\", so your program is unchanged. Try naming one, e.g. 'Replace \"" + target.Name + "\" with Lateral Lunges'.";
                state.AddAssistant(message);
                return UnchangedProposal(request, baselineResult, message);
            }

            newName = ApplyNamePrefixConvention(target.Name, newName);
            string script = RenameStationInScript(baselineResult.Script, target.Name, newName, note);

            ProgramValidationResult validation = _validator.Validate(script);
            validation = EnforceRevisionBaselineShape(request, baseline, validation);
            if (!validation.IsValid)
            {
                string message = "I couldn't apply that change safely, so your program is unchanged. " + DescribeTopIssues(validation);
                state.AddAssistant(message);
                return UnchangedProposal(request, baselineResult, message);
            }

            state.AddAssistant("Replaced station \"" + target.Name + "\" with \"" + newName + "\".");
            AiProgramProposal proposal = ToProposal(request, validation, baseline);
            return new AiProgramProposal
            {
                UserRequest = proposal.UserRequest,
                Script = proposal.Script,
                ParsedProgram = proposal.ParsedProgram,
                Issues = proposal.Issues,
                AssistantMessage = (explicitReplacement == null
                        ? "Suggested replacement for \"" + target.Name + "\": " + newName
                          + (string.IsNullOrWhiteSpace(note) ? string.Empty : " (" + note + ")") + Environment.NewLine
                        : string.Empty)
                    + proposal.AssistantMessage
            };
        }

        private async Task<string> SuggestCueAsync(string exercise, CancellationToken cancellationToken)
        {
            string reply = await _aiService.GenerateTextAsync(new[]
            {
                new AiChatMessage(AiChatRole.System,
                    "You are a fitness coach. Reply with one short coaching cue (under 12 words) for the exercise. No other text.",
                    DateTimeOffset.Now),
                new AiChatMessage(AiChatRole.User, "Exercise: " + exercise, DateTimeOffset.Now)
            }, cancellationToken).ConfigureAwait(false);

            string cue = CleanMarkdownFences(reply ?? string.Empty)
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(l => l.Trim().Trim('"', '*', '-', ' ').TrimEnd('.'))
                .FirstOrDefault(l => l.Length > 0);
            return cue != null && cue.Length <= 120 ? cue : null;
        }

        private static readonly Regex SuggestionIntent = new Regex(
            @"\b(suggest|suggests|suggestion|suggestions|recommend|recommendation|recommendations|ideas?|options|alternatives|thoughts"
            + @"|what\s+(could|should|would|can|else|do\s+you)|which\s+(exercise|one|station)|do\s+you\s+have|any\s+(good\s+)?(ideas|thoughts|options))\b",
            RegexOptions.IgnoreCase);

        /// <summary>True when the user is asking for ideas rather than telling us to make the change.</summary>
        public static bool IsSuggestionRequest(string request)
        {
            return SuggestionIntent.IsMatch(request ?? string.Empty);
        }

        private const int SuggestionCount = 3;

        private async Task<AiProgramProposal> SuggestStationOptionsAsync(
            string request,
            ProgramValidationResult baselineResult,
            WorkoutProgram baseline,
            int stationIndex,
            ConversationState state,
            CancellationToken cancellationToken)
        {
            StationDef target = baseline.Stations[stationIndex];
            List<StationOption> options = new List<StationOption>();

            for (int attempt = 0; attempt < 2 && options.Count == 0; attempt++)
            {
                string reply = await _aiService.GenerateTextAsync(
                    BuildSwapSuggestionMessages(request, baseline, target, attempt > 0, SuggestionCount), cancellationToken).ConfigureAwait(false);

                foreach (string line in (CleanMarkdownFences(reply ?? string.Empty)).Replace("\r", string.Empty).Split('\n'))
                {
                    ParseSwapSuggestion(line, baseline, out string name, out string note);
                    if (name != null && !options.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        options.Add(new StationOption(name, note));
                    }
                    if (options.Count == SuggestionCount) break;
                }
            }

            string message;
            if (options.Count == 0)
            {
                message = "I couldn't come up with suggestions for \"" + target.Name + "\" this time. Nothing has been changed."
                    + Environment.NewLine + "You can name one yourself, e.g. 'Replace \"" + target.Name + "\" with Lateral Lunges'.";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Here are some options to replace \"" + target.Name + "\":");
                for (int i = 0; i < options.Count; i++)
                {
                    sb.AppendLine("  " + (i + 1) + ") " + options[i].Name
                        + (string.IsNullOrWhiteSpace(options[i].Note) ? string.Empty : " - " + options[i].Note));
                }
                sb.AppendLine("Nothing has been changed yet.");
                sb.Append("Reply with " + (options.Count == 1 ? "1" : "a number (1-" + options.Count + ")")
                    + " to use one, or say e.g. 'Replace \"" + target.Name + "\" with " + options[0].Name + "'.");
                message = sb.ToString();

                state.PendingSuggestion = new PendingStationSuggestion { StationName = target.Name, Options = options };
            }

            state.AddAssistant(message);
            return UnchangedProposal(request, baselineResult, message);
        }

        private static readonly string[] OrdinalWords = { "first", "second", "third", "fourth", "fifth" };

        public static bool TryPickPendingOption(string request, PendingStationSuggestion pending, WorkoutProgram baseline, out StationOption pick)
        {
            pick = null;
            string text = (request ?? string.Empty).Trim().TrimEnd('.', '!');
            if (pending?.Options == null || pending.Options.Count == 0 || text.Length == 0) return false;

            // Naming some other station means this is a new request, not a pick.
            if (baseline != null && baseline.Stations.Any(s =>
                    !string.Equals(s.Name, pending.StationName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(s.Name)
                    && text.IndexOf(s.Name, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }

            // Picked by name, e.g. "Let's go with Jump Rope".
            StationOption byName = pending.Options
                .OrderByDescending(o => o.Name.Length)
                .FirstOrDefault(o => text.IndexOf(o.Name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (byName != null)
            {
                pick = byName;
                return true;
            }

            // A reply that starts with the number ("3", "Option 2, and add notes ...") is a pick,
            // whatever follows it.
            Match leading = Regex.Match(text, @"^\s*(?:option|number|no\.?|#)?\s*([1-9])(?!\d)(?!\s*(s|sec|secs|seconds|m|min|mins|minutes|x|rounds?|stations?)\b)", RegexOptions.IgnoreCase);
            if (leading.Success)
            {
                int leadIndex = int.Parse(leading.Groups[1].Value) - 1;
                if (leadIndex >= 0 && leadIndex < pending.Options.Count)
                {
                    pick = pending.Options[leadIndex];
                    return true;
                }
            }

            if (text.Length > 40) return false; // longer replies are new requests, not a pick

            Match number = Regex.Match(text, @"\b([1-9])\b");
            int index = -1;
            if (number.Success)
            {
                index = int.Parse(number.Groups[1].Value) - 1;
            }
            else
            {
                for (int i = 0; i < OrdinalWords.Length; i++)
                {
                    if (Regex.IsMatch(text, @"\b" + OrdinalWords[i] + @"\b", RegexOptions.IgnoreCase)) index = i;
                }
            }

            if (index < 0 || index >= pending.Options.Count) return false;
            pick = pending.Options[index];
            return true;
        }

        private static IReadOnlyList<AiChatMessage> BuildSwapSuggestionMessages(
            string request, WorkoutProgram baseline, StationDef target, bool strict)
        {
            return BuildSwapSuggestionMessages(request, baseline, target, strict, 1);
        }

        private static IReadOnlyList<AiChatMessage> BuildSwapSuggestionMessages(
            string request, WorkoutProgram baseline, StationDef target, bool strict, int count)
        {
            if (count > 1)
            {
                string otherStations = string.Join(", ", baseline.Stations
                    .Where(s => !ReferenceEquals(s, target))
                    .Select(s => s.Name));
                string listSystem =
                    "You are a fitness coach helping edit a group interval workout. "
                    + "Suggest exactly " + count + " different exercises. Reply with exactly " + count + " lines, "
                    + "each in the form: Exercise Name | short coaching cue. "
                    + "Each exercise name must be 1 to 4 words. No other text, no numbering, no explanation.";
                if (strict)
                {
                    listSystem += " Example reply:\nLateral Lunges | Sink low and push off the outside foot\nJump Rope | Stay light on the balls of your feet\nTuck Jumps | Drive the knees to the chest";
                }

                string listUser =
                    "Workout \"" + baseline.Name + "\". Other stations: " + otherStations + ".\n"
                    + "Suggest replacements for the station \"" + target.Name + "\" that are not already in the workout "
                    + "and suit the same work/rest timing.\n"
                    + "Coach's request: " + request;

                return new[]
                {
                    new AiChatMessage(AiChatRole.System, listSystem, DateTimeOffset.Now),
                    new AiChatMessage(AiChatRole.User, listUser, DateTimeOffset.Now)
                };
            }

            string others = string.Join(", ", baseline.Stations
                .Where(s => !ReferenceEquals(s, target))
                .Select(s => s.Name));

            string system =
                "You are a fitness coach helping edit a group interval workout. "
                + "Suggest exactly one exercise. Reply with exactly one line in the form: Exercise Name | short coaching cue. "
                + "The exercise name must be 1 to 4 words. No other text, no lists, no explanation.";
            if (strict)
            {
                system += " Example reply: Lateral Lunges | Sink low and push off the outside foot";
            }

            string user =
                "Workout \"" + baseline.Name + "\". Other stations: " + others + ".\n"
                + "Replace the station \"" + target.Name + "\" with a different exercise that is not already in the workout "
                + "and suits the same work/rest timing.\n"
                + "Coach's request: " + request;

            return new[]
            {
                new AiChatMessage(AiChatRole.System, system, DateTimeOffset.Now),
                new AiChatMessage(AiChatRole.User, user, DateTimeOffset.Now)
            };
        }

        internal static void ParseSwapSuggestion(string reply, WorkoutProgram baseline, out string name, out string note)
        {
            name = null;
            note = null;

            string line = CleanMarkdownFences(reply ?? string.Empty)
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);
            if (line == null) return;

            line = Regex.Replace(line, @"^(\d+[.)]|[-*•])\s*", string.Empty);   // list markers
            line = line.Replace("**", string.Empty).Replace("\"", string.Empty);
            line = Regex.Replace(line, @"^(exercise( name)?|replacement|suggestion)\s*:\s*", string.Empty, RegexOptions.IgnoreCase);

            string[] parts = line.Split(new[] { '|' }, 2);
            string candidate = parts[0].Trim().TrimEnd('.', ':', '-').Trim();
            string cue = parts.Length > 1 ? parts[1].Trim() : null;

            int words = candidate.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (candidate.Length < 2 || candidate.Length > 40 || words > 5) return;
            if (Regex.IsMatch(candidate, @"^(PROGRAM|MODE|STATION|REPEAT|WORK|REST|I|I'd|Sure|Here)\b", RegexOptions.IgnoreCase)) return;
            if (baseline.Stations.Any(s => string.Equals(StripPrefix(s.Name), StripPrefix(candidate), StringComparison.OrdinalIgnoreCase))) return;

            name = candidate;
            if (!string.IsNullOrWhiteSpace(cue) && cue.Length <= 120)
            {
                note = cue.TrimEnd('.');
            }
        }

        private static string StripPrefix(string name)
        {
            string value = name ?? string.Empty;
            int colon = value.IndexOf(':');
            return (colon >= 0 ? value.Substring(colon + 1) : value).Trim();
        }

        /// <summary>Keeps naming like "Cardio: High Knees" consistent when the suggestion is just "Burpees".</summary>
        private static string ApplyNamePrefixConvention(string oldName, string newName)
        {
            int colon = (oldName ?? string.Empty).IndexOf(':');
            if (colon <= 0 || newName.IndexOf(':') >= 0) return newName;
            return oldName.Substring(0, colon + 1) + " " + newName;
        }

        /// <summary>
        /// Renames one STATION line in place and, when a cue is supplied, replaces (or adds) that
        /// station's WORKNOTE. Every other line is left byte-for-byte as it was.
        /// </summary>
        internal static string RenameStationInScript(string script, string oldName, string newName, string workNote)
        {
            string newline = script.Contains("\r\n") ? "\r\n" : "\n";
            List<string> lines = script.Replace("\r\n", "\n").Split('\n').ToList();
            Regex stationLine = new Regex("^(\\s*STATION\\s+\")" + Regex.Escape(oldName) + "(\".*)$", RegexOptions.IgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                Match m = stationLine.Match(lines[i]);
                if (!m.Success) continue;

                lines[i] = m.Groups[1].Value + newName.Replace("\"", "'") + m.Groups[2].Value;
                if (string.IsNullOrWhiteSpace(workNote)) break;

                string noteValue = workNote.Replace("\"", "'");
                string indent = Regex.Match(lines[i], @"^\s*").Value + "    ";
                for (int j = i + 1; j < lines.Count; j++)
                {
                    string t = lines[j].Trim();
                    if (t.StartsWith("END STATION", StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Insert(i + 1, indent + "WORKNOTE \"" + noteValue + "\"");
                        break;
                    }
                    if (t.StartsWith("WORKNOTE", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[j] = Regex.Match(lines[j], @"^\s*").Value + "WORKNOTE \"" + noteValue + "\"";
                        break;
                    }
                }
                break;
            }

            return string.Join(newline, lines);
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

        /// <summary>
        /// When revising an existing program, reject proposals that change things the user
        /// did not ask to change (mode, station count, timings, station colours).
        /// <paramref name="baseline"/> is null when this is not a revision.
        /// </summary>
        private static ProgramValidationResult EnforceRevisionBaselineShape(
            string request,
            WorkoutProgram baseline,
            ProgramValidationResult validation)
        {
            if (validation == null) return new ProgramValidationResult();
            if (baseline == null) return validation;
            WorkoutProgram proposed = validation.ParsedProgram;
            if (proposed == null) return validation;

            string req = request ?? string.Empty;
            List<ProgramValidationIssue> extra = new List<ProgramValidationIssue>();

            if (proposed.Mode != baseline.Mode && !MentionsMode(req))
            {
                extra.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Revision changed MODE from " + baseline.Mode.ToString().ToUpperInvariant()
                    + " to " + proposed.Mode.ToString().ToUpperInvariant()
                    + " but the request did not ask for a mode change. Keep MODE "
                    + baseline.Mode.ToString().ToUpperInvariant() + "."));
            }

            bool stationCountChangeAllowed = TryExtractRequestedCount(req, "station", out _)
                || HasAddOrRemoveStationIntent(req);
            if (!stationCountChangeAllowed
                && baseline.Stations.Count > 0
                && proposed.Stations.Count != baseline.Stations.Count)
            {
                extra.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Revision changed station count from " + baseline.Stations.Count + " to " + proposed.Stations.Count
                    + " without an explicit station-count request."));
            }

            if (!MentionsTiming(req) && !SameTimings(baseline, proposed))
            {
                extra.Add(new ProgramValidationIssue(
                    ValidationSeverity.Error,
                    null,
                    "Revision changed REPEAT/WORK/REST timings but the request did not ask for timing changes. "
                    + "Copy the timing lines exactly from the current script."));
            }

            if (req.IndexOf("colour", StringComparison.OrdinalIgnoreCase) < 0
                && req.IndexOf("color", StringComparison.OrdinalIgnoreCase) < 0)
            {
                foreach (StationDef original in baseline.Stations)
                {
                    StationDef match = proposed.Stations.FirstOrDefault(s =>
                        string.Equals(s.Name, original.Name, StringComparison.OrdinalIgnoreCase));
                    if (match != null
                        && !string.Equals(match.Colour ?? string.Empty, original.Colour ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        extra.Add(new ProgramValidationIssue(
                            ValidationSeverity.Error,
                            null,
                            "Revision changed or removed the COLOUR of station \"" + original.Name
                            + "\". Keep its COLOUR line exactly as in the current script."));
                    }
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

        private static readonly string[] FreshProgramPhrases =
        {
            "new program", "new workout", "new session", "new class", "from scratch", "start over",
            "start again", "brand new", "different program", "another program", "different workout",
            "another workout", "create a", "generate a"
        };

        private static bool IsFreshProgramRequest(string request)
        {
            string value = request ?? string.Empty;
            return FreshProgramPhrases.Any(p => value.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool MentionsMode(string request)
        {
            return Regex.IsMatch(request ?? string.Empty, @"\b(shared|sequential|parallel|circuit)\b", RegexOptions.IgnoreCase);
        }

        private static bool MentionsTiming(string request)
        {
            // "keep the rest the same" is not a request to change REST periods.
            string text = Regex.Replace(request ?? string.Empty, @"\bthe\s+rest\b", " ", RegexOptions.IgnoreCase);
            // "work out notes" / "work note" / "rest notes" are about notes, not timings.
            text = Regex.Replace(text, @"\b(work[\s-]*out|work\s*notes?|rest\s*notes?)\b", " ", RegexOptions.IgnoreCase);
            return Regex.IsMatch(
                text,
                @"\b(work|rest|recovery|move|second|seconds|sec|secs|minute|minutes|min|mins|duration|time|timing|timings|longer|shorter|round|rounds|repeat|repeats|interval|intervals|pyramid|between|\d+\s*s)\b",
                RegexOptions.IgnoreCase);
        }

        private static bool HasAddOrRemoveStationIntent(string request)
        {
            return Regex.IsMatch(
                request ?? string.Empty,
                @"\b(add|remove|delete|drop|extra|another|fewer|more)\b[^.]*\bstations?\b|\bstations?\b[^.]*\b(add|remove|delete|drop)\b",
                RegexOptions.IgnoreCase);
        }

        private static bool SameTimings(WorkoutProgram a, WorkoutProgram b)
        {
            return TimingSignature(a).SequenceEqual(TimingSignature(b));
        }

        private static IEnumerable<string> TimingSignature(WorkoutProgram program)
        {
            if (program.Mode == ExecutionMode.Parallel)
            {
                return program.BuildParallelSequences()
                    .SelectMany((lane, i) => lane.Select(x => i + ":" + x.Block.Type + ":" + x.Block.Seconds))
                    .ToList();
            }

            return program.BuildRuntimeSequence()
                .Where(x => x.Block != null)
                .Select(x => x.Block.Type + ":" + x.Block.Seconds)
                .ToList();
        }

        /// <summary>
        /// If the user names a station in quotes that doesn't exist in the current program,
        /// say so instead of letting the model guess (or rewrite everything).
        /// </summary>
        private static bool TryBuildUnknownStationReply(string request, WorkoutProgram baseline, out string reply)
        {
            reply = string.Empty;
            string text = request ?? string.Empty;
            if (baseline.Stations.Count == 0) return false;

            bool editIntent = Regex.IsMatch(text, @"\b(replace|replacing|swap|instead|remove|rename|change|different|substitute)\b", RegexOptions.IgnoreCase);
            if (!editIntent) return false;

            List<string> quoted = Regex.Matches(text, "[\"“”]([^\"“”]{2,80})[\"“”]")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Where(q => q.Length > 0 && !string.Equals(q, baseline.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (quoted.Count == 0) return false;

            bool anyMatches = quoted.Any(q => baseline.Stations.Any(s =>
                (s.Name ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            if (anyMatches) return false;

            reply = "I can't find a station called \"" + quoted[0] + "\" in the current program, so nothing has been changed."
                + Environment.NewLine + "The current stations are:"
                + Environment.NewLine + string.Join(Environment.NewLine, baseline.Stations.Select((s, i) => "  " + (i + 1) + ") " + s.Name))
                + Environment.NewLine + "Tell me which one to replace.";
            return true;
        }

        private static AiProgramProposal UnchangedProposal(string request, ProgramValidationResult baseline, string message)
        {
            return new AiProgramProposal
            {
                UserRequest = request,
                Script = baseline.Script,
                ParsedProgram = baseline.ParsedProgram,
                Issues = baseline.Issues,
                LeftUnchanged = true,
                AssistantMessage = message
            };
        }

        private static string DescribeTopIssues(ProgramValidationResult validation)
        {
            if (validation?.Issues == null) return string.Empty;
            return string.Join(" ", validation.Issues
                .Where(i => i.Severity == ValidationSeverity.Error)
                .Select(i => i.Message)
                .Distinct()
                .Take(2));
        }

        /// <summary>A short, human-readable list of what a revision changed, so changes are never silent.</summary>
        internal static string DescribeChanges(WorkoutProgram before, WorkoutProgram after)
        {
            if (before == null || after == null) return string.Empty;

            List<string> changes = new List<string>();
            if (before.Mode != after.Mode)
            {
                changes.Add("Mode: " + before.Mode.ToString().ToUpperInvariant() + " -> " + after.Mode.ToString().ToUpperInvariant());
            }
            if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
            {
                changes.Add("Name: \"" + before.Name + "\" -> \"" + after.Name + "\"");
            }
            if (!SameTimings(before, after))
            {
                changes.Add("Timings changed");
            }

            int common = Math.Min(before.Stations.Count, after.Stations.Count);
            for (int i = 0; i < common; i++)
            {
                StationDef b = before.Stations[i];
                StationDef a = after.Stations[i];
                if (!string.Equals(b.Name, a.Name, StringComparison.Ordinal))
                {
                    changes.Add("Station " + (i + 1) + ": \"" + b.Name + "\" -> \"" + a.Name + "\"");
                }
                if (!string.Equals(b.Colour ?? string.Empty, a.Colour ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add("Station " + (i + 1) + " colour: " + Blank(b.Colour) + " -> " + Blank(a.Colour));
                }
                if (!string.Equals(b.WorkInstruction ?? string.Empty, a.WorkInstruction ?? string.Empty, StringComparison.Ordinal))
                {
                    changes.Add("Station " + (i + 1) + " work note: \"" + (a.WorkInstruction ?? string.Empty) + "\"");
                }
                if (!string.Equals(b.RestInstruction ?? string.Empty, a.RestInstruction ?? string.Empty, StringComparison.Ordinal))
                {
                    changes.Add("Station " + (i + 1) + " rest note: \"" + (a.RestInstruction ?? string.Empty) + "\"");
                }
            }
            for (int i = common; i < after.Stations.Count; i++) changes.Add("Added station \"" + after.Stations[i].Name + "\"");
            for (int i = common; i < before.Stations.Count; i++) changes.Add("Removed station \"" + before.Stations[i].Name + "\"");

            if (changes.Count == 0) return "The proposal is identical to the current program.";
            return "Changes from the current program:" + Environment.NewLine + string.Join(Environment.NewLine, changes.Select(c => "  - " + c));
        }

        private static string Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
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

        private const string RevisionRules =
            "This is a REVISION of the current script. Make the smallest change that satisfies the request. " +
            "Copy everything the request does not mention exactly as it is: PROGRAM name, MODE, REPEAT counts, " +
            "every WORK/REST/RECOVERY/MOVE line, BETWEEN blocks, and every other STATION with its COLOUR, WORKNOTE and RESTNOTE lines. " +
            "When replacing a station, keep its position and its COLOUR line and change only its name (and notes if relevant). " +
            "If the user asks what you suggest, choose the best option yourself and apply it directly in the script. " +
            "Your reply must be the complete script only - never a prose answer.";

        private IReadOnlyList<AiChatMessage> BuildGenerationMessages(
            string request,
            string currentValidatedScript,
            bool isRevision,
            ConversationState conversation)
        {
            List<AiChatMessage> messages = new List<AiChatMessage>
            {
                new AiChatMessage(
                    AiChatRole.System,
                    BuildSystemInstruction() + (isRevision ? " " + RevisionRules : string.Empty),
                    DateTimeOffset.Now)
            };

            if (!string.IsNullOrWhiteSpace(currentValidatedScript))
            {
                messages.Add(new AiChatMessage(
                    AiChatRole.User,
                    (isRevision ? "Current script (revise this; keep everything not mentioned unchanged):\n" : "Current validated script to revise:\n")
                    + currentValidatedScript.Trim(),
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
            string revisionBaselineScript,
            ConversationState conversation)
        {
            bool isRevision = !string.IsNullOrWhiteSpace(revisionBaselineScript);
            StringBuilder sb = new StringBuilder();
            if (isRevision)
            {
                sb.AppendLine("Current script being revised (keep everything the request does not mention exactly as here):");
                sb.AppendLine(revisionBaselineScript.Trim());
                sb.AppendLine();
            }
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
                    AiChatRole.System,
                    BuildSystemInstruction() + (isRevision ? " " + RevisionRules : string.Empty),
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

        private static AiProgramProposal ToProposal(string userRequest, ProgramValidationResult validation, WorkoutProgram revisionBaseline = null)
        {
            return new AiProgramProposal
            {
                UserRequest = userRequest,
                Script = validation.Script,
                ParsedProgram = validation.ParsedProgram,
                Issues = validation.Issues,
                AssistantMessage = revisionBaseline != null ? DescribeChanges(revisionBaseline, validation.ParsedProgram) : string.Empty
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
