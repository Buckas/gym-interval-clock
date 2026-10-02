using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// The fast path to a basic program: a name, an execution mode, a pattern
    /// and its numbers, and a live preview. Opened from ProgramEditorForm's
    /// "Quick Start Wizard..." button (or the clock's "B" shortcut, which opens
    /// that same editor and launches this straight away) - "Create Program"
    /// hands the result back to it, ready to refine further or just run as is.
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

        /// <summary>
        /// Parameterless constructor required by the Windows Forms designer at design time.
        /// Not intended for runtime use.
        /// </summary>
        public QuickSetupForm()
            : this(new TimerSettings())
        {
        }

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
            _pattern.SelectedIndexChanged += delegate { UpdateFieldVisibility(); UpdatePreview(); };
            _work.ValueChanged += refresh;
            _recovery.ValueChanged += refresh;
            _rounds.ValueChanged += refresh;
            _step.ValueChanged += refresh;
            _minutes.ValueChanged += refresh;
            EventHandler modeChanged = delegate { UpdateModeDescription(); UpdatePreview(); };
            _sharedMode.CheckedChanged += modeChanged;
            _sequentialMode.CheckedChanged += modeChanged;
            _parallelMode.CheckedChanged += modeChanged;
            _circuitMode.CheckedChanged += modeChanged;

            UpdateFieldVisibility();
            UpdateModeDescription();
            UpdatePreview();
        }

        /// <summary>
        /// Plain-language explanation with a concrete example for whichever
        /// execution mode is selected - the three names alone ("Shared",
        /// "Sequential", "Parallel") weren't enough to tell people apart.
        /// </summary>
        private void UpdateModeDescription()
        {
            if (_parallelMode.Checked)
            {
                _modeDescription.Text =
                    "Every station runs its OWN pattern at the same time, independently.\n"
                    + "Example: Station A does a 4-minute Tabata while Station B does a 10-minute Pyramid - "
                    + "both start together but finish at different times.";
            }
            else if (_sequentialMode.Checked)
            {
                _modeDescription.Text =
                    "Stations run one after another - only one is active at a time, each with its own pattern.\n"
                    + "Example: the group finishes all of Squats, moves to the Bike, then the Rower - "
                    + "one station's whole pattern before moving to the next.";
            }
            else if (_circuitMode.Checked)
            {
                _modeDescription.Text =
                    "A different group works at EVERY station at once, all on the exact same countdown - "
                    + "then everybody moves to the next station together.\n"
                    + "Example: 8 groups, one per station, all do 45s work / 15s rest together, then "
                    + "everyone rotates clockwise to the next station and repeats.";
            }
            else
            {
                _modeDescription.Text =
                    "Every station follows the exact SAME countdown together, at the same time.\n"
                    + "Example: the whole class does 30s work / 15s rest together - each station just says "
                    + "which exercise to do, but everyone starts and stops together.";
            }
        }

        /// <summary>
        /// Shows only the numbers a pattern actually uses - e.g. Tabata's work
        /// and recovery are fixed at 20/10, so showing those fields would just
        /// be confusing dead weight; EMOM cares about total minutes rather than
        /// a round count or a step size.
        /// </summary>
        private void UpdateFieldVisibility()
        {
            bool work = true;
            bool recovery = true;
            bool rounds = true;
            bool step = false;
            bool minutes = false;

            switch (SelectedPattern)
            {
                case PatternType.Tabata:
                    // Fixed at 20 on / 10 off - nothing to type.
                    work = false;
                    recovery = false;
                    break;

                case PatternType.Pyramid:
                case PatternType.ReversePyramid:
                case PatternType.LadderUp:
                case PatternType.LadderDown:
                case PatternType.Wave:
                case PatternType.AscendingWork:
                case PatternType.DescendingWork:
                    step = true;
                    break;

                case PatternType.Emom:
                case PatternType.E2mom:
                    recovery = false;
                    rounds = false;
                    minutes = true;
                    break;

                case PatternType.Custom:
                    recovery = false;
                    rounds = false;
                    break;
            }

            SetVisible(_workLabel, _work, _workUnit, work);
            SetVisible(_recoveryLabel, _recovery, _recoveryUnit, recovery);
            _roundsLabel.Visible = rounds;
            _rounds.Visible = rounds;
            SetVisible(_stepLabel, _step, _stepUnit, step);
            SetVisible(_minutesLabel, _minutes, _minutesUnit, minutes);
        }

        private static void SetVisible(Control label, Control field, Control unit, bool visible)
        {
            label.Visible = visible;
            field.Visible = visible;
            unit.Visible = visible;
        }

        private PatternType SelectedPattern
        {
            get
            {
                int index = _pattern.SelectedIndex;
                return index >= 0 && index < Patterns.Length ? Patterns[index].Key : PatternType.Standard;
            }
        }

        private ExecutionMode SelectedMode
        {
            get
            {
                if (_parallelMode.Checked) return ExecutionMode.Parallel;
                if (_sequentialMode.Checked) return ExecutionMode.Sequential;
                if (_circuitMode.Checked) return ExecutionMode.Circuit;
                return ExecutionMode.Shared;
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
            string mode;
            switch (SelectedMode)
            {
                case ExecutionMode.Sequential: mode = "Sequential Stations"; break;
                case ExecutionMode.Parallel: mode = "Parallel Independent Stations"; break;
                case ExecutionMode.Circuit: mode = "Shared Circuit (groups rotate)"; break;
                default: mode = "Shared Timing"; break;
            }

            _preview.Text = mode + Environment.NewLine + timeline.Summary()
                + "   (" + IntervalPlan.FormatDuration(timeline.TotalSeconds()) + ")";
        }

        private WorkoutProgram BuildProgram()
        {
            WorkoutProgram program = new WorkoutProgram
            {
                Name = string.IsNullOrEmpty(_name.Text.Trim()) ? "Workout" : _name.Text.Trim(),
                Mode = SelectedMode,
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
    }
}
