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

        public bool IsValid
        {
            get { return ParsedProgram != null && Issues.All(x => x.Severity != ValidationSeverity.Error); }
        }
    }
}
