using System;

namespace GymClock.AI
{
    public enum AiChatRole
    {
        User,
        Assistant
    }

    public sealed record AiChatMessage(
        AiChatRole Role,
        string Content,
        DateTimeOffset CreatedAt);
}
