using System;
using System.Collections.Generic;
using System.Linq;

namespace GymClock.AI
{
    public sealed class ConversationState
    {
        private readonly List<AiChatMessage> _messages = new List<AiChatMessage>();

        public IReadOnlyList<AiChatMessage> Messages
        {
            get { return _messages; }
        }

        /// <summary>Replacement options offered for a station, waiting for the user to pick one.</summary>
        public PendingStationSuggestion PendingSuggestion { get; set; }

        /// <summary>Exercise names from the last "give me N exercises" answer (for "more" and "use these").</summary>
        public List<string> LastExerciseIdeas { get; set; } = new List<string>();

        public void Clear()
        {
            _messages.Clear();
            PendingSuggestion = null;
            LastExerciseIdeas = new List<string>();
        }

        public void AddUser(string content)
        {
            Add(AiChatRole.User, content);
        }

        public void AddAssistant(string content)
        {
            Add(AiChatRole.Assistant, content);
        }

        public IReadOnlyList<AiChatMessage> TakeRecent(int maxMessages)
        {
            if (maxMessages <= 0) return Array.Empty<AiChatMessage>();
            if (_messages.Count <= maxMessages) return _messages.ToArray();
            return _messages.Skip(_messages.Count - maxMessages).ToArray();
        }

        private void Add(AiChatRole role, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            _messages.Add(new AiChatMessage(role, content.Trim(), DateTimeOffset.Now));
        }
    }

    public sealed record StationOption(string Name, string Note);

    public sealed class PendingStationSuggestion
    {
        public string StationName { get; init; } = string.Empty;
        public IReadOnlyList<StationOption> Options { get; init; } = Array.Empty<StationOption>();
    }
}
