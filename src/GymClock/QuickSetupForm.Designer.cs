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
        private RadioButton _parallelMode;
        private RadioButton _circuitMode;
        private Label _modeDescription;

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
            _circuitMode = new RadioButton();
            _modeDescription = new Label();

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
            _sharedMode.Text = "Shared Timing";
            //
            // _sequentialMode
            //
            _sequentialMode.Location = new Point(16, 110);
            _sequentialMode.Size = new Size(420, 26);
            _sequentialMode.Text = "Sequential Stations";
            //
            // _parallelMode
            //
            _parallelMode.Location = new Point(16, 136);
            _parallelMode.Size = new Size(420, 26);
            _parallelMode.Text = "Parallel Independent Stations";
            //
            // _circuitMode
            //
            _circuitMode.Location = new Point(16, 162);
            _circuitMode.Size = new Size(420, 26);
            _circuitMode.Text = "Shared Circuit (groups rotate)";
            //
            // _modeDescription
            //
            // Updated live by UpdateModeDescription() to explain, in plain
            // language with a concrete example, whichever mode is selected -
            // the three one-word names alone weren't enough to tell them apart.
            _modeDescription.BackColor = Color.FromArgb(240, 240, 245);
            _modeDescription.Font = new Font("Segoe UI", 9F);
            _modeDescription.Location = new Point(16, 192);
            _modeDescription.Size = new Size(568, 62);
            _modeDescription.Padding = new Padding(8, 6, 8, 6);
            _modeDescription.Text = "";
            //
            // _patternLabel
            //
            _patternLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _patternLabel.Location = new Point(16, 266);
            _patternLabel.Size = new Size(200, 24);
            _patternLabel.Text = "Pattern";
            //
            // _pattern
            //
            _pattern.DropDownStyle = ComboBoxStyle.DropDownList;
            _pattern.Location = new Point(16, 292);
            _pattern.Size = new Size(260, 36);
            //
            // _workLabel
            //
            _workLabel.Location = new Point(16, 340);
            _workLabel.Size = new Size(140, 30);
            _workLabel.Text = "Work seconds";
            _workLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _work
            //
            _work.Location = new Point(160, 336);
            _work.Size = new Size(80, 34);
            _work.Minimum = 1;
            _work.Maximum = 3600;
            _work.TextAlign = HorizontalAlignment.Right;
            //
            // _workUnit
            //
            _workUnit.Location = new Point(246, 340);
            _workUnit.Size = new Size(40, 30);
            _workUnit.Text = "sec";
            //
            // _recoveryLabel
            //
            _recoveryLabel.Location = new Point(16, 378);
            _recoveryLabel.Size = new Size(140, 30);
            _recoveryLabel.Text = "Recovery seconds";
            _recoveryLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _recovery
            //
            _recovery.Location = new Point(160, 374);
            _recovery.Size = new Size(80, 34);
            _recovery.Minimum = 0;
            _recovery.Maximum = 3600;
            _recovery.TextAlign = HorizontalAlignment.Right;
            //
            // _recoveryUnit
            //
            _recoveryUnit.Location = new Point(246, 378);
            _recoveryUnit.Size = new Size(40, 30);
            _recoveryUnit.Text = "sec";
            //
            // _roundsLabel
            //
            _roundsLabel.Location = new Point(16, 416);
            _roundsLabel.Size = new Size(140, 30);
            _roundsLabel.Text = "Rounds";
            _roundsLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _rounds
            //
            _rounds.Location = new Point(160, 412);
            _rounds.Size = new Size(80, 34);
            _rounds.Minimum = 1;
            _rounds.Maximum = 999;
            _rounds.TextAlign = HorizontalAlignment.Right;
            //
            // _stepLabel
            //
            _stepLabel.Location = new Point(300, 340);
            _stepLabel.Size = new Size(140, 30);
            _stepLabel.Text = "Step seconds";
            _stepLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _step
            //
            _step.Location = new Point(440, 336);
            _step.Size = new Size(80, 34);
            _step.Minimum = 1;
            _step.Maximum = 300;
            _step.TextAlign = HorizontalAlignment.Right;
            //
            // _stepUnit
            //
            _stepUnit.Location = new Point(526, 340);
            _stepUnit.Size = new Size(40, 30);
            _stepUnit.Text = "sec";
            //
            // _minutesLabel
            //
            _minutesLabel.Location = new Point(300, 404);
            _minutesLabel.Size = new Size(140, 30);
            _minutesLabel.Text = "Total minutes";
            _minutesLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _minutes
            //
            _minutes.Location = new Point(440, 400);
            _minutes.Size = new Size(80, 34);
            _minutes.Minimum = 1;
            _minutes.Maximum = 180;
            _minutes.TextAlign = HorizontalAlignment.Right;
            //
            // _minutesUnit
            //
            _minutesUnit.Location = new Point(526, 404);
            _minutesUnit.Size = new Size(40, 30);
            _minutesUnit.Text = "min";
            //
            // _previewLabel
            //
            _previewLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _previewLabel.Location = new Point(16, 462);
            _previewLabel.Size = new Size(200, 24);
            _previewLabel.Text = "Preview";
            //
            // _preview
            //
            _preview.ForeColor = Color.DimGray;
            _preview.Location = new Point(16, 488);
            _preview.Size = new Size(568, 70);
            _preview.Text = "";
            //
            // _createButton
            //
            _createButton.Location = new Point(414, 570);
            _createButton.Size = new Size(170, 38);
            _createButton.Text = "Create Program";
            _createButton.Click += CreateProgram_Click;
            //
            // _cancelButton
            //
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Location = new Point(16, 570);
            _cancelButton.Size = new Size(90, 38);
            _cancelButton.Text = "Cancel";
            //
            // QuickSetupForm
            //
            AcceptButton = _createButton;
            CancelButton = _cancelButton;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(600, 624);
            Controls.Add(_nameLabel);
            Controls.Add(_name);
            Controls.Add(_modeLabel);
            Controls.Add(_sharedMode);
            Controls.Add(_sequentialMode);
            Controls.Add(_parallelMode);
            Controls.Add(_circuitMode);
            Controls.Add(_modeDescription);
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
            // SettingsForm (and MainForm, when "keep window on top" is on) are
            // TopMost - without this, this dialog would render behind them
            // despite being modal, since TopMost wins the OS z-order regardless
            // of ownership. Same convention as SettingsForm/LicenceDialog/LicenceRequestForm.
            TopMost = true;
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
