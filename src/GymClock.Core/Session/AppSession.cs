using System;

namespace GymClock
{
    /// <summary>
    /// The one object every window shares: the settings in force, the timing engine, the display
    /// model and the link. A window reads from it and asks it to do things; it owns no window.
    ///
    /// Two sets of settings matter on a second screen (linked displays): <see cref="Settings"/> is
    /// what is showing - the main PC's while following - and <see cref="OwnSettings"/> is this PC's
    /// own, the only one ever saved to its settings file.
    /// </summary>
    public sealed class AppSession : IDisposable
    {
        private TimerSettings _settings;
        private TimerSettings _ownSettings;   // set only while the main PC's settings are showing

        public AppSession(TimerSettings settings, ICuePlayer cues)
        {
            _settings = settings ?? new TimerSettings();
            Engine = new SessionEngine(_settings, cues);
            Display = new DisplayModel(Engine);
            Link = new LinkController(this);
        }

        public SessionEngine Engine { get; }
        public DisplayModel Display { get; }
        public LinkController Link { get; }

        /// <summary>The settings in force: this PC's own, or the main PC's while following it.</summary>
        public TimerSettings Settings
        {
            get { return _settings; }
        }

        /// <summary>The settings that belong to this PC (and are saved to its settings file).</summary>
        public TimerSettings OwnSettings
        {
            get { return _ownSettings ?? _settings; }
        }

        /// <summary>True while the main PC's settings are showing in place of this PC's own.</summary>
        public bool ShowingLeaderSettings
        {
            get { return _ownSettings != null; }
        }

        /// <summary>
        /// Raised after the settings in force change (edited, received from a main PC, or restored),
        /// once the engine has taken them. The window applies what is its own business: sound and
        /// always-on-top.
        /// </summary>
        public event Action SettingsApplied;

        private void ApplyLive()
        {
            Engine.ApplySettings(_settings);   // rebuilds the program and keeps a running session inside it
            Action handler = SettingsApplied;
            if (handler != null) handler();
        }

        /// <summary>
        /// Takes settings edited in the settings window. Link settings are not the editor's to
        /// change (the Link displays window applies those live), so the current ones are kept.
        /// Returns false on a second screen, where the edits are saved as this PC's own settings
        /// for later and what is showing - the main PC's session - is left alone.
        /// </summary>
        public bool ReplaceSettings(TimerSettings edited, bool reset)
        {
            if (edited == null) return false;

            if (_ownSettings != null)
            {
                edited.CopyLinkFrom(_ownSettings);
                _ownSettings = edited;
                _ownSettings.Save();
                return false;
            }

            edited.CopyLinkFrom(_settings);
            _settings = edited;
            _settings.Save();
            ApplyLive();
            if (reset) Engine.Reset();
            return true;
        }

        /// <summary>Second screen: show the main PC's settings and program, keeping this PC's own aside.</summary>
        internal void ShowLeaderSettings(TimerSettings leaderSettings)
        {
            if (_ownSettings == null) _ownSettings = _settings;
            _settings = leaderSettings;
            ApplyLive();
        }

        /// <summary>Second screen stops following: back to this PC's own settings and program.</summary>
        internal void RestoreOwnSettings(bool applyAndReset)
        {
            if (_ownSettings == null) return;
            _settings = _ownSettings;
            _ownSettings = null;
            if (applyAndReset)
            {
                ApplyLive();
                Engine.Reset();
            }
        }

        /// <summary>One step of the clock: the engine first, then the link tells any second screens.</summary>
        public void Tick()
        {
            Engine.Tick();
            Link.Tick();
        }

        public void SaveOwnSettings()
        {
            OwnSettings.Save();
        }

        public void Dispose()
        {
            Link.Dispose();
        }
    }
}
