using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// The first thing you see when building a program: a name, an execution
    /// mode, a pattern and its numbers, and a live preview. "Create Program"
    /// applies it straight away; "Open In Builder" hands the generated program
    /// to the Program Builder for further shaping (adding stations, rest blocks
    /// anywhere, repeat groups, and so on) before it runs.
    /// </summary>
    public partial class QuickSetupForm : Form
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

        public WorkoutProgram Result { get; private set; }

        public QuickSetupForm(TimerSettings settings)
        {
            InitializeComponent();

            foreach (KeyValuePair<PatternType, string> entry in Patterns) _pattern.Items.Add(entry.Value);
            _pattern.SelectedIndex = 0;

            _name.Text = "Workout";
            _work.Value = 30;
            _recovery.Value = 15;
            _rounds.Value = 8;
            _step.Value = 10;
            _minutes.Value = 10;

            EventHandler refresh = delegate { UpdatePreview(); };
            _pattern.SelectedIndexChanged += refresh;
            _work.ValueChanged += refresh;
            _recovery.ValueChanged += refresh;
            _rounds.ValueChanged += refresh;
            _step.ValueChanged += refresh;
            _minutes.ValueChanged += refresh;
            _sharedMode.CheckedChanged += refresh;
            _sequentialMode.CheckedChanged += refresh;

            UpdatePreview();
        }

        private PatternType SelectedPattern
        {
            get
            {
                int index = _pattern.SelectedIndex;
                return index >= 0 && index < Patterns.Length ? Patterns[index].Key : PatternType.Standard;
            }
        }

        private PatternSettings ReadSettings()
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

        private void UpdatePreview()
        {
            Timeline timeline = PatternGenerator.Generate(SelectedPattern, ReadSettings());
            string mode = _sequentialMode.Checked ? "Sequential Stations" : "Shared Timing";

            _preview.Text = mode + Environment.NewLine + timeline.Summary()
                + "   (" + IntervalPlan.FormatDuration(timeline.TotalSeconds()) + ")";
        }

        private WorkoutProgram BuildProgram()
        {
            WorkoutProgram program = new WorkoutProgram
            {
                Name = string.IsNullOrEmpty(_name.Text.Trim()) ? "Workout" : _name.Text.Trim(),
                Mode = _sequentialMode.Checked ? ExecutionMode.Sequential : ExecutionMode.Shared,
                SharedTimeline = PatternGenerator.Generate(SelectedPattern, ReadSettings())
            };
            return program;
        }

        private void CreateProgram_Click(object sender, EventArgs e)
        {
            Result = BuildProgram();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OpenInBuilder_Click(object sender, EventArgs e)
        {
            using (ProgramBuilderForm dialog = new ProgramBuilderForm(BuildProgram()))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
                {
                    Result = dialog.Result;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
        }
    }
}
