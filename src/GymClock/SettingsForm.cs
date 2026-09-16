using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
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

        /// <summary>
        /// The program built so far this dialog session (Quick Setup / the
        /// Program Builder / an import all write here). Kept separately from
        /// Result, the same way every other field lives in a control until
        /// ApplyControlsTo copies it across - Ok_Click rebuilds Result fresh
        /// from _original each time, which would otherwise silently discard a
        /// program built earlier in the same session.
        /// </summary>
        private string _workingProgramScript;

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

            _clockFormat.Text = settings.ClockFormat;

            _restAfterFinal.Checked = settings.RestAfterFinalRound;
            _sound.Checked = settings.SoundEnabled;
            _showClock.Checked = settings.ShowClock;
            _onTop.Checked = settings.AlwaysOnTop;

            _showStationsTable.Checked = settings.ShowStationsTable;
            _showStationNameAsDescription.Checked = settings.ShowStationNameAsDescription;
            _highlightCurrentStation.Checked = settings.HighlightCurrentStation;

            _moveSeconds.Minimum = 1;
            _moveSeconds.Maximum = 600;
            _moveSeconds.Value = Clamp(settings.MoveSeconds, _moveSeconds);
            _useMoveTime.Checked = settings.UseMoveTime;
            _moveMessage.Text = settings.MoveMessage;

            _workingProgramScript = settings.ProgramScript;
            RefreshProgramSummary();
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

        // ------------------------------------------------------------ program

        /// <summary>The program built so far this session, parsed from _workingProgramScript, or the legacy synthesis if there isn't one yet.</summary>
        private WorkoutProgram CurrentWorkingProgram()
        {
            if (!string.IsNullOrEmpty(_workingProgramScript))
            {
                WorkoutProgram parsed = ProgramScriptFormat.Parse(_workingProgramScript);
                if (!parsed.SharedTimeline.IsEmpty || parsed.Stations.Count > 0) return parsed;
            }
            return Result.EffectiveProgram();
        }

        /// <summary>Shows the current program's name, mode and shape, whether it was built or synthesised from the simple fields.</summary>
        private void RefreshProgramSummary()
        {
            WorkoutProgram program = CurrentWorkingProgram();
            bool builtProgram = !string.IsNullOrEmpty(_workingProgramScript);

            string modeText = program.Mode == ExecutionMode.Sequential
                ? "Sequential stations (" + program.Stations.Count + ")"
                : "Shared timing";

            string summary = builtProgram
                ? program.Name + Environment.NewLine + modeText
                : "No program built yet - running the simple work/rest/rounds settings on the other tab."
                    + Environment.NewLine + "Use Quick Setup or the Program Builder to build a real program"
                    + " (Tabata, Pyramid, stations with their own timing, and so on).";

            _programSummaryLabel.Text = summary;
        }

        private void QuickSetup_Click(object sender, EventArgs e)
        {
            using (QuickSetupForm dialog = new QuickSetupForm(Result))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
                {
                    _workingProgramScript = ProgramScriptFormat.ToScript(dialog.Result);
                    RefreshProgramSummary();
                    _saveStatusLabel.Text = string.Empty;
                }
            }
        }

        private void OpenBuilder_Click(object sender, EventArgs e)
        {
            using (ProgramBuilderForm dialog = new ProgramBuilderForm(CurrentWorkingProgram()))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
                {
                    _workingProgramScript = ProgramScriptFormat.ToScript(dialog.Result);
                    RefreshProgramSummary();
                    _saveStatusLabel.Text = string.Empty;
                }
            }
        }

        /// <summary>
        /// Exports the current program as script text - the same grammar the
        /// Program Builder's Script tab reads and writes - so a teacher can build
        /// a program once and reuse it on another computer or in another session.
        /// </summary>
        private void ExportProgram_Click(object sender, EventArgs e)
        {
            WorkoutProgram program = CurrentWorkingProgram();

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Program file (*.program.txt)|*.program.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = SafeFileName(program.Name) + ".program.txt";
                dialog.Title = "Export program";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.WriteAllText(dialog.FileName, ProgramScriptFormat.ToScript(program));
                    _saveStatusLabel.Text = "Exported program to " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save that file:" + Environment.NewLine + ex.Message,
                        "Export program", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportProgram_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Program file (*.program.txt;*.txt)|*.program.txt;*.txt|All files (*.*)|*.*";
                dialog.Title = "Import program";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string text;
                try
                {
                    text = File.ReadAllText(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not read that file:" + Environment.NewLine + ex.Message,
                        "Import program", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                WorkoutProgram program = ProgramScriptFormat.Parse(text);
                if (program.SharedTimeline.IsEmpty && program.Stations.Count == 0)
                {
                    MessageBox.Show(this, "No program was found in that file.", "Import program",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _workingProgramScript = ProgramScriptFormat.ToScript(program);
                RefreshProgramSummary();
                _saveStatusLabel.Text = "Imported program from " + dialog.FileName;
            }
        }

        private static string SafeFileName(string name)
        {
            string text = string.IsNullOrEmpty(name) ? "workout" : name;
            foreach (char c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '-');
            return text;
        }

        // --------------------------------------------------------------- save

        /// <summary>
        /// Writes straight to settings.txt without closing the dialog, so a teacher
        /// can build up a session and check it is on disk before moving on.
        /// </summary>
        private void Save_Click(object sender, EventArgs e)
        {
            ApplyControlsTo(Result);
            Result.Save();
            _saveStatusLabel.Text = "Saved to " + TimerSettings.FilePath;
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
            ApplyControlsTo(Result);
        }

        /// <summary>Copies every control's current value onto the given settings object.</summary>
        private void ApplyControlsTo(TimerSettings target)
        {
            target.PrepSeconds = (int)_prep.Value;
            target.WorkSeconds = Math.Max(1, (int)_work.Value);
            target.RestSeconds = (int)_rest.Value;
            target.Rounds = (int)_rounds.Value;

            // Keep only a plan that actually parses, so a typo cannot silently
            // replace a working session with nothing.
            string plan = _plan.Text.Trim();
            target.Plan = (plan.Length > 0 && !IntervalPlan.Parse(plan).IsEmpty) ? plan : string.Empty;
            target.Volume = (double)_volume.Value / 100.0;

            target.WorkLabel = string.IsNullOrEmpty(_workLabel.Text.Trim()) ? "WORK" : _workLabel.Text.Trim();
            target.RestLabel = string.IsNullOrEmpty(_restLabel.Text.Trim()) ? "REST" : _restLabel.Text.Trim();
            target.ShowStationsTable = _showStationsTable.Checked;
            target.ShowStationNameAsDescription = _showStationNameAsDescription.Checked;
            target.HighlightCurrentStation = _highlightCurrentStation.Checked;
            target.UseMoveTime = _useMoveTime.Checked;
            target.MoveSeconds = Math.Max(1, (int)_moveSeconds.Value);
            target.MoveMessage = string.IsNullOrEmpty(_moveMessage.Text.Trim())
                ? "Move to the next station" : _moveMessage.Text.Trim();

            // The program built through Quick Setup / the Program Builder / an
            // import lives in _workingProgramScript until now, the same way
            // every other field lives in a control until this point.
            target.ProgramScript = _workingProgramScript ?? string.Empty;

            string format = _clockFormat.Text.Trim();
            if (format.Length > 0)
            {
                try
                {
                    DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
                    target.ClockFormat = format;
                }
                catch
                {
                    target.ClockFormat = "h:mm:ss tt";
                }
            }

            target.RestAfterFinalRound = _restAfterFinal.Checked;
            target.SoundEnabled = _sound.Checked;
            target.ShowClock = _showClock.Checked;
            target.AlwaysOnTop = _onTop.Checked;
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
