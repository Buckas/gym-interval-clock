namespace GymClock.AI
{
    public sealed record ProgramValidationIssue(
        ValidationSeverity Severity,
        int? LineNumber,
        string Message);
}
