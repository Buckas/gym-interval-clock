using System.Collections.Generic;
using GymClock.Wpf;

namespace GymClock.Tests
{
    /// <summary>
    /// Answers the pages' questions for the teacher in tests. Set the answers first; everything
    /// asked or shown is recorded.
    /// </summary>
    internal sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmAnswer = true;
        public string PromptAnswer;
        public string OpenFileAnswer;
        public string SaveFileAnswer;
        public string ColourAnswer;
        public bool OpenSucceeds = true;

        public readonly List<string> Shown = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public readonly List<string> Opened = new List<string>();
        public string Clipboard;

        public bool Confirm(string title, string message, string yesText, string noText) { Shown.Add(message); return ConfirmAnswer; }
        public void Info(string title, string message) { Shown.Add(message); }
        public string Prompt(string title, string label, string initial) { Shown.Add(label); return PromptAnswer; }
        public string PickOpenFile(string title, string filter) { return OpenFileAnswer; }
        public string PickSaveFile(string title, string filter, string suggestedName) { return SaveFileAnswer; }
        public string PickColour(string title, string current) { return ColourAnswer; }
        public void Notify(string message, InfoKind kind) { Notes.Add(message); }
        public bool CopyText(string text) { Clipboard = text; return true; }
        public bool Open(string target, string arguments = null) { Opened.Add(target); return OpenSucceeds; }
    }
}
