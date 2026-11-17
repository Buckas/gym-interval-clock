using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GymClock.Wpf;

namespace GymClock.Wpf
{
    /// <summary>One "jump to station" chip on the Session page.</summary>
    public sealed class StationChip
    {
        public int Index { get; set; }
        public string Label { get; set; }
        public ICommand Jump { get; set; }
    }

    /// <summary>One line of "Up next".</summary>
    public sealed class UpNextItem
    {
        public string Name { get; set; }
        public string Length { get; set; }
    }

    /// <summary>
    /// The Session page (build guide 7.1, mockup 4): what is running, the transport buttons, jump
    /// to a station, up next, sound and the Display. It reads the same DisplayFrame the clock
    /// draws, so the two always agree. Refreshed four times a second while the page is showing.
    /// </summary>
    public sealed class SessionViewModel : ObservableObject
    {
        private readonly AppSession _session;
        private readonly IDisplayActions _display;
        private readonly DispatcherTimer _timer;

        private string _programName = string.Empty, _description = string.Empty, _modeTag = string.Empty;
        private string _phaseTag = string.Empty, _countdown = string.Empty, _stationLine = string.Empty, _roundLine = string.Empty;
        private Brush _phaseBrush = Brushes.Gray;
        private double _progress;
        private bool _showProgress;
        private string _pauseText = "Start", _pauseGlyph = Icons.Play;
        private bool _isFollowing;
        private bool _soundOn;
        private double _volume;
        private string _screenText = string.Empty, _fullScreenText = "Full screen (F11)", _linkLine = string.Empty;
        private bool _hasOtherScreen;
        private bool _showStations;
        private string _stationsKey = string.Empty;
        private string _upNextKey = string.Empty;

        public SessionViewModel(AppSession session, IDisplayActions display)
        {
            _session = session;
            _display = display;

            Back = new RelayCommand(_ => Act(() => _session.Engine.GoBack()), _ => !_isFollowing);
            Pause = new RelayCommand(_ => Act(() => _session.Engine.TogglePause()), _ => !_isFollowing);
            Skip = new RelayCommand(_ => Act(() =>
            {
                if (_session.Engine.Phase == Phase.Idle) _session.Engine.Start(); else _session.Engine.Advance();
            }), _ => !_isFollowing && !_session.Engine.IsParallelActive);
            Reset = new RelayCommand(_ => Act(() => _session.Engine.Reset()), _ => !_isFollowing);
            MoveToNextScreen = new RelayCommand(_ => { _display.MoveToNextScreen(); Refresh(); }, _ => _hasOtherScreen);
            ToggleFullScreen = new RelayCommand(_ => { _display.ToggleFullScreen(); Refresh(); });

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += delegate { Refresh(); };
            Refresh();
        }

        // ---------------------------------------------------------------- what is running

        public string ProgramName { get { return _programName; } private set { Set(ref _programName, value); } }
        /// <summary>"Groups rotate, one clock · 4 stations · 19:00".</summary>
        public string Description { get { return _description; } private set { Set(ref _description, value); } }
        public string ModeTag { get { return _modeTag; } private set { Set(ref _modeTag, value); } }
        public string PhaseTag { get { return _phaseTag; } private set { Set(ref _phaseTag, value); } }
        /// <summary>The phase colour, as on the clock.</summary>
        public Brush PhaseBrush { get { return _phaseBrush; } private set { Set(ref _phaseBrush, value); } }
        public string Countdown { get { return _countdown; } private set { Set(ref _countdown, value); } }
        public string StationLine { get { return _stationLine; } private set { Set(ref _stationLine, value); } }
        public string RoundLine { get { return _roundLine; } private set { Set(ref _roundLine, value); } }
        public double Progress { get { return _progress; } private set { Set(ref _progress, value); } }
        public bool ShowProgress { get { return _showProgress; } private set { Set(ref _showProgress, value); } }

        // ---------------------------------------------------------------- transport

        public ICommand Back { get; }
        public ICommand Pause { get; }
        public ICommand Skip { get; }
        public ICommand Reset { get; }
        public string PauseText { get { return _pauseText; } private set { Set(ref _pauseText, value); } }
        public string PauseGlyph { get { return _pauseGlyph; } private set { Set(ref _pauseGlyph, value); } }

        /// <summary>True on a second screen: the transport and the chips are off.</summary>
        public bool IsFollowing { get { return _isFollowing; } private set { if (Set(ref _isFollowing, value)) CommandManager.InvalidateRequerySuggested(); } }

        public ObservableCollection<StationChip> Stations { get; } = new ObservableCollection<StationChip>();
        public bool ShowStations { get { return _showStations; } private set { Set(ref _showStations, value); } }
        public ObservableCollection<UpNextItem> UpNext { get; } = new ObservableCollection<UpNextItem>();

