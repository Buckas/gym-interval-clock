using System;

namespace GymClock.AI
{
    public sealed class AiAssistantOptions
    {
        public string LocalModelAlias { get; init; } = "phi-4-mini";
        public int MaximumConversationMessages { get; init; } = 8;
        public TimeSpan GenerationTimeout { get; init; } = TimeSpan.FromMinutes(4);
        public int MaximumAutomaticRepairAttempts { get; init; } = 1;
        public int MaximumGenerationAttempts { get; init; } = 1;
        public bool DeveloperDiagnostics { get; init; } = IsDeveloperDiagnosticsEnabled();

        private static bool IsDeveloperDiagnosticsEnabled()
        {
            string value = Environment.GetEnvironmentVariable("GYMCLOCK_DEV_AI_ERRORS");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
