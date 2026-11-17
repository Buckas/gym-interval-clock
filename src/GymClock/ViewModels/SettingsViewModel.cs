using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GymClock.Wpf
{
    /// <summary>One phase colour button on the Settings page.</summary>
    public sealed class ColourItem : ObservableObject
    {
        private string _hex;

        public ColourItem(string name, Func<TimerSettings, string> get, Action<TimerSettings, string> set)
        {
            Name = name;
            Get = get;
            Put = set;
        }

        public string Name { get; }
        internal Func<TimerSettings, string> Get { get; }
        internal Action<TimerSettings, string> Put { get; }
        public ICommand Pick { get; internal set; }

        public string Hex
        {
            get { return _hex; }
            set
            {
                if (!Set(ref _hex, value)) return;
                Raise(nameof(Fill));
                Raise(nameof(Ink));
            }
        }

        public Brush Fill
        {
            get
            {
                System.Drawing.Color c = TimerSettings.ParseColour(_hex, System.Drawing.Color.Black);
                return new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            }
        }

        /// <summary>White or black, whichever reads better on the colour.</summary>
        public Brush Ink
        {
            get
            {
                System.Drawing.Color c = TimerSettings.ParseColour(_hex, System.Drawing.Color.Black);
                return DisplayModel.ReadableInkFor(c) == System.Drawing.Color.Black ? Brushes.Black : Brushes.White;
            }
        }
    }

    /// <summary>
    /// The Settings page (build guide 7.2, mockup 9). It edits a TimerSettings and hands it to
    /// <c>commit</c> a moment after each change (so a slider being dragged is one save, not
    /// fifty). In the Control window commit applies the settings to the clock at once; from the
    /// old program editor it is null and the editor copies the result back when the window closes.
    /// </summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        public static readonly string[] ClockFormats = { "h:mm tt", "H:mm", "h:mm:ss tt", "H:mm:ss" };

        private readonly Action<TimerSettings> _commit;
        private readonly IDialogService _dialogs;
        private readonly DispatcherTimer _soon;
        private TimerSettings _edit;
        private bool _loading;
        private string _clockFormatText = string.Empty;

        public SettingsViewModel(TimerSettings edit, Action<TimerSettings> commit, IDialogService dialogs)
        {
            _commit = commit;
            _dialogs = dialogs;
            _soon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _soon.Tick += delegate { Flush(); };

            Colours = new ObservableCollection<ColourItem>
            {
                new ColourItem("Work", s => s.WorkColour, (s, v) => s.WorkColour = v),
                new ColourItem("Rest", s => s.RestColour, (s, v) => s.RestColour = v),
                new ColourItem("Recovery", s => s.RecoveryColour, (s, v) => s.RecoveryColour = v),
                new ColourItem("Move", s => s.MoveColour, (s, v) => s.MoveColour = v),
                new ColourItem("Get ready", s => s.PrepColour, (s, v) => s.PrepColour = v),
                new ColourItem("Countdown", s => s.CountdownColour, (s, v) => s.CountdownColour = v),
                new ColourItem("Water break", s => s.WaterBreakColour, (s, v) => s.WaterBreakColour = v),
                new ColourItem("Instruction", s => s.InstructionColour, (s, v) => s.InstructionColour = v),
                new ColourItem("Custom", s => s.CustomColour, (s, v) => s.CustomColour = v),
                new ColourItem("Paused", s => s.PausedColour, (s, v) => s.PausedColour = v),
                new ColourItem("Finished", s => s.DoneColour, (s, v) => s.DoneColour = v),
                new ColourItem("Idle", s => s.IdleColour, (s, v) => s.IdleColour = v),
            };
            foreach (ColourItem item in Colours)
            {
                ColourItem current = item;
                current.Pick = new RelayCommand(_ => PickColour(current));
            }
            ResetColours = new RelayCommand(_ => ResetAllColours());

            Load(edit);
        }

        /// <summary>The settings being edited (with every change so far).</summary>
        public TimerSettings Edited
        {
            get { return _edit; }
        }

        /// <summary>Shows these settings (the page is opened, or they changed elsewhere, e.g. M on the clock).</summary>
        public void Load(TimerSettings settings)
        {
            _soon.Stop();
            _loading = true;
            _edit = settings.Clone();
            foreach (ColourItem item in Colours) item.Hex = item.Get(_edit);
            _clockFormatText = _edit.ClockFormat;
            _loading = false;
            Raise(string.Empty);    // every property
        }

        /// <summary>Hands any change still waiting to <c>commit</c> (the page is closing).</summary>
        public void Flush()
        {
            _soon.Stop();
            if (_dirty && _commit != null) _commit(_edit.Clone());
            _dirty = false;
        }

        private bool _dirty;

        private void Changed([System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            Raise(name);
            if (_loading) return;
            _dirty = true;
            if (_commit == null) return;
            _soon.Stop();
            _soon.Start();
        }

        // ================================================================ display layout

        /// <summary>0 Standard, 1 Big number, 2 Stations.</summary>
        public int LayoutIndex
        {
            get { return _edit.DisplayLayout == "bignumber" ? 1 : _edit.DisplayLayout == "stations" ? 2 : 0; }
            set { _edit.DisplayLayout = value == 1 ? "bignumber" : value == 2 ? "stations" : "standard"; Changed(); }
        }

        public int SafeMargin
        {
            get { return _edit.SafeMargin; }
            set { int v = Math.Max(0, Math.Min(8, value)); if (v == _edit.SafeMargin) return; _edit.SafeMargin = v; Changed(); Raise(nameof(SafeMarginText)); }
        }

        public string SafeMarginText
        {
            get { return "Safe margin for projectors that cut off the edges: " + _edit.SafeMargin + "%"; }
        }

        public bool ControlBar
        {
            get { return _edit.ControlBar; }
            set { _edit.ControlBar = value; Changed(); }
        }

        /// <summary>Ticked = on. Unticked = follow Windows' animation setting.</summary>
        public bool ReduceMotion
        {
            get { return _edit.ReduceMotion == "on"; }
            set { _edit.ReduceMotion = value ? "on" : "system"; Changed(); }
        }

        // ================================================================ colours

        public ObservableCollection<ColourItem> Colours { get; }
        public ICommand ResetColours { get; }

        private void PickColour(ColourItem item)
        {
            string picked = _dialogs.PickColour(item.Name + " colour", item.Hex);
            if (picked == null) return;
            item.Hex = picked;
            item.Put(_edit, picked);
            Changed(nameof(Colours));
        }

        private void ResetAllColours()
        {
            if (!_dialogs.Confirm("Reset the colours", "Put every phase colour back to how the app came?", "Reset", "Cancel")) return;
            TimerSettings defaults = new TimerSettings();
            foreach (ColourItem item in Colours)
            {
                string value = item.Get(defaults);
                item.Hex = value;
                item.Put(_edit, value);
            }
            Changed(nameof(Colours));
        }

        // ================================================================ appearance

        /// <summary>0 Follow Windows, 1 Light, 2 Dark.</summary>
        public int ThemeIndex
        {
            get { return _edit.Theme == "light" ? 1 : _edit.Theme == "dark" ? 2 : 0; }
            set
            {
                _edit.Theme = value == 1 ? "light" : value == 2 ? "dark" : "system";
                Changed();
                ThemeService.Apply(_edit.Theme);    // seen at once, in this window too
            }
        }

        // ================================================================ sound

        public bool SoundEnabled
        {
            get { return _edit.SoundEnabled; }
            set { _edit.SoundEnabled = value; Changed(); }
        }

        /// <summary>5 to 100 percent.</summary>
        public double Volume
        {
            get { return Math.Round(_edit.Volume * 100); }
            set
            {
                double v = Math.Max(5, Math.Min(100, Math.Round(value)));
                if (Math.Abs(v - Volume) < 0.5) return;
                _edit.Volume = v / 100.0;
                Changed();
                Raise(nameof(VolumeText));
            }
        }

        public string VolumeText
        {
            get { return "Volume: " + Volume.ToString("0", CultureInfo.InvariantCulture) + "%"; }
        }

        /// <summary>The get-ready countdown, 0 to 300 seconds.</summary>
        public int PrepSeconds
        {
            get { return _edit.PrepSeconds; }
            set { int v = Math.Max(0, Math.Min(300, value)); if (v == _edit.PrepSeconds) return; _edit.PrepSeconds = v; Changed(); }
        }

        // ================================================================ clock and stations

        public bool ShowClock
        {
            get { return _edit.ShowClock; }
            set { _edit.ShowClock = value; Changed(); }
        }

        /// <summary>The time format as typed (one of <see cref="ClockFormats"/>, or the teacher's own).</summary>
        public string ClockFormat
        {
            get { return _clockFormatText; }
            set
            {
                _clockFormatText = value ?? string.Empty;
                Raise();
                Raise(nameof(ClockExample));
                string format = _clockFormatText.Trim();
                if (!IsValidClockFormat(format)) return;     // keep the last good one until it reads properly
                if (format == _edit.ClockFormat) return;
                _edit.ClockFormat = format;
                Changed(nameof(ClockFormat));
            }
        }

        public string ClockExample
        {
            get
            {
                string format = _clockFormatText.Trim();
                return IsValidClockFormat(format)
                    ? "Shows as " + DateTime.Now.ToString(format, CultureInfo.CurrentCulture)
                    : "That format can't be read - the clock keeps " + DateTime.Now.ToString(_edit.ClockFormat, CultureInfo.CurrentCulture);
            }
        }

        public static bool IsValidClockFormat(string format)
        {
            if (string.IsNullOrWhiteSpace(format)) return false;
            try
            {
                string shown = DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
                return shown.Length > 0 && shown != format;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool AlwaysOnTop
        {
            get { return _edit.AlwaysOnTop; }
            set { _edit.AlwaysOnTop = value; Changed(); }
        }

        public bool ShowStationsTable
        {
            get { return _edit.ShowStationsTable; }
            set { _edit.ShowStationsTable = value; Changed(); }
        }

        public bool ShowStationNameAsDescription
        {
            get { return _edit.ShowStationNameAsDescription; }
            set { _edit.ShowStationNameAsDescription = value; Changed(); }
        }

        public string SettingsFileText
        {
            get { return "Settings file (it can also be edited in Notepad): " + TimerSettings.FilePath; }
        }
    }
}
