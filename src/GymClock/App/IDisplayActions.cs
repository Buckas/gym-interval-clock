namespace GymClock.Wpf
{
    /// <summary>What the Control window may ask of the clock window (the Session page's Display card).</summary>
    public interface IDisplayActions
    {
        /// <summary>"Projector · 1920 × 1080": the screen the clock is on.</summary>
        string ScreenDescription { get; }

        bool IsFullScreen { get; }

        /// <summary>More than one screen, so F6 has somewhere to go.</summary>
        bool HasOtherScreen { get; }

        void MoveToNextScreen();

        void ToggleFullScreen();

        /// <summary>Applies the sound settings (on/off, volume) to the cue player, as the M key does.</summary>
        void ApplySound();
    }
}