        // ---------------------------------------------------------------- sound and display

        public bool SoundOn
        {
            get { return _soundOn; }
            set
            {
                if (_soundOn == value) return;
                _soundOn = value;
                Raise();
                if (_isFollowing) _session.OwnSettings.LinkSound = value;
                else _session.Settings.SoundEnabled = value;
                _display.ApplySound();
                _session.SaveOwnSettings();
            }
        }

        /// <summary>Volume, 5 to 100 percent.</summary>
        public double Volume
        {
            get { return _volume; }
            set
            {
                double v = Math.Max(5, Math.Min(100, Math.Round(value)));
                if (Math.Abs(_volume - v) < 0.5) return;
                _volume = v;
                Raise();
                Raise(nameof(VolumeText));
                _session.OwnSettings.Volume = v / 100.0;
                if (!ReferenceEquals(_session.Settings, _session.OwnSettings)) _session.Settings.Volume = v / 100.0;
                _display.ApplySound();
                _saveVolumeSoon = true;
            }
        }

        private bool _saveVolumeSoon;

        public string VolumeText { get { return "Volume: " + _volume.ToString("0", CultureInfo.InvariantCulture) + "%"; } }

        public string ScreenText { get { return _screenText; } private set { Set(ref _screenText, value); } }
        public string FullScreenText { get { return _fullScreenText; } private set { Set(ref _fullScreenText, value); } }
        public string LinkLine { get { return _linkLine; } private set { Set(ref _linkLine, value); } }
        public ICommand MoveToNextScreen { get; }
        public ICommand ToggleFullScreen { get; }

        // ---------------------------------------------------------------- life

        /// <summary>Starts the four-times-a-second refresh (the page is showing).</summary>
        public void Start()
        {
            Refresh();
            _timer.Start();
        }

        /// <summary>Stops it (the page is hidden), saving a volume change.</summary>
        public void Stop()
        {
            _timer.Stop();
            if (_saveVolumeSoon) { _saveVolumeSoon = false; _session.SaveOwnSettings(); }
        }

        private void Act(Action action)
        {
            if (_isFollowing) return;
            action();
            Refresh();
        }

        public void Refresh()
        {
            SessionEngine engine = _session.Engine;
            IsFollowing = _session.Link.IsFollowing;
            if (_saveVolumeSoon && !_timer.IsEnabled) { _saveVolumeSoon = false; _session.SaveOwnSettings(); }

            DisplayFrame frame = _session.Display.BuildFrame(false);
            WorkoutProgram program = engine.Program;
            bool simple = !_session.Settings.UseBuiltProgram;

            ProgramName = program.Name;
            ModeTag = simple ? "SIMPLE SESSION" : ModeLabel(program.Mode).ToUpperInvariant();
            Description = DescribeProgram(engine, simple);

            bool running = engine.Phase == Phase.Prep || engine.Phase == Phase.Active;
            PhaseBrush = new SolidColorBrush(Color.FromRgb(frame.BaseBackground.R, frame.BaseBackground.G, frame.BaseBackground.B));
            if (frame.IsParallel)
            {
                PhaseTag = engine.IsPaused ? "PAUSED" : "RUNNING";
                Countdown = frame.RotationHeader;
                StationLine = frame.Tiles.Count + " stations, each with its own clock";
                RoundLine = string.Empty;
                ShowProgress = false;
            }
            else
            {
                PhaseTag = frame.PhaseLabel;
                Countdown = frame.BigNumber;
                StationLine = frame.Tip.Length > 0 && frame.Secondary.Length > 0 ? frame.Secondary + " · " + frame.Tip : frame.Secondary + frame.Tip;
                RoundLine = frame.RoundLine;
                ShowProgress = frame.ShowProgress;
                Progress = frame.ProgressFraction;
            }

            if (running && !engine.IsPaused) { PauseText = "Pause"; PauseGlyph = Icons.Pause; }
            else if (running) { PauseText = "Resume"; PauseGlyph = Icons.Play; }
            else { PauseText = "Start"; PauseGlyph = Icons.Play; }

            RefreshStations(engine, simple);
            RefreshUpNext(engine);

            _soundOn = _isFollowing ? _session.OwnSettings.LinkSound : _session.Settings.SoundEnabled;
            Raise(nameof(SoundOn));
            double volume = Math.Round(_session.OwnSettings.Volume * 100);
            if (Math.Abs(_volume - volume) >= 0.5) { _volume = volume; Raise(nameof(Volume)); Raise(nameof(VolumeText)); }

            ScreenText = "Display is on: " + _display.ScreenDescription;
            FullScreenText = _display.IsFullScreen ? "Window (F11)" : "Full screen (F11)";
            if (_hasOtherScreen != _display.HasOtherScreen) { _hasOtherScreen = _display.HasOtherScreen; CommandManager.InvalidateRequerySuggested(); }
            bool warning;
            string badge = _session.Link.BadgeText(true, out warning);
            LinkLine = badge.Length > 0 ? badge : "Not linked to other screens";
        }

