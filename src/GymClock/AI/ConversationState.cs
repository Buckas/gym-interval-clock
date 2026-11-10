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

        public void Clear()
        {
            _messages.Clear();
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
}
