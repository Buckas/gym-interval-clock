using System;

namespace GymClock.AI
{
    public sealed class LocalAiSetupException : Exception
    {
        public LocalAiSetupException(string message)
            : base(message)
        {
        }

        public LocalAiSetupException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
