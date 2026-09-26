using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// The small dialog behind the Program Builder's "+Pattern..." button - pick
    /// a generator and its numbers, same idea as Quick Setup but scoped to
    /// inserting into whatever timeline is currently selected. Built entirely in
    /// code rather than a separate Designer file since it is this small.
    /// </summary>
    public class PatternPickerForm : Form
    {
        private static readonly KeyValuePair<PatternType, string>[] Patterns = new[]
        {
            new KeyValuePair<PatternType, string>(PatternType.Standard, "Standard Interval"),
            new KeyValuePair<PatternType, string>(PatternType.Tabata, "Tabata (20/10)"),
            new KeyValuePair<PatternType, string>(PatternType.Pyramid, "Pyramid"),
            new KeyValuePair<PatternType, string>(PatternType.ReversePyramid, "Reverse Pyramid"),
            new KeyValuePair<PatternType, string>(PatternType.LadderUp, "Ladder Up"),
            new KeyValuePair<PatternType, string>(PatternType.LadderDown, "Ladder Down"),
            new KeyValuePair<PatternType, string>(PatternType.Wave, "Wave"),
            new KeyValuePair<PatternType, string>(PatternType.AscendingWork, "Ascending Work"),
            new KeyValuePair<PatternType, string>(PatternType.DescendingWork, "Descending Work"),
            new KeyValuePair<PatternType, string>(PatternType.Emom, "EMOM"),
            new KeyValuePair<PatternType, string>(PatternType.E2mom, "E2MOM"),
            new KeyValuePair<PatternType, string>(PatternType.Custom, "Custom Sequence")
        };

        private readonly ComboBox _pattern = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _work = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 30 };
        private readonly NumericUpDown _recovery = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = 15 };
        private readonly NumericUpDown _rounds = new NumericUpDown { Minimum = 1, Maximum = 999, Value = 8 };
        private readonly NumericUpDown _step = new NumericUpDown { Minimum = 1, Maximum = 300, Value = 10 };
        private readonly NumericUpDown _minutes = new NumericUpDown { Minimum = 1, Maximum = 180, Value = 10 };
        private readonly Label _preview = new Label { ForeColor = Color.DimGray };

        public PatternType SelectedPattern
        {
            get
            {
                int index = _pattern.SelectedIndex;
                return index >= 0 && index < Patterns.Length ? Patterns[index].Key : PatternType.Standard;
            }
        }

        public PatternSettings Settings
        {
            get
            {
                return new PatternSettings
                {
                    WorkSeconds = (int)_work.Value,
                    RecoverySeconds = (int)_recovery.Value,
                    Rounds = (int)_rounds.Value,
                    StepSeconds = (int)_step.Value,
                    TotalMinutes = (int)_minutes.Value
                };
            }
        }

        public PatternPickerForm()
        {
            Text = "Add Pattern";
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(360, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            // ProgramBuilderForm (its owner here) is TopMost, so this needs to be
            // too - otherwise it would render behind its own owner, same issue as
            // QuickSetupForm/ProgramBuilderForm needing it against SettingsForm.
            TopMost = true;

            foreach (KeyValuePair<PatternType, string> entry in Patterns) _pattern.Items.Add(entry.Value);
            _pattern.SelectedIndex = 0;
            _pattern.Location = new Point(12, 12);
            _pattern.Size = new Size(220, 30);

            Label workLabel = new Label { Text = "Work (sec)", Location = new Point(12, 52), Size = new Size(100, 26) };
            _work.Location = new Point(140, 48);
            _work.Size = new Size(80, 30);

            Label recoveryLabel = new Label { Text = "Recovery (sec)", Location = new Point(12, 88), Size = new Size(100, 26) };
            _recovery.Location = new Point(140, 84);
            _recovery.Size = new Size(80, 30);

            Label roundsLabel = new Label { Text = "Rounds", Location = new Point(12, 124), Size = new Size(100, 26) };
            _rounds.Location = new Point(140, 120);
            _rounds.Size = new Size(80, 30);

            Label stepLabel = new Label { Text = "Step (sec)", Location = new Point(12, 160), Size = new Size(100, 26) };
            _step.Location = new Point(140, 156);
            _step.Size = new Size(80, 30);

            Label minutesLabel = new Label { Text = "Total minutes", Location = new Point(12, 196), Size = new Size(100, 26) };
            _minutes.Location = new Point(140, 192);
            _minutes.Size = new Size(80, 30);

            _preview.Location = new Point(12, 232);
            _preview.Size = new Size(336, 50);

            Button ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Location = new Point(172, 290), Size = new Size(80, 34) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(260, 290), Size = new Size(88, 34) };

            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(_pattern);
            Controls.Add(workLabel); Controls.Add(_work);
            Controls.Add(recoveryLabel); Controls.Add(_recovery);
            Controls.Add(roundsLabel); Controls.Add(_rounds);
            Controls.Add(stepLabel); Controls.Add(_step);
            Controls.Add(minutesLabel); Controls.Add(_minutes);
            Controls.Add(_preview);
            Controls.Add(ok);
            Controls.Add(cancel);

            EventHandler refresh = delegate { UpdatePreview(); };
            _pattern.SelectedIndexChanged += refresh;
            _work.ValueChanged += refresh;
            _recovery.ValueChanged += refresh;
            _rounds.ValueChanged += refresh;
            _step.ValueChanged += refresh;
            _minutes.ValueChanged += refresh;

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            Timeline timeline = PatternGenerator.Generate(SelectedPattern, Settings);
            _preview.Text = timeline.Summary() + "   (" + IntervalPlan.FormatDuration(timeline.TotalSeconds()) + ")";
        }
    }
}
