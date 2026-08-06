using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// Opened at any time with S or F2. Changes take effect the moment you press OK,
    /// even mid-interval, so you can shorten a round on the fly.
    /// </summary>
    public partial class SettingsForm : Form
    {
        private readonly TimerSettings _original;

        public TimerSettings Result { get; private set; }

        /// <summary>
        /// Parameterless constructor required by the Windows Forms designer at design time.
        /// Not intended to be used at runtime.
        /// </summary>
        public SettingsForm()
            : this(new TimerSettings())
        {
        }

        public SettingsForm(TimerSettings settings)
        {
            _original = settings;
            Result = settings.Clone();

            InitializeComponent();

            // The designer bakes in whatever path the developer's machine had, so
            // this must be set at runtime or every user sees somebody else's profile.
            _pathLabel.Text = "Settings file (editable in Notepad):"
                + Environment.NewLine + TimerSettings.FilePath;

            _prep.Minimum = 0;
            _prep.Maximum = 300;
            _prep.Value = Clamp(settings.PrepSeconds, _prep);

            _work.Minimum = 1;
            _work.Maximum = 3600;
            _plan.Text = settings.Plan ?? string.Empty;
            _plan.TextChanged += delegate { UpdatePlanPreview(); };
            _prep.ValueChanged += delegate { UpdatePlanPreview(); };
            _work.ValueChanged += delegate { UpdatePlanPreview(); };
            _rest.ValueChanged += delegate { UpdatePlanPreview(); };
            _rounds.ValueChanged += delegate { UpdatePlanPreview(); };

            _work.Value = Clamp(settings.WorkSeconds, _work);

            _rest.Minimum = 0;
            _rest.Maximum = 3600;
            _rest.Value = Clamp(settings.RestSeconds, _rest);

            _rounds.Minimum = 0;
            _rounds.Maximum = 999;
            _rounds.Value = Clamp(settings.Rounds, _rounds);

            _volume.Minimum = 5;
            _volume.Maximum = 100;
            _volume.Value = Clamp((int)Math.Round(settings.Volume * 100), _volume);

            _workLabel.Text = settings.WorkLabel;
            _restLabel.Text = settings.RestLabel;
            _stations.Text = string.Join(", ", settings.Stations.ToArray());
            _stations.PlaceholderTextSafe("Burpees, Squats, Push-ups, ...");

            _clockFormat.Text = settings.ClockFormat;

            _restAfterFinal.Checked = settings.RestAfterFinalRound;
            _sound.Checked = settings.SoundEnabled;
            _showClock.Checked = settings.ShowClock;
            _onTop.Checked = settings.AlwaysOnTop;

            UpdatePlanPreview();

            Button[] presetButtons = new[]
            {
                _presetButton1, _presetButton2, _presetButton3, _presetButton4, _presetButton5,
                _presetButton6, _presetButton7, _presetButton8, _presetButton9, _presetButton10
            };

            for (int i = 0; i < presetButtons.Length; i++)
            {
                Button button = presetButtons[i];
                if (i < settings.Presets.Count)
                {
                    Preset preset = settings.Presets[i];
                    button.Text = (i + 1) + ": " + preset.Label;
                    button.Tag = preset;
                    button.Visible = true;
                }
                else
                {
                    button.Visible = false;
                }
            }

            }

        /// <summary>
        /// Shows what the plan actually resolves to, so a typo is obvious before you
        /// find out in front of a class.
        /// </summary>
        private void UpdatePlanPreview()
        {
            string text = _plan.Text.Trim();

            if (text.Length == 0)
            {
                IntervalPlan uniform = IntervalPlan.Uniform((int)_work.Value, (int)_rest.Value,
                    Math.Max(1, (int)_rounds.Value));

                _planPreview.Text = "Even session: " + uniform.Summary() + "   ->   "
                    + uniform.RoundCount + " rounds, "
                    + IntervalPlan.FormatDuration(uniform.TotalSeconds((int)_prep.Value, _restAfterFinal.Checked))
                    + ((int)_rounds.Value == 0 ? ", repeating continuously" : string.Empty);
                return;
            }

            IntervalPlan plan = IntervalPlan.Parse(text);

            if (plan.IsEmpty)
            {
                _planPreview.Text = "Not understood. Use blocks like  45/15x2 + 50/10x4";
                return;
            }

            _planPreview.Text = plan.Summary() + "   ->   "
                + plan.RoundCount + " rounds, "
                + IntervalPlan.FormatDuration(plan.TotalSeconds((int)_prep.Value, _restAfterFinal.Checked))
                + "   (overrides work, rest and rounds above)";
        }

        private void Preset_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;

            Preset preset = button.Tag as Preset;
            if (preset == null) return;

            _plan.Text = string.Empty;
            _work.Value = Clamp(preset.Work, _work);
            _rest.Value = Clamp(preset.Rest, _rest);
            if (preset.Rounds > 0) _rounds.Value = Clamp(preset.Rounds, _rounds);
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            Result = _original.Clone();

            Result.PrepSeconds = (int)_prep.Value;
            Result.WorkSeconds = Math.Max(1, (int)_work.Value);
            Result.RestSeconds = (int)_rest.Value;
            Result.Rounds = (int)_rounds.Value;

            // Keep only a plan that actually parses, so a typo cannot silently
            // replace a working session with nothing.
            string plan = _plan.Text.Trim();
            Result.Plan = (plan.Length > 0 && !IntervalPlan.Parse(plan).IsEmpty) ? plan : string.Empty;
            Result.Volume = (double)_volume.Value / 100.0;

            Result.WorkLabel = string.IsNullOrEmpty(_workLabel.Text.Trim()) ? "WORK" : _workLabel.Text.Trim();
            Result.RestLabel = string.IsNullOrEmpty(_restLabel.Text.Trim()) ? "REST" : _restLabel.Text.Trim();
            Result.Stations = TimerSettings.SplitList(_stations.Text);

            string format = _clockFormat.Text.Trim();
            if (format.Length > 0)
            {
                try
                {
                    DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
                    Result.ClockFormat = format;
                }
                catch
                {
                    Result.ClockFormat = "h:mm:ss tt";
                }
            }

            Result.RestAfterFinalRound = _restAfterFinal.Checked;
            Result.SoundEnabled = _sound.Checked;
            Result.ShowClock = _showClock.Checked;
            Result.AlwaysOnTop = _onTop.Checked;
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

    internal static class TextBoxExtensions
    {
        /// <summary>
        /// PlaceholderText only exists on newer frameworks, so set it defensively.
        /// </summary>
        public static void PlaceholderTextSafe(this TextBox box, string text)
        {
            try
            {
                System.Reflection.PropertyInfo property = typeof(TextBox).GetProperty("PlaceholderText");
                if (property != null && property.CanWrite) property.SetValue(box, text, null);
            }
            catch { }
        }
    }
}