        /// <summary>The user's four modes, in their words.</summary>
        public static string ModeLabel(ExecutionMode mode)
        {
            switch (mode)
            {
                case ExecutionMode.Shared: return "One clock, nobody moves";
                case ExecutionMode.Sequential: return "Whole class, one station at a time";
                case ExecutionMode.Parallel: return "Each station its own clock";
                case ExecutionMode.Circuit: return "Groups rotate, one clock";
                default: return mode.ToString();
            }
        }

        private static string DescribeProgram(SessionEngine engine, bool simple)
        {
            WorkoutProgram program = engine.Program;
            string mode = simple ? "Simple session" : ModeLabel(program.Mode);
            int stations = simple ? engine.Settings.Stations.Count : program.Stations.Count;
            string text = mode;
            if (stations > 0) text += " · " + stations + (stations == 1 ? " station" : " stations");

            int seconds = 0;
            if (engine.IsParallelActive || program.Mode == ExecutionMode.Parallel)
            {
                if (engine.ParallelPlan != null) seconds = engine.ParallelPlan.TotalSeconds;
            }
            else if (!program.Continuous)
            {
                foreach (RuntimeBlock block in engine.Sequence) seconds += block.Block.Seconds;
            }
            if (seconds > 0) text += " · " + DisplayModel.FormatClock(seconds);
            else if (program.Continuous) text += " · continuous";
            return text;
        }

        private void RefreshStations(SessionEngine engine, bool simple)
        {
            WorkoutProgram program = engine.Program;
            bool jumpable = !engine.IsParallelActive
                && (simple ? engine.Settings.Stations.Count > 0
                           : program.Mode == ExecutionMode.Sequential || program.Mode == ExecutionMode.Circuit);
            ShowStations = jumpable;

            string key = jumpable ? string.Join("|", simple ? engine.Settings.Stations.ConvertAll(s => s.Name) : program.Stations.ConvertAll(s => s.Name)) : string.Empty;
            if (key == _stationsKey) return;
            _stationsKey = key;
            Stations.Clear();
            if (!jumpable) return;

            int count = simple ? engine.Settings.Stations.Count : program.Stations.Count;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                string name = simple ? engine.Settings.Stations[i].Name : program.Stations[i].Name;
                Stations.Add(new StationChip
                {
                    Index = i,
                    Label = (i + 1) + " · " + name,
                    Jump = new RelayCommand(_ => Act(() => _session.Engine.JumpToStation(index)), _ => !_isFollowing),
                });
            }
        }

        /// <summary>WORK to Work, MOVE TO THE NEXT STATION to Move to the next station.</summary>
        private static string Capitalise(string word)
        {
            if (string.IsNullOrEmpty(word)) return string.Empty;
            string lower = word.ToLower(CultureInfo.CurrentCulture);
            return char.ToUpper(lower[0], CultureInfo.CurrentCulture) + lower.Substring(1);
        }

        private void RefreshUpNext(SessionEngine engine)
        {
            System.Collections.Generic.List<UpNextItem> items = new System.Collections.Generic.List<UpNextItem>();
            if (!engine.IsParallelActive && engine.Sequence.Count > 0 && engine.Phase != Phase.Finished)
            {
                int start = engine.Phase == Phase.Idle || engine.Phase == Phase.Prep ? engine.Index : engine.Index + 1;
                for (int i = start; i < start + 3; i++)
                {
                    if (!engine.Program.Continuous && i >= engine.Sequence.Count) break;
                    RuntimeBlock block = engine.BlockAt(i);
                    if (block == null) break;
                    string name = Capitalise(block.Block.Word);
                    if (block.StationIndex >= 0 && block.StationIndex < engine.Program.Stations.Count)
                        name += " · " + engine.Program.Stations[block.StationIndex].Name;
                    else if (block.RepeatTotal > 1)
                        name += " · round " + block.RepeatIndex;
                    items.Add(new UpNextItem { Name = name, Length = DisplayModel.FormatClock(block.Block.Seconds) });
                }
            }

            string key = string.Join("|", items.ConvertAll(x => x.Name + x.Length));
            if (key == _upNextKey) return;
            _upNextKey = key;
            UpNext.Clear();
            foreach (UpNextItem item in items) UpNext.Add(item);
        }
    }
}
