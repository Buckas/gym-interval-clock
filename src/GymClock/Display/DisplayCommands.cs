using System;
using System.Windows.Input;

namespace GymClock.Wpf
{
    /// <summary>
    /// The control bar's buttons (mockup 2): the same actions as the clock's keys, with the
    /// labels that change (Start / Pause / Resume, sound on or off, full screen or window).
    /// The Display window supplies the actions and calls <see cref="Refresh"/> every tick.
    /// </summary>
    public sealed class DisplayCommands : ObservableObject
    {
        private string _pauseText = "Start";
        private string _pauseGlyph = Icons.Play;
        private string _soundGlyph = Icons.SoundOn;
        private string _soundText = "Sound";
        private string _fullScreenText = "Window";
        private string _fullScreenGlyph = Icons.ExitFullScreen;
        private bool _canSkip = true;

        public DisplayCommands(Action back, Action pause, Action skip, Action reset, Action sound, Action link, Action controls, Action fullScreen)
        {
            Back = new RelayCommand(_ => back());
            Pause = new RelayCommand(_ => pause());
            Skip = new RelayCommand(_ => skip(), _ => _canSkip);
            Reset = new RelayCommand(_ => reset());
            Sound = new RelayCommand(_ => sound());
            Link = new RelayCommand(_ => link());
            Controls = new RelayCommand(_ => controls());
            FullScreen = new RelayCommand(_ => fullScreen());
        }

        public ICommand Back { get; }
        public ICommand Pause { get; }
        public ICommand Skip { get; }
        public ICommand Reset { get; }
        public ICommand Sound { get; }
        public ICommand Link { get; }
        public ICommand Controls { get; }
        public ICommand FullScreen { get; }

        public string PauseText { get { return _pauseText; } private set { Set(ref _pauseText, value); } }
        public string PauseGlyph { get { return _pauseGlyph; } private set { Set(ref _pauseGlyph, value); } }
        public string SoundText { get { return _soundText; } private set { Set(ref _soundText, value); } }
        public string SoundGlyph { get { return _soundGlyph; } private set { Set(ref _soundGlyph, value); } }
        public string FullScreenText { get { return _fullScreenText; } private set { Set(ref _fullScreenText, value); } }
        public string FullScreenGlyph { get { return _fullScreenGlyph; } private set { Set(ref _fullScreenGlyph, value); } }

        /// <summary>Skip is off while a Parallel program runs: every station has its own clock.</summary>
        public bool CanSkip
        {
            get { return _canSkip; }
            private set
            {
                if (Set(ref _canSkip, value)) CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Brings the labels up to date with the session.</summary>
        public void Refresh(SessionEngine engine, bool soundOn, bool fullScreen)
        {
            bool running = engine.Phase == Phase.Prep || engine.Phase == Phase.Active;
            if (running && !engine.IsPaused) { PauseText = "Pause"; PauseGlyph = Icons.Pause; }
            else if (running) { PauseText = "Resume"; PauseGlyph = Icons.Play; }
            else { PauseText = "Start"; PauseGlyph = Icons.Play; }

            SoundText = soundOn ? "Sound on" : "Sound off";
            SoundGlyph = soundOn ? Icons.SoundOn : Icons.SoundOff;
            FullScreenText = fullScreen ? "Window" : "Full screen";
            FullScreenGlyph = fullScreen ? Icons.ExitFullScreen : Icons.FullScreen;
            CanSkip = !engine.IsParallelActive;
        }
    }
}
