using System;
using System.Collections.Generic;
using System.Linq;

namespace GymClock.AI
{
    public sealed class ProgramValidationResult
    {
        public string Script { get; init; } = string.Empty;
        public WorkoutProgram ParsedProgram { get; init; }
        public IReadOnlyList<ProgramValidationIssue> Issues { get; init; } = Array.Empty<ProgramValidationIssue>();

        public bool IsValid
        {
            get { return ParsedProgram != null && Issues.All(x => x.Severity != ValidationSeverity.Error); }
        }
    }
}
