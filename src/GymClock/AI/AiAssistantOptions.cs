using System;

namespace GymClock.AI
{
    public sealed class AiAssistantOptions
    {
        public string LocalModelAlias { get; init; } = "phi-4-mini";
        public int MaximumConversationMessages { get; init; } = 8;
        public TimeSpan GenerationTimeout { get; init; } = TimeSpan.FromMinutes(2);
        public int MaximumAutomaticRepairAttempts { get; init; } = 1;
    }
}
