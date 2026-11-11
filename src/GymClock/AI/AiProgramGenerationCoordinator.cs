using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

            ConversationState state = conversation ?? new ConversationState();
            state.AddUser(request);

            IReadOnlyList<AiChatMessage> generationMessages = BuildGenerationMessages(request, currentValidatedScript, state);
            string first = await _aiService.GenerateTextAsync(generationMessages, cancellationToken).ConfigureAwait(false);
            string firstScript = CleanMarkdownFences(first);
            ProgramValidationResult firstValidation = _validator.Validate(firstScript);

            if (firstValidation.IsValid)
            {
                state.AddAssistant("Proposed a valid program script.");
                return ToProposal(request, firstValidation);
            }

            int maxRepairs = Math.Max(0, _options.MaximumAutomaticRepairAttempts);
            if (maxRepairs <= 0)
            {
                state.AddAssistant("Proposed script requires manual fixes.");
                return ToProposal(request, firstValidation);
            }

            string repaired = null;
            ProgramValidationResult repairValidation = firstValidation;

            for (int attempt = 0; attempt < maxRepairs; attempt++)
            {
                IReadOnlyList<AiChatMessage> repairMessages = BuildRepairMessages(request, firstScript, repairValidation.Issues, state);
                repaired = await _aiService.GenerateTextAsync(repairMessages, cancellationToken).ConfigureAwait(false);
                repaired = CleanMarkdownFences(repaired);
                repairValidation = _validator.Validate(repaired);

                if (repairValidation.IsValid)
                {
                    state.AddAssistant("Repaired proposal into a valid program script.");
                    return ToProposal(request, repairValidation);
                }

                firstScript = repaired;
            }

            state.AddAssistant("Proposed script remains invalid after one automatic repair.");
            return ToProposal(request, repairValidation);
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
                "Every block must be correctly closed according to the application's grammar. " +
                "In MODE SEQUENTIAL and MODE PARALLEL, each STATION must include at least one timed block (WORK/REST/RECOVERY/MOVE) before END STATION. " +
                "In MODE SHARED and MODE CIRCUIT, timed content may be defined in shared/repeat sections while stations can carry labels/colour/instructions. " +
                "Do not emit empty STATION or BETWEEN sections. " +
                "Ensure every STATION and BETWEEN has a matching END STATION or END BETWEEN.";
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
    }
}
