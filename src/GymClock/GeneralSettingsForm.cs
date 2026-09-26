using System;
using System.Globalization;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// Everything that's genuinely independent of what workout is running -
    /// prep countdown, sound, clock, and how the station panel displays. Its
    /// own separate window, opened from the Program Editor's "General
    /// Settings..." button, rather than living inside the program-editing
    /// screen where it would just compete for attention with the actual
    /// program content.
    /// </summary>
    public partial class GeneralSettingsForm : Form
    {
        public int PrepSeconds { get; private set; }
        public double Volume { get; private set; }
        public string ClockFormat { get; private set; }
        public bool SoundEnabled { get; private set; }
        public bool ShowClock { get; private set; }
        public bool AlwaysOnTop { get; private set; }
        public bool ShowStationsTable { get; private set; }
        public bool ShowStationNameAsDescription { get; private set; }

        /// <summary>
        /// Parameterless constructor required by the Windows Forms designer at design time.
        /// Not intended for runtime use.
        /// </summary>
        public GeneralSettingsForm()
            : this(new TimerSettings())
        {
        }

        public GeneralSettingsForm(TimerSettings current)
        {
            InitializeComponent();

            _pathLabel.Text = "Settings file (editable in Notepad):" + Environment.NewLine + TimerSettings.FilePath;

            _prep.Minimum = 0;
            _prep.Maximum = 300;
            _prep.Value = Clamp(current.PrepSeconds, _prep);

            _volume.Minimum = 5;
            _volume.Maximum = 100;
            _volume.Value = Clamp((int)Math.Round(current.Volume * 100), _volume);

            _sound.Checked = current.SoundEnabled;
            _clockFormat.Text = current.ClockFormat;
            _showClock.Checked = current.ShowClock;
            _onTop.Checked = current.AlwaysOnTop;
            _showStationsTable.Checked = current.ShowStationsTable;
            _showStationNameAsDescription.Checked = current.ShowStationNameAsDescription;
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            PrepSeconds = (int)_prep.Value;
            Volume = (double)_volume.Value / 100.0;
            SoundEnabled = _sound.Checked;
            ShowClock = _showClock.Checked;
            AlwaysOnTop = _onTop.Checked;
            ShowStationsTable = _showStationsTable.Checked;
            ShowStationNameAsDescription = _showStationNameAsDescription.Checked;

            string format = _clockFormat.Text.Trim();
            if (format.Length > 0)
            {
                try
                {
                    DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
                    ClockFormat = format;
                }
                catch
                {
                    ClockFormat = "h:mm:ss tt";
                }
            }
            else
            {
                ClockFormat = "h:mm:ss tt";
            }
        }

        private static decimal Clamp(int value, NumericUpDown target)
        {
            decimal v = value;
            if (v < target.Minimum) return target.Minimum;
            if (v > target.Maximum) return target.Maximum;
            return v;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
    }
}
