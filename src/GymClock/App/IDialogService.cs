namespace GymClock.Wpf
{
    public enum InfoKind { Info, Success, Warning, Error }

    /// <summary>
    /// Everything a page asks the teacher (build guide P4.1). Pages never call MessageBox or a
    /// file dialog themselves; they ask this, so the tests can answer for the teacher with a fake.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>A question with two answers. True for <paramref name="yesText"/>.</summary>
        bool Confirm(string title, string message, string yesText, string noText);

        /// <summary>A message with an OK button.</summary>
        void Info(string title, string message);

        /// <summary>Asks for one line of text. Null when cancelled.</summary>
        string Prompt(string title, string label, string initial);

        /// <summary>Windows' own open-file dialog. Null when cancelled.</summary>
        string PickOpenFile(string title, string filter);

        /// <summary>Windows' own save-file dialog. Null when cancelled.</summary>
        string PickSaveFile(string title, string filter, string suggestedName);

        /// <summary>Picks a colour: 24 swatches and a hex box. Returns "#RRGGBB", or null when cancelled.</summary>
        string PickColour(string title, string current);

        /// <summary>A short message in the bar at the top of the page; it goes by itself after a few seconds.</summary>
        void Notify(string message, InfoKind kind);

        /// <summary>Puts text on the clipboard. False if Windows would not allow it.</summary>
        bool CopyText(string text);

        /// <summary>Opens a web page, a mailto: link or a Windows screen. False if nothing could open it.</summary>
        bool Open(string target, string arguments = null);
    }
}
