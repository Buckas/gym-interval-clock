using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    public partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        private Label _prepLabel;
        private NumericUpDown _prep;
        private Label _prepUnit;

        private Label _workLabelCaption;
        private NumericUpDown _work;
        private Label _workUnit;

        private Label _restLabelCaption;
        private NumericUpDown _rest;
        private Label _restUnit;

        private Label _roundsLabel;
        private NumericUpDown _rounds;

        private Label _volumeLabel;
        private NumericUpDown _volume;
        private Label _volumeUnit;

        private Label _workWordingLabel;
        private TextBox _workLabel;

        private Label _restWordingLabel;
        private TextBox _restLabel;

        private Label _stationsLabel;
        private TextBox _stations;

        private Label _clockFormatLabel;
        private ComboBox _clockFormat;

        private Label _planLabel;
        private TextBox _plan;
        private Label _planPreview;

        private CheckBox _restAfterFinal;
        private CheckBox _sound;
        private CheckBox _showClock;
        private CheckBox _onTop;

        private Label _presetLabel;
        private Button _presetButton1;
        private Button _presetButton2;
        private Button _presetButton3;
        private Button _presetButton4;
        private Button _presetButton5;
        private Button _presetButton6;
        private Button _presetButton7;
        private Button _presetButton8;
        private Button _presetButton9;
        private Button _presetButton10;

        private Label _pathLabel;
        private Button _okButton;
        private Button _cancelButton;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            _prepLabel = new Label();
            _prep = new NumericUpDown();
            _prepUnit = new Label();
            _workLabelCaption = new Label();
            _work = new NumericUpDown();
            _workUnit = new Label();
            _restLabelCaption = new Label();
            _rest = new NumericUpDown();
            _restUnit = new Label();
            _roundsLabel = new Label();
            _rounds = new NumericUpDown();
            _volumeLabel = new Label();
            _volume = new NumericUpDown();
            _volumeUnit = new Label();
            _workWordingLabel = new Label();
            _workLabel = new TextBox();
            _restWordingLabel = new Label();
            _restLabel = new TextBox();
            _stationsLabel = new Label();
            _stations = new TextBox();
            _clockFormatLabel = new Label();
            _clockFormat = new ComboBox();
            _planLabel = new Label();
            _plan = new TextBox();
            _planPreview = new Label();
            _restAfterFinal = new CheckBox();
            _sound = new CheckBox();
            _showClock = new CheckBox();
            _onTop = new CheckBox();
            _presetLabel = new Label();
            _presetButton1 = new Button();
            _presetButton2 = new Button();
            _presetButton3 = new Button();
            _presetButton4 = new Button();
            _presetButton5 = new Button();
            _presetButton6 = new Button();
            _presetButton7 = new Button();
            _presetButton8 = new Button();
            _presetButton9 = new Button();
            _presetButton10 = new Button();
            _pathLabel = new Label();
            _okButton = new Button();
            _cancelButton = new Button();
            ((System.ComponentModel.ISupportInitialize)_prep).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_work).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_rest).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_rounds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_volume).BeginInit();
            SuspendLayout();
            // 
            // _prepLabel
            // 
            _prepLabel.Location = new Point(12, 15);
            _prepLabel.Name = "_prepLabel";
            _prepLabel.Size = new Size(281, 30);
            _prepLabel.TabIndex = 0;
            _prepLabel.Text = "Get ready countdown";
            _prepLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _prep
            // 
            _prep.Location = new Point(299, 11);
            _prep.Name = "_prep";
            _prep.Size = new Size(90, 34);
            _prep.TabIndex = 1;
            _prep.TextAlign = HorizontalAlignment.Right;
            // 
            // _prepUnit
            // 
            _prepUnit.Location = new Point(395, 15);
            _prepUnit.Name = "_prepUnit";
            _prepUnit.Size = new Size(60, 30);
            _prepUnit.TabIndex = 2;
            _prepUnit.Text = "sec";
            // 
            // _workLabelCaption
            // 
            _workLabelCaption.Location = new Point(16, 50);
            _workLabelCaption.Name = "_workLabelCaption";
            _workLabelCaption.Size = new Size(277, 30);
            _workLabelCaption.TabIndex = 3;
            _workLabelCaption.Text = "Work interval";
            _workLabelCaption.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _work
            // 
            _work.Location = new Point(299, 50);
            _work.Name = "_work";
            _work.Size = new Size(90, 34);
            _work.TabIndex = 4;
            _work.TextAlign = HorizontalAlignment.Right;
            // 
            // _workUnit
            // 
            _workUnit.Location = new Point(395, 54);
            _workUnit.Name = "_workUnit";
            _workUnit.Size = new Size(60, 30);
            _workUnit.TabIndex = 5;
            _workUnit.Text = "sec";
            // 
            // _restLabelCaption
            // 
            _restLabelCaption.Location = new Point(16, 89);
            _restLabelCaption.Name = "_restLabelCaption";
            _restLabelCaption.Size = new Size(277, 34);
            _restLabelCaption.TabIndex = 6;
            _restLabelCaption.Text = "Rest interval";
            _restLabelCaption.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _rest
            // 
            _rest.Location = new Point(299, 89);
            _rest.Name = "_rest";
            _rest.Size = new Size(90, 34);
            _rest.TabIndex = 7;
            _rest.TextAlign = HorizontalAlignment.Right;
            // 
            // _restUnit
            // 
            _restUnit.Location = new Point(395, 93);
            _restUnit.Name = "_restUnit";
            _restUnit.Size = new Size(60, 30);
            _restUnit.TabIndex = 8;
            _restUnit.Text = "sec";
            // 
            // _roundsLabel
            // 
            _roundsLabel.Location = new Point(12, 131);
            _roundsLabel.Name = "_roundsLabel";
            _roundsLabel.Size = new Size(281, 34);
            _roundsLabel.TabIndex = 9;
            _roundsLabel.Text = "Rounds (0 = continuous)";
            _roundsLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _rounds
            // 
            _rounds.Location = new Point(299, 129);
            _rounds.Name = "_rounds";
            _rounds.Size = new Size(90, 34);
            _rounds.TabIndex = 10;
            _rounds.TextAlign = HorizontalAlignment.Right;
            // 
            // _volumeLabel
            // 
            _volumeLabel.Location = new Point(12, 169);
            _volumeLabel.Name = "_volumeLabel";
            _volumeLabel.Size = new Size(281, 30);
            _volumeLabel.TabIndex = 11;
            _volumeLabel.Text = "Cue volume";
            _volumeLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _volume
            // 
            _volume.Location = new Point(299, 167);
            _volume.Name = "_volume";
            _volume.Size = new Size(90, 34);
            _volume.TabIndex = 12;
            _volume.TextAlign = HorizontalAlignment.Right;
            // 
            // _volumeUnit
            // 
            _volumeUnit.Location = new Point(395, 171);
            _volumeUnit.Name = "_volumeUnit";
            _volumeUnit.Size = new Size(60, 33);
            _volumeUnit.TabIndex = 13;
            _volumeUnit.Text = "%";
            // 
            // _workWordingLabel
            // 
            _workWordingLabel.Location = new Point(12, 211);
            _workWordingLabel.Name = "_workWordingLabel";
            _workWordingLabel.Size = new Size(172, 34);
            _workWordingLabel.TabIndex = 14;
            _workWordingLabel.Text = "Work wording";
            // 
            // _workLabel
            // 
            _workLabel.Location = new Point(192, 207);
            _workLabel.Name = "_workLabel";
            _workLabel.Size = new Size(270, 34);
            _workLabel.TabIndex = 15;
            // 
            // _restWordingLabel
            // 
            _restWordingLabel.Location = new Point(12, 253);
            _restWordingLabel.Name = "_restWordingLabel";
            _restWordingLabel.Size = new Size(172, 34);
            _restWordingLabel.TabIndex = 16;
            _restWordingLabel.Text = "Rest wording";
            // 
            // _restLabel
            // 
            _restLabel.Location = new Point(192, 250);
            _restLabel.Name = "_restLabel";
            _restLabel.Size = new Size(270, 34);
            _restLabel.TabIndex = 17;
            // 
            // _stationsLabel
            // 
            _stationsLabel.Location = new Point(12, 298);
            _stationsLabel.Name = "_stationsLabel";
            _stationsLabel.Size = new Size(172, 30);
            _stationsLabel.TabIndex = 18;
            _stationsLabel.Text = "Stations (optional)";
            // 
            // _stations
            // 
            _stations.Location = new Point(192, 294);
            _stations.Name = "_stations";
            _stations.Size = new Size(270, 34);
            _stations.TabIndex = 19;
            // 
            // _clockFormatLabel
            // 
            _clockFormatLabel.Location = new Point(12, 340);
            _clockFormatLabel.Name = "_clockFormatLabel";
            _clockFormatLabel.Size = new Size(172, 32);
            _clockFormatLabel.TabIndex = 20;
            _clockFormatLabel.Text = "Clock format";
            // 
            // _clockFormat
            // 
            _clockFormat.Items.AddRange(new object[] { "h:mm:ss tt", "h:mm tt", "HH:mm:ss", "HH:mm" });
            _clockFormat.Location = new Point(192, 336);
            _clockFormat.Name = "_clockFormat";
            _clockFormat.Size = new Size(150, 36);
            _clockFormat.TabIndex = 21;
            // 
            // _restAfterFinal
            // 
            _restAfterFinal.Location = new Point(192, 383);
            _restAfterFinal.Name = "_restAfterFinal";
            _restAfterFinal.Size = new Size(270, 36);
            _restAfterFinal.TabIndex = 22;
            _restAfterFinal.Text = "Rest after the final round";
            // 
            // _sound
            // 
            _sound.Location = new Point(192, 419);
            _sound.Name = "_sound";
            _sound.Size = new Size(270, 33);
            _sound.TabIndex = 23;
            _sound.Text = "Play audible cues";
            // 
            // _showClock
            // 
            _showClock.Location = new Point(192, 452);
            _showClock.Name = "_showClock";
            _showClock.Size = new Size(270, 42);
            _showClock.TabIndex = 24;
            _showClock.Text = "Show the time of day";
            // 
            // _onTop
            // 
            _onTop.Location = new Point(192, 494);
            _onTop.Name = "_onTop";
            _onTop.Size = new Size(270, 41);
            _onTop.TabIndex = 25;
            _onTop.Text = "Keep window on top";
            // 
            // _presetLabel
            // 
            _presetLabel.Location = new Point(500, 15);
            _presetLabel.Name = "_presetLabel";
            _presetLabel.Size = new Size(160, 39);
            _presetLabel.TabIndex = 26;
            _presetLabel.Text = "Quick presets (also keys 1-9 on the clock):";
            // 
            // _presetButton1
            // 
            _presetButton1.Location = new Point(500, 60);
            _presetButton1.Name = "_presetButton1";
            _presetButton1.Size = new Size(150, 48);
            _presetButton1.TabIndex = 30;
            _presetButton1.Click += Preset_Click;
            // 
            // _presetButton2
            // 
            _presetButton2.Location = new Point(500, 116);
            _presetButton2.Name = "_presetButton2";
            _presetButton2.Size = new Size(150, 48);
            _presetButton2.TabIndex = 31;
            _presetButton2.Click += Preset_Click;
            // 
            // _presetButton3
            // 
            _presetButton3.Location = new Point(500, 172);
            _presetButton3.Name = "_presetButton3";
            _presetButton3.Size = new Size(150, 48);
            _presetButton3.TabIndex = 32;
            _presetButton3.Click += Preset_Click;
            // 
            // _presetButton4
            // 
            _presetButton4.Location = new Point(500, 228);
            _presetButton4.Name = "_presetButton4";
            _presetButton4.Size = new Size(150, 48);
            _presetButton4.TabIndex = 33;
            _presetButton4.Click += Preset_Click;
            // 
            // _presetButton5
            // 
            _presetButton5.Location = new Point(500, 284);
            _presetButton5.Name = "_presetButton5";
            _presetButton5.Size = new Size(150, 48);
            _presetButton5.TabIndex = 34;
            _presetButton5.Click += Preset_Click;
            // 
            // _presetButton6
            // 
            _presetButton6.Location = new Point(500, 340);
            _presetButton6.Name = "_presetButton6";
            _presetButton6.Size = new Size(150, 48);
            _presetButton6.TabIndex = 35;
            _presetButton6.Click += Preset_Click;
            // 
            // _presetButton7
            // 
            _presetButton7.Location = new Point(500, 396);
            _presetButton7.Name = "_presetButton7";
            _presetButton7.Size = new Size(150, 48);
            _presetButton7.TabIndex = 36;
            _presetButton7.Click += Preset_Click;
            // 
            // _presetButton8
            // 
            _presetButton8.Location = new Point(500, 452);
            _presetButton8.Name = "_presetButton8";
            _presetButton8.Size = new Size(150, 48);
            _presetButton8.TabIndex = 37;
            _presetButton8.Click += Preset_Click;
            // 
            // _presetButton9
            // 
            _presetButton9.Location = new Point(500, 508);
            _presetButton9.Name = "_presetButton9";
            _presetButton9.Size = new Size(150, 48);
            _presetButton9.TabIndex = 38;
            _presetButton9.Click += Preset_Click;
            // 
            // _presetButton10
            // 
            _presetButton10.Location = new Point(500, 564);
            _presetButton10.Name = "_presetButton10";
            _presetButton10.Size = new Size(150, 48);
            _presetButton10.TabIndex = 39;
            _presetButton10.Click += Preset_Click;
            // 
            // _pathLabel
            // 
            _pathLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _pathLabel.Font = new Font("Segoe UI", 8F);
            _pathLabel.ForeColor = Color.DimGray;
            // _planLabel
            _planLabel.Location = new Point(12, 550);
            _planLabel.Name = "_planLabel";
            _planLabel.Size = new Size(172, 34);
            _planLabel.Text = "Variable plan";

            // _plan
            _plan.Location = new Point(192, 546);
            _plan.Name = "_plan";
            _plan.Size = new Size(270, 34);

            // _planPreview
            _planPreview.ForeColor = Color.DimGray;
            _planPreview.Location = new Point(12, 588);
            _planPreview.Name = "_planPreview";
            _planPreview.Size = new Size(638, 36);
            _planPreview.Text = "";

            _pathLabel.Location = new Point(16, 632);
            _pathLabel.Name = "_pathLabel";
            _pathLabel.Size = new Size(634, 48);
            _pathLabel.TabIndex = 27;
            _pathLabel.Text = "Settings file (editable in Notepad):\r\nC:\\Users\\c.bucknell\\AppData\\Roaming\\GymClock\\settings.txt";
            // 
            // _okButton
            // 
            _okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _okButton.DialogResult = DialogResult.OK;
            _okButton.Location = new Point(448, 702);
            _okButton.Name = "_okButton";
            _okButton.Size = new Size(90, 38);
            _okButton.TabIndex = 28;
            _okButton.Text = "OK";
            _okButton.Click += Ok_Click;
            // 
            // _cancelButton
            // 
            _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Location = new Point(560, 702);
            _cancelButton.Name = "_cancelButton";
            _cancelButton.Size = new Size(90, 38);
            _cancelButton.TabIndex = 29;
            _cancelButton.Text = "Cancel";
            // 
            // SettingsForm
            // 
            AcceptButton = _okButton;
            AutoScaleMode = AutoScaleMode.None;
            CancelButton = _cancelButton;
            ClientSize = new Size(666, 760);
            Controls.Add(_prepLabel);
            Controls.Add(_prep);
            Controls.Add(_prepUnit);
            Controls.Add(_workLabelCaption);
            Controls.Add(_work);
            Controls.Add(_workUnit);
            Controls.Add(_restLabelCaption);
            Controls.Add(_rest);
            Controls.Add(_restUnit);
            Controls.Add(_roundsLabel);
            Controls.Add(_rounds);
            Controls.Add(_volumeLabel);
            Controls.Add(_volume);
            Controls.Add(_volumeUnit);
            Controls.Add(_workWordingLabel);
            Controls.Add(_workLabel);
            Controls.Add(_restWordingLabel);
            Controls.Add(_restLabel);
            Controls.Add(_stationsLabel);
            Controls.Add(_stations);
            Controls.Add(_clockFormatLabel);
            Controls.Add(_clockFormat);
            Controls.Add(_restAfterFinal);
            Controls.Add(_sound);
            Controls.Add(_showClock);
            Controls.Add(_onTop);
            Controls.Add(_presetLabel);
            Controls.Add(_presetButton1);
            Controls.Add(_presetButton2);
            Controls.Add(_presetButton3);
            Controls.Add(_presetButton4);
            Controls.Add(_presetButton5);
            Controls.Add(_presetButton6);
            Controls.Add(_presetButton7);
            Controls.Add(_presetButton8);
            Controls.Add(_presetButton9);
            Controls.Add(_presetButton10);
            Controls.Add(_planLabel);
            Controls.Add(_plan);
            Controls.Add(_planPreview);
            Controls.Add(_pathLabel);
            Controls.Add(_okButton);
            Controls.Add(_cancelButton);
            Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "SettingsForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Interval settings";
            TopMost = true;
            ((System.ComponentModel.ISupportInitialize)_prep).EndInit();
            ((System.ComponentModel.ISupportInitialize)_work).EndInit();
            ((System.ComponentModel.ISupportInitialize)_rest).EndInit();
            ((System.ComponentModel.ISupportInitialize)_rounds).EndInit();
            ((System.ComponentModel.ISupportInitialize)_volume).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    }
}
