using System;
using System.Collections.Generic;
using System.Linq;

namespace GymClock.AI
{
    public sealed class AiProgramProposal
    {
        public required string UserRequest { get; init; }
        public required string Script { get; init; }
        public WorkoutProgram ParsedProgram { get; init; }
        public IReadOnlyList<ProgramValidationIssue> Issues { get; init; } = Array.Empty<ProgramValidationIssue>();
        public bool RequiresClarification { get; init; }
        public string AssistantMessage { get; init; } = string.Empty;

        /// <summary>
        /// True when the assistant deliberately left the current program untouched
        /// (e.g. it could not produce a safe revision). The UI must not replace the draft.
        /// </summary>
        public bool LeftUnchanged { get; init; }

        public bool IsValid
        {
            get { return ParsedProgram != null && Issues.All(x => x.Severity != ValidationSeverity.Error); }
        }
    }
}
