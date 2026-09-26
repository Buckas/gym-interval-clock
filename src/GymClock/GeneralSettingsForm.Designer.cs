using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    public partial class GeneralSettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        private Label _sectionPrep;
        private Label _prepLabel;
        private NumericUpDown _prep;
        private Label _prepUnit;
        private Label _volumeLabel;
        private NumericUpDown _volume;
        private Label _volumeUnit;
        private CheckBox _sound;

        private Label _sectionClock;
        private Label _clockFormatLabel;
        private ComboBox _clockFormat;
        private CheckBox _showClock;
        private CheckBox _onTop;

        private Label _sectionStation;
        private CheckBox _showStationsTable;
        private CheckBox _showStationNameAsDescription;

        private Label _pathLabel;
        private Button _okButton;
        private Button _cancelButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _sectionPrep = new Label();
            _prepLabel = new Label();
            _prep = new NumericUpDown();
            _prepUnit = new Label();
            _volumeLabel = new Label();
            _volume = new NumericUpDown();
            _volumeUnit = new Label();
            _sound = new CheckBox();

            _sectionClock = new Label();
            _clockFormatLabel = new Label();
            _clockFormat = new ComboBox();
            _showClock = new CheckBox();
            _onTop = new CheckBox();

            _sectionStation = new Label();
            _showStationsTable = new CheckBox();
            _showStationNameAsDescription = new CheckBox();

            _pathLabel = new Label();
            _okButton = new Button();
            _cancelButton = new Button();

            ((System.ComponentModel.ISupportInitialize)_prep).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_volume).BeginInit();
            SuspendLayout();
            //
            // _sectionPrep
            //
            _sectionPrep.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _sectionPrep.Location = new Point(16, 16);
            _sectionPrep.Size = new Size(300, 24);
            _sectionPrep.Text = "Prep && sound";
            //
            // _prepLabel
            //
            _prepLabel.Location = new Point(16, 48);
            _prepLabel.Size = new Size(260, 30);
            _prepLabel.Text = "Get-ready countdown";
            _prepLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _prep
            //
            _prep.Location = new Point(280, 44);
            _prep.Size = new Size(90, 34);
            _prep.TextAlign = HorizontalAlignment.Right;
            //
            // _prepUnit
            //
            _prepUnit.Location = new Point(376, 48);
            _prepUnit.Size = new Size(50, 30);
            _prepUnit.Text = "sec";
            //
            // _volumeLabel
            //
            _volumeLabel.Location = new Point(16, 86);
            _volumeLabel.Size = new Size(260, 30);
            _volumeLabel.Text = "Cue volume";
            _volumeLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _volume
            //
            _volume.Location = new Point(280, 82);
            _volume.Size = new Size(90, 34);
            _volume.TextAlign = HorizontalAlignment.Right;
            //
            // _volumeUnit
            //
            _volumeUnit.Location = new Point(376, 86);
            _volumeUnit.Size = new Size(50, 30);
            _volumeUnit.Text = "%";
            //
            // _sound
            //
            _sound.Location = new Point(16, 124);
            _sound.Size = new Size(300, 30);
            _sound.Text = "Play audible cues";
            //
            // _sectionClock
            //
            _sectionClock.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _sectionClock.Location = new Point(16, 168);
            _sectionClock.Size = new Size(300, 24);
            _sectionClock.Text = "Clock && window";
            //
            // _clockFormatLabel
            //
            _clockFormatLabel.Location = new Point(16, 200);
            _clockFormatLabel.Size = new Size(260, 30);
            _clockFormatLabel.Text = "Clock format";
            _clockFormatLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _clockFormat
            //
            _clockFormat.Items.AddRange(new object[] { "h:mm:ss tt", "h:mm tt", "HH:mm:ss", "HH:mm" });
            _clockFormat.Location = new Point(280, 196);
            _clockFormat.Size = new Size(160, 36);
            //
            // _showClock
            //
            _showClock.Location = new Point(16, 238);
            _showClock.Size = new Size(300, 30);
            _showClock.Text = "Show the time of day";
            //
            // _onTop
            //
            _onTop.Location = new Point(16, 272);
            _onTop.Size = new Size(300, 30);
            _onTop.Text = "Keep window on top";
            //
            // _sectionStation
            //
            _sectionStation.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _sectionStation.Location = new Point(16, 316);
            _sectionStation.Size = new Size(300, 24);
            _sectionStation.Text = "Station panel display";
            //
            // _showStationsTable
            //
            _showStationsTable.Location = new Point(16, 348);
            _showStationsTable.Size = new Size(420, 30);
            _showStationsTable.Text = "Show a stations table on screen";
            //
            // _showStationNameAsDescription
            //
            _showStationNameAsDescription.Location = new Point(16, 382);
            _showStationNameAsDescription.Size = new Size(420, 48);
            _showStationNameAsDescription.Text = "Show the station name as the description instead of WORK / REST / MOVE";
            //
            // _pathLabel
            //
            _pathLabel.Font = new Font("Segoe UI", 8F);
            _pathLabel.ForeColor = Color.DimGray;
            _pathLabel.Location = new Point(16, 442);
            _pathLabel.Size = new Size(420, 50);
            _pathLabel.Text = "";
            //
            // _cancelButton
            //
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Location = new Point(270, 506);
            _cancelButton.Size = new Size(90, 38);
            _cancelButton.Text = "Cancel";
            //
            // _okButton
            //
            _okButton.DialogResult = DialogResult.OK;
            _okButton.Location = new Point(366, 506);
            _okButton.Size = new Size(90, 38);
            _okButton.Text = "OK";
            _okButton.Click += Ok_Click;
            //
            // GeneralSettingsForm
            //
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(460, 560);
            Controls.Add(_sectionPrep);
            Controls.Add(_prepLabel);
            Controls.Add(_prep);
            Controls.Add(_prepUnit);
            Controls.Add(_volumeLabel);
            Controls.Add(_volume);
            Controls.Add(_volumeUnit);
            Controls.Add(_sound);
            Controls.Add(_sectionClock);
            Controls.Add(_clockFormatLabel);
            Controls.Add(_clockFormat);
            Controls.Add(_showClock);
            Controls.Add(_onTop);
            Controls.Add(_sectionStation);
            Controls.Add(_showStationsTable);
            Controls.Add(_showStationNameAsDescription);
            Controls.Add(_pathLabel);
            Controls.Add(_cancelButton);
            Controls.Add(_okButton);
            Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "GeneralSettingsForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "General Settings";
            // The owner (ProgramEditorForm) is TopMost when "keep window on top"
            // is on, and an owned non-topmost window isn't reliably kept above a
            // topmost owner by the OS - same reasoning as every other dialog here.
            TopMost = true;
            ((System.ComponentModel.ISupportInitialize)_prep).EndInit();
            ((System.ComponentModel.ISupportInitialize)_volume).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
