using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    public partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        private TabControl _tabs;
        private TabPage _timingTab;
        private TabPage _stationsTab;

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

        // ---- Stations tab
        private Label _stationsHelp;
        private DataGridView _stationsGrid;
        private DataGridViewTextBoxColumn _stationNameColumn;
        private DataGridViewTextBoxColumn _stationWorkColumn;
        private DataGridViewTextBoxColumn _stationRestColumn;
        private DataGridViewTextBoxColumn _stationColourColumn;
        private Button _addStationButton;
        private Button _duplicateStationButton;
        private Button _removeStationButton;
        private Button _moveStationUpButton;
        private Button _moveStationDownButton;
        private Button _importStationsButton;
        private Button _exportStationsButton;
        private Label _duplicateWarningLabel;
        private Label _stationsSummaryLabel;

        private Label _onScreenLabel;
        private CheckBox _showStationsTable;
        private CheckBox _useStationWording;
        private CheckBox _highlightCurrentStation;

        private Label _moveGroupLabel;
        private CheckBox _useMoveTime;
        private Label _moveSecondsLabel;
        private NumericUpDown _moveSeconds;
        private Label _moveSecondsUnit;
        private Label _moveMessageLabel;
        private TextBox _moveMessage;

        private Label _pathLabel;
        private Label _saveStatusLabel;
        private Button _saveButton;
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
            _tabs = new TabControl();
            _timingTab = new TabPage();
            _stationsTab = new TabPage();

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

            _stationsHelp = new Label();
            _stationsGrid = new DataGridView();
            _stationNameColumn = new DataGridViewTextBoxColumn();
            _stationWorkColumn = new DataGridViewTextBoxColumn();
            _stationRestColumn = new DataGridViewTextBoxColumn();
            _stationColourColumn = new DataGridViewTextBoxColumn();
            _addStationButton = new Button();
            _duplicateStationButton = new Button();
            _removeStationButton = new Button();
            _moveStationUpButton = new Button();
            _moveStationDownButton = new Button();
            _importStationsButton = new Button();
            _exportStationsButton = new Button();
            _duplicateWarningLabel = new Label();
            _stationsSummaryLabel = new Label();

            _onScreenLabel = new Label();
            _showStationsTable = new CheckBox();
            _useStationWording = new CheckBox();
            _highlightCurrentStation = new CheckBox();

            _moveGroupLabel = new Label();
            _useMoveTime = new CheckBox();
            _moveSecondsLabel = new Label();
            _moveSeconds = new NumericUpDown();
            _moveSecondsUnit = new Label();
            _moveMessageLabel = new Label();
            _moveMessage = new TextBox();

            _pathLabel = new Label();
            _saveStatusLabel = new Label();
            _saveButton = new Button();
            _okButton = new Button();
            _cancelButton = new Button();

            ((System.ComponentModel.ISupportInitialize)_prep).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_work).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_rest).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_rounds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_volume).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_stationsGrid).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_moveSeconds).BeginInit();
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
            // _clockFormatLabel
            //
            _clockFormatLabel.Location = new Point(12, 298);
            _clockFormatLabel.Name = "_clockFormatLabel";
            _clockFormatLabel.Size = new Size(172, 32);
            _clockFormatLabel.TabIndex = 20;
            _clockFormatLabel.Text = "Clock format";
            //
            // _clockFormat
            //
            _clockFormat.Items.AddRange(new object[] { "h:mm:ss tt", "h:mm tt", "HH:mm:ss", "HH:mm" });
            _clockFormat.Location = new Point(192, 294);
            _clockFormat.Name = "_clockFormat";
            _clockFormat.Size = new Size(150, 36);
            _clockFormat.TabIndex = 21;
            //
            // _restAfterFinal
            //
            _restAfterFinal.Location = new Point(192, 341);
            _restAfterFinal.Name = "_restAfterFinal";
            _restAfterFinal.Size = new Size(270, 36);
            _restAfterFinal.TabIndex = 22;
            _restAfterFinal.Text = "Rest after the final round";
            //
            // _sound
            //
            _sound.Location = new Point(192, 377);
            _sound.Name = "_sound";
            _sound.Size = new Size(270, 33);
            _sound.TabIndex = 23;
            _sound.Text = "Play audible cues";
            //
            // _showClock
            //
            _showClock.Location = new Point(192, 410);
            _showClock.Name = "_showClock";
            _showClock.Size = new Size(270, 42);
            _showClock.TabIndex = 24;
            _showClock.Text = "Show the time of day";
            //
            // _onTop
            //
            _onTop.Location = new Point(192, 452);
            _onTop.Name = "_onTop";
            _onTop.Size = new Size(270, 41);
            _onTop.TabIndex = 25;
            _onTop.Text = "Keep window on top";
            //
            // _planLabel
            //
            _planLabel.Location = new Point(12, 508);
            _planLabel.Name = "_planLabel";
            _planLabel.Size = new Size(172, 34);
            _planLabel.Text = "Variable plan";
            //
            // _plan
            //
            _plan.Location = new Point(192, 504);
            _plan.Name = "_plan";
            _plan.Size = new Size(270, 34);
            //
            // _planPreview
            //
            _planPreview.ForeColor = Color.DimGray;
            _planPreview.Location = new Point(12, 546);
            _planPreview.Name = "_planPreview";
            _planPreview.Size = new Size(638, 52);
            _planPreview.Text = "";
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
            // _timingTab
            //
            _timingTab.Name = "_timingTab";
            _timingTab.Text = "Timing && display";
            _timingTab.UseVisualStyleBackColor = true;
            _timingTab.Controls.Add(_prepLabel);
            _timingTab.Controls.Add(_prep);
            _timingTab.Controls.Add(_prepUnit);
            _timingTab.Controls.Add(_workLabelCaption);
            _timingTab.Controls.Add(_work);
            _timingTab.Controls.Add(_workUnit);
            _timingTab.Controls.Add(_restLabelCaption);
            _timingTab.Controls.Add(_rest);
            _timingTab.Controls.Add(_restUnit);
            _timingTab.Controls.Add(_roundsLabel);
            _timingTab.Controls.Add(_rounds);
            _timingTab.Controls.Add(_volumeLabel);
            _timingTab.Controls.Add(_volume);
            _timingTab.Controls.Add(_volumeUnit);
            _timingTab.Controls.Add(_workWordingLabel);
            _timingTab.Controls.Add(_workLabel);
            _timingTab.Controls.Add(_restWordingLabel);
            _timingTab.Controls.Add(_restLabel);
            _timingTab.Controls.Add(_clockFormatLabel);
            _timingTab.Controls.Add(_clockFormat);
            _timingTab.Controls.Add(_restAfterFinal);
            _timingTab.Controls.Add(_sound);
            _timingTab.Controls.Add(_showClock);
            _timingTab.Controls.Add(_onTop);
            _timingTab.Controls.Add(_planLabel);
            _timingTab.Controls.Add(_plan);
            _timingTab.Controls.Add(_planPreview);
            _timingTab.Controls.Add(_presetLabel);
            _timingTab.Controls.Add(_presetButton1);
            _timingTab.Controls.Add(_presetButton2);
            _timingTab.Controls.Add(_presetButton3);
            _timingTab.Controls.Add(_presetButton4);
            _timingTab.Controls.Add(_presetButton5);
            _timingTab.Controls.Add(_presetButton6);
            _timingTab.Controls.Add(_presetButton7);
            _timingTab.Controls.Add(_presetButton8);
            _timingTab.Controls.Add(_presetButton9);
            _timingTab.Controls.Add(_presetButton10);
            //
            // _stationsHelp
            //
            _stationsHelp.Location = new Point(12, 10);
            _stationsHelp.Name = "_stationsHelp";
            _stationsHelp.Size = new Size(550, 22);
            _stationsHelp.Text = "One row per station. Repeats are fine, but not directly back-to-back.";
            //
            // _stationNameColumn
            //
            _stationNameColumn.HeaderText = "Station / exercise";
            _stationNameColumn.Name = "_stationNameColumn";
            _stationNameColumn.FillWeight = 28F;
            //
            // _stationWorkColumn
            //
            _stationWorkColumn.HeaderText = "During WORK";
            _stationWorkColumn.Name = "_stationWorkColumn";
            _stationWorkColumn.FillWeight = 30F;
            //
            // _stationRestColumn
            //
            _stationRestColumn.HeaderText = "During REST";
            _stationRestColumn.Name = "_stationRestColumn";
            _stationRestColumn.FillWeight = 30F;
            //
            // _stationColourColumn
            //
            _stationColourColumn.HeaderText = "Colour";
            _stationColourColumn.Name = "_stationColourColumn";
            _stationColourColumn.ToolTipText = "Optional, e.g. #2AA7A0 or SkyBlue";
            _stationColourColumn.FillWeight = 12F;
            //
            // _stationsGrid
            //
            _stationsGrid.AllowUserToAddRows = true;
            _stationsGrid.AllowUserToDeleteRows = true;
            _stationsGrid.AllowUserToResizeRows = false;
            _stationsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _stationsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _stationsGrid.Columns.AddRange(new DataGridViewColumn[] {
                _stationNameColumn, _stationWorkColumn, _stationRestColumn, _stationColourColumn });
            _stationsGrid.Location = new Point(12, 36);
            _stationsGrid.MultiSelect = false;
            _stationsGrid.Name = "_stationsGrid";
            _stationsGrid.RowHeadersWidth = 30;
            _stationsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _stationsGrid.Size = new Size(550, 336);
            _stationsGrid.CellEndEdit += StationsGrid_Changed;
            _stationsGrid.RowsRemoved += StationsGrid_Changed;
            //
            // _addStationButton
            //
            _addStationButton.Location = new Point(12, 380);
            _addStationButton.Name = "_addStationButton";
            _addStationButton.Size = new Size(70, 34);
            _addStationButton.Text = "Add";
            _addStationButton.Click += AddStation_Click;
            //
            // _duplicateStationButton
            //
            _duplicateStationButton.Location = new Point(88, 380);
            _duplicateStationButton.Name = "_duplicateStationButton";
            _duplicateStationButton.Size = new Size(90, 34);
            _duplicateStationButton.Text = "Duplicate";
            _duplicateStationButton.Click += DuplicateStation_Click;
            //
            // _removeStationButton
            //
            _removeStationButton.Location = new Point(184, 380);
            _removeStationButton.Name = "_removeStationButton";
            _removeStationButton.Size = new Size(90, 34);
            _removeStationButton.Text = "Remove";
            _removeStationButton.Click += RemoveStation_Click;
            //
            // _moveStationUpButton
            //
            _moveStationUpButton.Location = new Point(280, 380);
            _moveStationUpButton.Name = "_moveStationUpButton";
            _moveStationUpButton.Size = new Size(65, 34);
            _moveStationUpButton.Text = "Up";
            _moveStationUpButton.Click += MoveStationUp_Click;
            //
            // _moveStationDownButton
            //
            _moveStationDownButton.Location = new Point(351, 380);
            _moveStationDownButton.Name = "_moveStationDownButton";
            _moveStationDownButton.Size = new Size(75, 34);
            _moveStationDownButton.Text = "Down";
            _moveStationDownButton.Click += MoveStationDown_Click;
            //
            // _importStationsButton
            //
            _importStationsButton.Location = new Point(12, 420);
            _importStationsButton.Name = "_importStationsButton";
            _importStationsButton.Size = new Size(110, 34);
            _importStationsButton.Text = "Import...";
            _importStationsButton.Click += ImportStations_Click;
            //
            // _exportStationsButton
            //
            _exportStationsButton.Location = new Point(132, 420);
            _exportStationsButton.Name = "_exportStationsButton";
            _exportStationsButton.Size = new Size(110, 34);
            _exportStationsButton.Text = "Export...";
            _exportStationsButton.Click += ExportStations_Click;
            //
            // _duplicateWarningLabel
            //
            _duplicateWarningLabel.ForeColor = Color.FromArgb(200, 60, 40);
            _duplicateWarningLabel.Location = new Point(12, 460);
            _duplicateWarningLabel.Name = "_duplicateWarningLabel";
            _duplicateWarningLabel.Size = new Size(550, 20);
            _duplicateWarningLabel.Text = "";
            //
            // _stationsSummaryLabel
            //
            _stationsSummaryLabel.ForeColor = Color.DimGray;
            _stationsSummaryLabel.Location = new Point(12, 482);
            _stationsSummaryLabel.Name = "_stationsSummaryLabel";
            _stationsSummaryLabel.Size = new Size(550, 40);
            _stationsSummaryLabel.Text = "";
            //
            // _onScreenLabel
            //
            _onScreenLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _onScreenLabel.Location = new Point(576, 10);
            _onScreenLabel.Name = "_onScreenLabel";
            _onScreenLabel.Size = new Size(288, 22);
            _onScreenLabel.Text = "On the main screen:";
            //
            // _showStationsTable
            //
            _showStationsTable.Location = new Point(576, 36);
            _showStationsTable.Name = "_showStationsTable";
            _showStationsTable.Size = new Size(288, 26);
            _showStationsTable.Text = "Show a stations table on screen";
            //
            // _useStationWording
            //
            _useStationWording.Location = new Point(576, 66);
            _useStationWording.Name = "_useStationWording";
            _useStationWording.Size = new Size(288, 44);
            _useStationWording.Text = "Replace the WORK/REST word with each station's own instruction, when it has one";
            //
            // _highlightCurrentStation
            //
            _highlightCurrentStation.Location = new Point(576, 114);
            _highlightCurrentStation.Name = "_highlightCurrentStation";
            _highlightCurrentStation.Size = new Size(288, 64);
            _highlightCurrentStation.Text = "Highlight the station in progress — turn off if the class rotates through " +
                "every station each round instead of moving through them together";
            //
            // _moveGroupLabel
            //
            _moveGroupLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _moveGroupLabel.Location = new Point(576, 186);
            _moveGroupLabel.Name = "_moveGroupLabel";
            _moveGroupLabel.Size = new Size(288, 22);
            _moveGroupLabel.Text = "Move between stations:";
            //
            // _useMoveTime
            //
            _useMoveTime.Location = new Point(576, 212);
            _useMoveTime.Name = "_useMoveTime";
            _useMoveTime.Size = new Size(288, 28);
            _useMoveTime.Text = "Include a move time between stations";
            //
            // _moveSecondsLabel
            //
            _moveSecondsLabel.Location = new Point(576, 250);
            _moveSecondsLabel.Name = "_moveSecondsLabel";
            _moveSecondsLabel.Size = new Size(140, 30);
            _moveSecondsLabel.Text = "Move duration";
            _moveSecondsLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _moveSeconds
            //
            _moveSeconds.Location = new Point(726, 246);
            _moveSeconds.Name = "_moveSeconds";
            _moveSeconds.Size = new Size(70, 34);
            _moveSeconds.TextAlign = HorizontalAlignment.Right;
            //
            // _moveSecondsUnit
            //
            _moveSecondsUnit.Location = new Point(800, 250);
            _moveSecondsUnit.Name = "_moveSecondsUnit";
            _moveSecondsUnit.Size = new Size(50, 30);
            _moveSecondsUnit.Text = "sec";
            //
            // _moveMessageLabel
            //
            _moveMessageLabel.Location = new Point(576, 288);
            _moveMessageLabel.Name = "_moveMessageLabel";
            _moveMessageLabel.Size = new Size(288, 22);
            _moveMessageLabel.Text = "Move message";
            //
            // _moveMessage
            //
            _moveMessage.Location = new Point(576, 312);
            _moveMessage.Name = "_moveMessage";
            _moveMessage.Size = new Size(288, 34);
            //
            // _stationsTab
            //
            _stationsTab.Name = "_stationsTab";
            _stationsTab.Text = "Stations";
            _stationsTab.UseVisualStyleBackColor = true;
            _stationsTab.Controls.Add(_stationsHelp);
            _stationsTab.Controls.Add(_stationsGrid);
            _stationsTab.Controls.Add(_addStationButton);
            _stationsTab.Controls.Add(_duplicateStationButton);
            _stationsTab.Controls.Add(_removeStationButton);
            _stationsTab.Controls.Add(_moveStationUpButton);
            _stationsTab.Controls.Add(_moveStationDownButton);
            _stationsTab.Controls.Add(_importStationsButton);
            _stationsTab.Controls.Add(_exportStationsButton);
            _stationsTab.Controls.Add(_duplicateWarningLabel);
            _stationsTab.Controls.Add(_stationsSummaryLabel);
            _stationsTab.Controls.Add(_onScreenLabel);
            _stationsTab.Controls.Add(_showStationsTable);
            _stationsTab.Controls.Add(_useStationWording);
            _stationsTab.Controls.Add(_highlightCurrentStation);
            _stationsTab.Controls.Add(_moveGroupLabel);
            _stationsTab.Controls.Add(_useMoveTime);
            _stationsTab.Controls.Add(_moveSecondsLabel);
            _stationsTab.Controls.Add(_moveSeconds);
            _stationsTab.Controls.Add(_moveSecondsUnit);
            _stationsTab.Controls.Add(_moveMessageLabel);
            _stationsTab.Controls.Add(_moveMessage);
            //
            // _tabs
            //
            _tabs.Location = new Point(12, 12);
            _tabs.Name = "_tabs";
            _tabs.SelectedIndex = 0;
            _tabs.Size = new Size(876, 690);
            _tabs.TabIndex = 0;
            _tabs.Controls.Add(_timingTab);
            _tabs.Controls.Add(_stationsTab);
            //
            // _pathLabel
            //
            _pathLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _pathLabel.Font = new Font("Segoe UI", 8F);
            _pathLabel.ForeColor = Color.DimGray;
            _pathLabel.Location = new Point(16, 714);
            _pathLabel.Name = "_pathLabel";
            _pathLabel.Size = new Size(560, 40);
            _pathLabel.TabIndex = 27;
            _pathLabel.Text = "Settings file (editable in Notepad):\r\nC:\\Users\\c.bucknell\\AppData\\Roaming\\GymClock\\settings.txt";
            //
            // _saveStatusLabel
            //
            _saveStatusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _saveStatusLabel.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            _saveStatusLabel.ForeColor = Color.FromArgb(15, 138, 70);
            _saveStatusLabel.Location = new Point(16, 756);
            _saveStatusLabel.Name = "_saveStatusLabel";
            _saveStatusLabel.Size = new Size(560, 22);
            _saveStatusLabel.Text = "";
            //
            // _saveButton
            //
            _saveButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _saveButton.Location = new Point(592, 728);
            _saveButton.Name = "_saveButton";
            _saveButton.Size = new Size(90, 38);
            _saveButton.TabIndex = 28;
            _saveButton.Text = "Save";
            _saveButton.Click += Save_Click;
            //
            // _okButton
            //
            _okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _okButton.DialogResult = DialogResult.OK;
            _okButton.Location = new Point(688, 728);
            _okButton.Name = "_okButton";
            _okButton.Size = new Size(90, 38);
            _okButton.TabIndex = 29;
            _okButton.Text = "OK";
            _okButton.Click += Ok_Click;
            //
            // _cancelButton
            //
            _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Location = new Point(784, 728);
            _cancelButton.Name = "_cancelButton";
            _cancelButton.Size = new Size(90, 38);
            _cancelButton.TabIndex = 30;
            _cancelButton.Text = "Cancel";
            //
            // SettingsForm
            //
            AcceptButton = _okButton;
            AutoScaleMode = AutoScaleMode.None;
            CancelButton = _cancelButton;
            ClientSize = new Size(900, 800);
            Controls.Add(_tabs);
            Controls.Add(_pathLabel);
            Controls.Add(_saveStatusLabel);
            Controls.Add(_saveButton);
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
            ((System.ComponentModel.ISupportInitialize)_stationsGrid).EndInit();
            ((System.ComponentModel.ISupportInitialize)_moveSeconds).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    }
}
