using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    public partial class QuickSetupForm
    {
        private System.ComponentModel.IContainer components = null;

        private Label _nameLabel;
        private TextBox _name;

        private Label _modeLabel;
        private RadioButton _sharedMode;
        private RadioButton _sequentialMode;
        private RadioButton _parallelMode;   // present but disabled - Parallel Independent Stations is a later phase

        private Label _patternLabel;
        private ComboBox _pattern;

        private Label _workLabel;
        private NumericUpDown _work;
        private Label _workUnit;

        private Label _recoveryLabel;
        private NumericUpDown _recovery;
        private Label _recoveryUnit;

        private Label _roundsLabel;
        private NumericUpDown _rounds;

        private Label _stepLabel;
        private NumericUpDown _step;
        private Label _stepUnit;

        private Label _minutesLabel;
        private NumericUpDown _minutes;
        private Label _minutesUnit;

        private Label _previewLabel;
        private Label _preview;

        private Button _createButton;
        private Button _openBuilderButton;
        private Button _cancelButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _nameLabel = new Label();
            _name = new TextBox();

            _modeLabel = new Label();
            _sharedMode = new RadioButton();
            _sequentialMode = new RadioButton();
            _parallelMode = new RadioButton();

            _patternLabel = new Label();
            _pattern = new ComboBox();

            _workLabel = new Label();
            _work = new NumericUpDown();
            _workUnit = new Label();

            _recoveryLabel = new Label();
            _recovery = new NumericUpDown();
            _recoveryUnit = new Label();

            _roundsLabel = new Label();
            _rounds = new NumericUpDown();

            _stepLabel = new Label();
            _step = new NumericUpDown();
            _stepUnit = new Label();

            _minutesLabel = new Label();
            _minutes = new NumericUpDown();
            _minutesUnit = new Label();

            _previewLabel = new Label();
            _preview = new Label();

            _createButton = new Button();
            _openBuilderButton = new Button();
            _cancelButton = new Button();

            ((System.ComponentModel.ISupportInitialize)_work).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_recovery).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_rounds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_step).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_minutes).BeginInit();
            SuspendLayout();
            //
            // _nameLabel
            //
            _nameLabel.Location = new Point(16, 16);
            _nameLabel.Size = new Size(120, 30);
            _nameLabel.Text = "Program name";
            _nameLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _name
            //
            _name.Location = new Point(144, 12);
            _name.Size = new Size(300, 34);
            //
            // _modeLabel
            //
            _modeLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _modeLabel.Location = new Point(16, 58);
            _modeLabel.Size = new Size(200, 24);
            _modeLabel.Text = "Execution mode";
            //
            // _sharedMode
            //
            _sharedMode.Checked = true;
            _sharedMode.Location = new Point(16, 84);
            _sharedMode.Size = new Size(420, 26);
            _sharedMode.Text = "Shared Timing - every station follows the same countdown";
            //
            // _sequentialMode
            //
            _sequentialMode.Location = new Point(16, 110);
            _sequentialMode.Size = new Size(420, 26);
            _sequentialMode.Text = "Sequential Stations - one station at a time, each with its own timing";
            //
            // _parallelMode
            //
            _parallelMode.Enabled = false;
            _parallelMode.Location = new Point(16, 136);
            _parallelMode.Size = new Size(420, 26);
            _parallelMode.Text = "Parallel Independent Stations - coming soon";
            //
            // _patternLabel
            //
            _patternLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _patternLabel.Location = new Point(16, 176);
            _patternLabel.Size = new Size(200, 24);
            _patternLabel.Text = "Pattern";
            //
            // _pattern
            //
            _pattern.DropDownStyle = ComboBoxStyle.DropDownList;
            _pattern.Location = new Point(16, 202);
            _pattern.Size = new Size(260, 36);
            //
            // _workLabel
            //
            _workLabel.Location = new Point(16, 250);
            _workLabel.Size = new Size(140, 30);
            _workLabel.Text = "Work seconds";
            _workLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _work
            //
            _work.Location = new Point(160, 246);
            _work.Size = new Size(80, 34);
            _work.Minimum = 1;
            _work.Maximum = 3600;
            _work.TextAlign = HorizontalAlignment.Right;
            //
            // _workUnit
            //
            _workUnit.Location = new Point(246, 250);
            _workUnit.Size = new Size(40, 30);
            _workUnit.Text = "sec";
            //
            // _recoveryLabel
            //
            _recoveryLabel.Location = new Point(16, 288);
            _recoveryLabel.Size = new Size(140, 30);
            _recoveryLabel.Text = "Recovery seconds";
            _recoveryLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _recovery
            //
            _recovery.Location = new Point(160, 284);
            _recovery.Size = new Size(80, 34);
            _recovery.Minimum = 0;
            _recovery.Maximum = 3600;
            _recovery.TextAlign = HorizontalAlignment.Right;
            //
            // _recoveryUnit
            //
            _recoveryUnit.Location = new Point(246, 288);
            _recoveryUnit.Size = new Size(40, 30);
            _recoveryUnit.Text = "sec";
            //
            // _roundsLabel
            //
            _roundsLabel.Location = new Point(16, 326);
            _roundsLabel.Size = new Size(140, 30);
            _roundsLabel.Text = "Rounds";
            _roundsLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _rounds
            //
            _rounds.Location = new Point(160, 322);
            _rounds.Size = new Size(80, 34);
            _rounds.Minimum = 1;
            _rounds.Maximum = 999;
            _rounds.TextAlign = HorizontalAlignment.Right;
            //
            // _stepLabel
            //
            _stepLabel.Location = new Point(300, 250);
            _stepLabel.Size = new Size(140, 30);
            _stepLabel.Text = "Step seconds";
            _stepLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _step
            //
            _step.Location = new Point(440, 246);
            _step.Size = new Size(80, 34);
            _step.Minimum = 1;
            _step.Maximum = 300;
            _step.TextAlign = HorizontalAlignment.Right;
            //
            // _stepUnit
            //
            _stepUnit.Location = new Point(526, 250);
            _stepUnit.Size = new Size(40, 30);
            _stepUnit.Text = "sec";
            //
            // _minutesLabel
            //
            _minutesLabel.Location = new Point(300, 288);
            _minutesLabel.Size = new Size(140, 30);
            _minutesLabel.Text = "Total minutes";
            _minutesLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _minutes
            //
            _minutes.Location = new Point(440, 284);
            _minutes.Size = new Size(80, 34);
            _minutes.Minimum = 1;
            _minutes.Maximum = 180;
            _minutes.TextAlign = HorizontalAlignment.Right;
            //
            // _minutesUnit
            //
            _minutesUnit.Location = new Point(526, 288);
            _minutesUnit.Size = new Size(40, 30);
            _minutesUnit.Text = "min";
            //
            // _previewLabel
            //
            _previewLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _previewLabel.Location = new Point(16, 372);
            _previewLabel.Size = new Size(200, 24);
            _previewLabel.Text = "Preview";
            //
            // _preview
            //
            _preview.ForeColor = Color.DimGray;
            _preview.Location = new Point(16, 398);
            _preview.Size = new Size(568, 70);
            _preview.Text = "";
            //
            // _createButton
            //
            _createButton.Location = new Point(300, 480);
            _createButton.Size = new Size(140, 38);
            _createButton.Text = "Create Program";
            _createButton.Click += CreateProgram_Click;
            //
            // _openBuilderButton
            //
            _openBuilderButton.Location = new Point(448, 480);
            _openBuilderButton.Size = new Size(136, 38);
            _openBuilderButton.Text = "Open In Builder";
            _openBuilderButton.Click += OpenInBuilder_Click;
            //
            // _cancelButton
            //
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Location = new Point(16, 480);
            _cancelButton.Size = new Size(90, 38);
            _cancelButton.Text = "Cancel";
            //
            // QuickSetupForm
            //
            AcceptButton = _createButton;
            CancelButton = _cancelButton;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(600, 534);
            Controls.Add(_nameLabel);
            Controls.Add(_name);
            Controls.Add(_modeLabel);
            Controls.Add(_sharedMode);
            Controls.Add(_sequentialMode);
            Controls.Add(_parallelMode);
            Controls.Add(_patternLabel);
            Controls.Add(_pattern);
            Controls.Add(_workLabel);
            Controls.Add(_work);
            Controls.Add(_workUnit);
            Controls.Add(_recoveryLabel);
            Controls.Add(_recovery);
            Controls.Add(_recoveryUnit);
            Controls.Add(_roundsLabel);
            Controls.Add(_rounds);
            Controls.Add(_stepLabel);
            Controls.Add(_step);
            Controls.Add(_stepUnit);
            Controls.Add(_minutesLabel);
            Controls.Add(_minutes);
            Controls.Add(_minutesUnit);
            Controls.Add(_previewLabel);
            Controls.Add(_preview);
            Controls.Add(_createButton);
            Controls.Add(_openBuilderButton);
            Controls.Add(_cancelButton);
            Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "QuickSetupForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Quick Setup";
            ((System.ComponentModel.ISupportInitialize)_work).EndInit();
            ((System.ComponentModel.ISupportInitialize)_recovery).EndInit();
            ((System.ComponentModel.ISupportInitialize)_rounds).EndInit();
            ((System.ComponentModel.ISupportInitialize)_step).EndInit();
            ((System.ComponentModel.ISupportInitialize)_minutes).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
