using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    public partial class ProgramEditorForm
    {
        private System.ComponentModel.IContainer components = null;

        // ---- source switch
        private Label _sourceLabel;
        private RadioButton _sourceSimple;
        private RadioButton _sourceBuilt;
        private Label _sourceDesc;
        private Button _generalSettingsButton;

        // ---- Simple session panel
        private Panel _simplePanel;
        private Label _timingHeader;
        private Label _workLabelCaption;
        private NumericUpDown _work;
        private Label _workUnit;
        private Label _restLabelCaption;
        private NumericUpDown _rest;
        private Label _restUnit;
        private Label _roundsLabel;
        private NumericUpDown _rounds;
        private Label _workWordingLabel;
        private TextBox _workLabelText;
        private Label _restWordingLabel;
        private TextBox _restLabelText;
        private CheckBox _restAfterFinal;
        private Label _planLabel;
        private TextBox _plan;
        private Label _planPreview;
        private Label _moveHeader;
        private CheckBox _useMoveTime;
        private Label _moveSecondsLabel;
        private NumericUpDown _moveSeconds;
        private Label _moveSecondsUnit;
        private Label _moveMessageLabel;
        private TextBox _moveMessage;

        // ---- Built program panel
        private Panel _builtPanel;
        private Label _nameCaption;
        private TextBox _programName;
        private Label _modeCaption;
        private RadioButton _modeShared;
        private RadioButton _modeSequential;
        private RadioButton _modeParallel;
        private Label _modeDesc;
        private Button _quickWizardButton;
        private Button _importButton;
        private Button _exportButton;
        private Label _instructions;

        private Label _outlineLabel;
        private TreeView _outline;
        private Button _addStationButton;
        private Button _addBetweenRestButton;
        private Button _addBetweenMoveButton;
        private Button _duplicateStationButton;
        private Button _deleteStationButton;
        private Button _moveStationUpButton;
        private Button _moveStationDownButton;

        private Label _timelineLabel;
        private ListBox _timelineList;
        private Button _addWorkButton;
        private Button _addRecoveryButton;
        private Button _addRestButton;
        private Button _addMoveButton;
        private Button _addRepeatGroupButton;
        private Button _addPatternButton;
        private Button _duplicateBlockButton;
        private Button _deleteBlockButton;
        private Button _moveBlockUpButton;
        private Button _moveBlockDownButton;

        private Label _propertiesLabel;
        private PropertyGrid _properties;

        private Label _scriptLabel;
        private TextBox _script;

        // ---- footer
        private Label _pathLabel;
        private Label _saveStatusLabel;
        private Button _saveButton;
        private Button _okButton;
        private Button _cancelButton;
        private ToolTip _tip;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null) components.Dispose();
                if (_tip != null) _tip.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _sourceLabel = new Label();
            _sourceSimple = new RadioButton();
            _sourceBuilt = new RadioButton();
            _sourceDesc = new Label();
            _generalSettingsButton = new Button();

            _simplePanel = new Panel();
            _timingHeader = new Label();
            _workLabelCaption = new Label();
            _work = new NumericUpDown();
            _workUnit = new Label();
            _restLabelCaption = new Label();
            _rest = new NumericUpDown();
            _restUnit = new Label();
            _roundsLabel = new Label();
            _rounds = new NumericUpDown();
            _workWordingLabel = new Label();
            _workLabelText = new TextBox();
            _restWordingLabel = new Label();
            _restLabelText = new TextBox();
            _restAfterFinal = new CheckBox();
            _planLabel = new Label();
            _plan = new TextBox();
            _planPreview = new Label();
            _moveHeader = new Label();
            _useMoveTime = new CheckBox();
            _moveSecondsLabel = new Label();
            _moveSeconds = new NumericUpDown();
            _moveSecondsUnit = new Label();
            _moveMessageLabel = new Label();
            _moveMessage = new TextBox();

            _builtPanel = new Panel();
            _nameCaption = new Label();
            _programName = new TextBox();
            _modeCaption = new Label();
            _modeShared = new RadioButton();
            _modeSequential = new RadioButton();
            _modeParallel = new RadioButton();
            _modeDesc = new Label();
            _quickWizardButton = new Button();
            _importButton = new Button();
            _exportButton = new Button();
            _instructions = new Label();

            _outlineLabel = new Label();
            _outline = new TreeView();
            _addStationButton = new Button();
            _addBetweenRestButton = new Button();
            _addBetweenMoveButton = new Button();
            _duplicateStationButton = new Button();
            _deleteStationButton = new Button();
            _moveStationUpButton = new Button();
            _moveStationDownButton = new Button();

            _timelineLabel = new Label();
            _timelineList = new ListBox();
            _addWorkButton = new Button();
            _addRecoveryButton = new Button();
            _addRestButton = new Button();
            _addMoveButton = new Button();
            _addRepeatGroupButton = new Button();
            _addPatternButton = new Button();
            _duplicateBlockButton = new Button();
            _deleteBlockButton = new Button();
            _moveBlockUpButton = new Button();
            _moveBlockDownButton = new Button();

            _propertiesLabel = new Label();
            _properties = new PropertyGrid();

            _scriptLabel = new Label();
            _script = new TextBox();

            _pathLabel = new Label();
            _saveStatusLabel = new Label();
            _saveButton = new Button();
            _okButton = new Button();
            _cancelButton = new Button();
            _tip = new ToolTip();

            ((System.ComponentModel.ISupportInitialize)_work).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_rest).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_rounds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_moveSeconds).BeginInit();
            _simplePanel.SuspendLayout();
            _builtPanel.SuspendLayout();
            SuspendLayout();

            // ================================================= SOURCE SWITCH
            //
            // _sourceLabel
            //
            _sourceLabel.Font = new Font("Segoe UI", 8F);
            _sourceLabel.ForeColor = Color.DimGray;
            _sourceLabel.Location = new Point(12, 10);
            _sourceLabel.Size = new Size(120, 18);
            _sourceLabel.Text = "WHAT'S RUNNING";
            //
            // _sourceSimple
            //
            _sourceSimple.Appearance = Appearance.Button;
            _sourceSimple.Location = new Point(12, 30);
            _sourceSimple.Size = new Size(130, 32);
            _sourceSimple.Text = "Simple session";
            _sourceSimple.TextAlign = ContentAlignment.MiddleCenter;
            //
            // _sourceBuilt
            //
            _sourceBuilt.Appearance = Appearance.Button;
            _sourceBuilt.Location = new Point(144, 30);
            _sourceBuilt.Size = new Size(130, 32);
            _sourceBuilt.Text = "Built program";
            _sourceBuilt.TextAlign = ContentAlignment.MiddleCenter;
            //
            // _sourceDesc
            //
            _sourceDesc.Font = new Font("Segoe UI", 8.5F);
            _sourceDesc.ForeColor = Color.DimGray;
            _sourceDesc.Location = new Point(284, 10);
            _sourceDesc.Size = new Size(560, 52);
            _sourceDesc.Text = "";
            //
            // _generalSettingsButton
            //
            _generalSettingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _generalSettingsButton.Location = new Point(852, 30);
            _generalSettingsButton.Size = new Size(150, 32);
            _generalSettingsButton.Text = "⚙ General Settings...";
            _generalSettingsButton.Click += GeneralSettings_Click;

            // ================================================= SIMPLE SESSION PANEL
            //
            // _timingHeader
            //
            _timingHeader.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _timingHeader.Location = new Point(8, 8);
            _timingHeader.Size = new Size(300, 24);
            _timingHeader.Text = "Timing";
            //
            // _workLabelCaption
            //
            _workLabelCaption.Location = new Point(8, 40);
            _workLabelCaption.Size = new Size(180, 30);
            _workLabelCaption.Text = "Work interval";
            _workLabelCaption.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _work
            //
            _work.Location = new Point(200, 36);
            _work.Size = new Size(90, 34);
            _work.TextAlign = HorizontalAlignment.Right;
            //
            // _workUnit
            //
            _workUnit.Location = new Point(296, 40);
            _workUnit.Size = new Size(50, 30);
            _workUnit.Text = "sec";
            //
            // _restLabelCaption
            //
            _restLabelCaption.Location = new Point(8, 78);
            _restLabelCaption.Size = new Size(180, 30);
            _restLabelCaption.Text = "Rest interval";
            _restLabelCaption.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _rest
            //
            _rest.Location = new Point(200, 74);
            _rest.Size = new Size(90, 34);
            _rest.TextAlign = HorizontalAlignment.Right;
            //
            // _restUnit
            //
            _restUnit.Location = new Point(296, 78);
            _restUnit.Size = new Size(50, 30);
            _restUnit.Text = "sec";
            //
            // _roundsLabel
            //
            _roundsLabel.Location = new Point(8, 116);
            _roundsLabel.Size = new Size(220, 30);
            _roundsLabel.Text = "Rounds (0 = continuous)";
            _roundsLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _rounds
            //
            _rounds.Location = new Point(240, 112);
            _rounds.Size = new Size(90, 34);
            _rounds.TextAlign = HorizontalAlignment.Right;
            //
            // _workWordingLabel
            //
            _workWordingLabel.Location = new Point(8, 158);
            _workWordingLabel.Size = new Size(140, 30);
            _workWordingLabel.Text = "Work wording";
            _workWordingLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _workLabelText
            //
            _workLabelText.Location = new Point(160, 154);
            _workLabelText.Size = new Size(140, 34);
            //
            // _restWordingLabel
            //
            _restWordingLabel.Location = new Point(320, 158);
            _restWordingLabel.Size = new Size(140, 30);
            _restWordingLabel.Text = "Rest wording";
            _restWordingLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _restLabelText
            //
            _restLabelText.Location = new Point(460, 154);
            _restLabelText.Size = new Size(140, 34);
            //
            // _restAfterFinal
            //
            _restAfterFinal.Location = new Point(8, 198);
            _restAfterFinal.Size = new Size(320, 30);
            _restAfterFinal.Text = "Rest after the final round";
            //
            // _planLabel
            //
            _planLabel.Location = new Point(8, 238);
            _planLabel.Size = new Size(150, 30);
            _planLabel.Text = "Variable plan";
            _planLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _plan
            //
            _plan.Location = new Point(160, 234);
            _plan.Size = new Size(340, 34);
            //
            // _planPreview
            //
            _planPreview.ForeColor = Color.DimGray;
            _planPreview.Location = new Point(8, 272);
            _planPreview.Size = new Size(700, 50);
            _planPreview.Text = "";
            //
            // _moveHeader
            //
            _moveHeader.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _moveHeader.Location = new Point(8, 332);
            _moveHeader.Size = new Size(300, 24);
            _moveHeader.Text = "Move between stations";
            //
            // _useMoveTime
            //
            _useMoveTime.Location = new Point(8, 364);
            _useMoveTime.Size = new Size(340, 30);
            _useMoveTime.Text = "Include a move time between stations";
            //
            // _moveSecondsLabel
            //
            _moveSecondsLabel.Location = new Point(8, 400);
            _moveSecondsLabel.Size = new Size(140, 30);
            _moveSecondsLabel.Text = "Move duration";
            _moveSecondsLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _moveSeconds
            //
            _moveSeconds.Location = new Point(160, 396);
            _moveSeconds.Size = new Size(90, 34);
            _moveSeconds.TextAlign = HorizontalAlignment.Right;
            //
            // _moveSecondsUnit
            //
            _moveSecondsUnit.Location = new Point(256, 400);
            _moveSecondsUnit.Size = new Size(50, 30);
            _moveSecondsUnit.Text = "sec";
            //
            // _moveMessageLabel
            //
            _moveMessageLabel.Location = new Point(8, 440);
            _moveMessageLabel.Size = new Size(300, 24);
            _moveMessageLabel.Text = "Move message";
            //
            // _moveMessage
            //
            _moveMessage.Location = new Point(8, 466);
            _moveMessage.Size = new Size(600, 34);
            //
            // _simplePanel
            //
            _simplePanel.Location = new Point(12, 70);
            _simplePanel.Size = new Size(990, 520);
            _simplePanel.Controls.Add(_timingHeader);
            _simplePanel.Controls.Add(_workLabelCaption);
            _simplePanel.Controls.Add(_work);
            _simplePanel.Controls.Add(_workUnit);
            _simplePanel.Controls.Add(_restLabelCaption);
            _simplePanel.Controls.Add(_rest);
            _simplePanel.Controls.Add(_restUnit);
            _simplePanel.Controls.Add(_roundsLabel);
            _simplePanel.Controls.Add(_rounds);
            _simplePanel.Controls.Add(_workWordingLabel);
            _simplePanel.Controls.Add(_workLabelText);
            _simplePanel.Controls.Add(_restWordingLabel);
            _simplePanel.Controls.Add(_restLabelText);
            _simplePanel.Controls.Add(_restAfterFinal);
            _simplePanel.Controls.Add(_planLabel);
            _simplePanel.Controls.Add(_plan);
            _simplePanel.Controls.Add(_planPreview);
            _simplePanel.Controls.Add(_moveHeader);
            _simplePanel.Controls.Add(_useMoveTime);
            _simplePanel.Controls.Add(_moveSecondsLabel);
            _simplePanel.Controls.Add(_moveSeconds);
            _simplePanel.Controls.Add(_moveSecondsUnit);
            _simplePanel.Controls.Add(_moveMessageLabel);
            _simplePanel.Controls.Add(_moveMessage);

            // ================================================= BUILT PROGRAM PANEL
            //
            // _nameCaption
            //
            _nameCaption.Font = new Font("Segoe UI", 8F);
            _nameCaption.ForeColor = Color.DimGray;
            _nameCaption.Location = new Point(0, 0);
            _nameCaption.Size = new Size(150, 18);
            _nameCaption.Text = "PROGRAM NAME";
            //
            // _programName
            //
            _programName.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            _programName.Location = new Point(0, 20);
            _programName.Size = new Size(280, 36);
            //
            // _modeCaption
            //
            _modeCaption.Font = new Font("Segoe UI", 8F);
            _modeCaption.ForeColor = Color.DimGray;
            _modeCaption.Location = new Point(300, 0);
            _modeCaption.Size = new Size(150, 18);
            _modeCaption.Text = "EXECUTION MODE";
            //
            // _modeShared
            //
            _modeShared.Location = new Point(300, 20);
            _modeShared.Size = new Size(180, 24);
            _modeShared.Text = "Shared Timing";
            //
            // _modeSequential
            //
            _modeSequential.Location = new Point(300, 44);
            _modeSequential.Size = new Size(200, 24);
            _modeSequential.Text = "Sequential Stations";
            //
            // _modeParallel
            //
            _modeParallel.Location = new Point(300, 68);
            _modeParallel.Size = new Size(220, 24);
            _modeParallel.Text = "Parallel Independent";
            //
            // _modeDesc
            //
            _modeDesc.Font = new Font("Segoe UI", 8.5F);
            _modeDesc.ForeColor = Color.DimGray;
            _modeDesc.Location = new Point(540, 0);
            _modeDesc.Size = new Size(450, 96);
            _modeDesc.Text = "";
            //
            // _quickWizardButton
            //
            _quickWizardButton.Location = new Point(0, 100);
            _quickWizardButton.Size = new Size(160, 32);
            _quickWizardButton.Text = "✨ Quick Start Wizard...";
            _quickWizardButton.Click += QuickWizard_Click;
            //
            // _importButton
            //
            _importButton.Location = new Point(166, 100);
            _importButton.Size = new Size(100, 32);
            _importButton.Text = "Import...";
            _importButton.Click += ImportProgram_Click;
            //
            // _exportButton
            //
            _exportButton.Location = new Point(272, 100);
            _exportButton.Size = new Size(100, 32);
            _exportButton.Text = "Export...";
            _exportButton.Click += ExportProgram_Click;
            //
            // _instructions
            //
            _instructions.Font = new Font("Segoe UI", 8.5F, FontStyle.Italic);
            _instructions.ForeColor = Color.DimGray;
            _instructions.Location = new Point(0, 140);
            _instructions.Size = new Size(990, 32);
            _instructions.Text = "1. Pick a station (or the Shared Timeline) on the left.   2. Add blocks in the middle.   3. Edit details on the right.   4. The script below always matches - edit either one.";
            //
            // _outlineLabel
            //
            _outlineLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _outlineLabel.Location = new Point(0, 178);
            _outlineLabel.Size = new Size(260, 20);
            _outlineLabel.Text = "Program Outline";
            //
            // _outline
            //
            _outline.Location = new Point(0, 200);
            _outline.Size = new Size(300, 220);
            _outline.HideSelection = false;
            _outline.AfterSelect += Outline_AfterSelect;
            //
            // _addStationButton
            //
            _addStationButton.Location = new Point(0, 426);
            _addStationButton.Size = new Size(84, 30);
            _addStationButton.Text = "+Station";
            _addStationButton.Click += AddStation_Click;
            //
            // _addBetweenRestButton
            //
            _addBetweenRestButton.Location = new Point(88, 426);
            _addBetweenRestButton.Size = new Size(84, 30);
            _addBetweenRestButton.Text = "+Rest";
            _addBetweenRestButton.Click += AddBetweenRest_Click;
            //
            // _addBetweenMoveButton
            //
            _addBetweenMoveButton.Location = new Point(176, 426);
            _addBetweenMoveButton.Size = new Size(84, 30);
            _addBetweenMoveButton.Text = "+Move";
            _addBetweenMoveButton.Click += AddBetweenMove_Click;
            //
            // _duplicateStationButton
            //
            _duplicateStationButton.Location = new Point(0, 460);
            _duplicateStationButton.Size = new Size(84, 30);
            _duplicateStationButton.Text = "Duplicate";
            _duplicateStationButton.Click += DuplicateStation_Click;
            //
            // _deleteStationButton
            //
            _deleteStationButton.Location = new Point(88, 460);
            _deleteStationButton.Size = new Size(84, 30);
            _deleteStationButton.Text = "Delete";
            _deleteStationButton.Click += DeleteStation_Click;
            //
            // _moveStationUpButton
            //
            _moveStationUpButton.Location = new Point(176, 460);
            _moveStationUpButton.Size = new Size(40, 30);
            _moveStationUpButton.Text = "Up";
            _moveStationUpButton.Click += MoveStationUp_Click;
            //
            // _moveStationDownButton
            //
            _moveStationDownButton.Location = new Point(220, 460);
            _moveStationDownButton.Size = new Size(40, 30);
            _moveStationDownButton.Text = "Dn";
            _moveStationDownButton.Click += MoveStationDown_Click;
            //
            // _timelineLabel
            //
            _timelineLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _timelineLabel.Location = new Point(316, 178);
            _timelineLabel.Size = new Size(300, 20);
            _timelineLabel.Text = "Selected Timeline";
            //
            // _timelineList
            //
            _timelineList.Location = new Point(316, 200);
            _timelineList.Size = new Size(300, 220);
            _timelineList.IntegralHeight = false;
            _timelineList.SelectedIndexChanged += TimelineList_SelectedIndexChanged;
            _timelineList.DoubleClick += TimelineList_DoubleClick;
            //
            // _addWorkButton
            //
            _addWorkButton.Location = new Point(316, 426);
            _addWorkButton.Size = new Size(70, 30);
            _addWorkButton.Text = "+Work";
            _addWorkButton.Click += AddWork_Click;
            //
            // _addRecoveryButton
            //
            _addRecoveryButton.Location = new Point(390, 426);
            _addRecoveryButton.Size = new Size(70, 30);
            _addRecoveryButton.Text = "+Recov";
            _addRecoveryButton.Click += AddRecovery_Click;
            //
            // _addRestButton
            //
            _addRestButton.Location = new Point(464, 426);
            _addRestButton.Size = new Size(70, 30);
            _addRestButton.Text = "+Rest";
            _addRestButton.Click += AddRest_Click;
            //
            // _addMoveButton
            //
            _addMoveButton.Location = new Point(538, 426);
            _addMoveButton.Size = new Size(78, 30);
            _addMoveButton.Text = "+Move";
            _addMoveButton.Click += AddMove_Click;
            //
            // _addRepeatGroupButton
            //
            _addRepeatGroupButton.Location = new Point(316, 460);
            _addRepeatGroupButton.Size = new Size(110, 30);
            _addRepeatGroupButton.Text = "+Repeat Group";
            _addRepeatGroupButton.Click += AddRepeatGroup_Click;
            //
            // _addPatternButton
            //
            _addPatternButton.Location = new Point(430, 460);
            _addPatternButton.Size = new Size(90, 30);
            _addPatternButton.Text = "+Pattern...";
            _addPatternButton.Click += AddPattern_Click;
            //
            // _duplicateBlockButton
            //
            _duplicateBlockButton.Location = new Point(316, 494);
            _duplicateBlockButton.Size = new Size(84, 30);
            _duplicateBlockButton.Text = "Duplicate";
            _duplicateBlockButton.Click += DuplicateBlock_Click;
            //
            // _deleteBlockButton
            //
            _deleteBlockButton.Location = new Point(404, 494);
            _deleteBlockButton.Size = new Size(84, 30);
            _deleteBlockButton.Text = "Delete";
            _deleteBlockButton.Click += DeleteBlock_Click;
            //
            // _moveBlockUpButton
            //
            _moveBlockUpButton.Location = new Point(492, 494);
            _moveBlockUpButton.Size = new Size(56, 30);
            _moveBlockUpButton.Text = "Up";
            _moveBlockUpButton.Click += MoveBlockUp_Click;
            //
            // _moveBlockDownButton
            //
            _moveBlockDownButton.Location = new Point(552, 494);
            _moveBlockDownButton.Size = new Size(64, 30);
            _moveBlockDownButton.Text = "Down";
            _moveBlockDownButton.Click += MoveBlockDown_Click;
            //
            // _propertiesLabel
            //
            _propertiesLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _propertiesLabel.Location = new Point(632, 178);
            _propertiesLabel.Size = new Size(300, 20);
            _propertiesLabel.Text = "Properties";
            //
            // _properties
            //
            _properties.Location = new Point(632, 200);
            _properties.Size = new Size(300, 324);
            _properties.ToolbarVisible = false;
            _properties.PropertyValueChanged += Properties_PropertyValueChanged;
            //
            // _scriptLabel
            //
            _scriptLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _scriptLabel.Location = new Point(0, 534);
            _scriptLabel.Size = new Size(400, 20);
            _scriptLabel.Text = "Script  (always in sync with the panes above)";
            //
            // _script
            //
            _script.Location = new Point(0, 556);
            _script.Size = new Size(932, 130);
            _script.Multiline = true;
            _script.ScrollBars = ScrollBars.Vertical;
            _script.AcceptsReturn = true;
            _script.AcceptsTab = true;
            _script.Font = new Font("Consolas", 9.5F);
            _script.TextChanged += Script_TextChanged;
            //
            // _builtPanel
            //
            _builtPanel.Location = new Point(12, 70);
            _builtPanel.Size = new Size(990, 700);
            _builtPanel.Controls.Add(_nameCaption);
            _builtPanel.Controls.Add(_programName);
            _builtPanel.Controls.Add(_modeCaption);
            _builtPanel.Controls.Add(_modeShared);
            _builtPanel.Controls.Add(_modeSequential);
            _builtPanel.Controls.Add(_modeParallel);
            _builtPanel.Controls.Add(_modeDesc);
            _builtPanel.Controls.Add(_quickWizardButton);
            _builtPanel.Controls.Add(_importButton);
            _builtPanel.Controls.Add(_exportButton);
            _builtPanel.Controls.Add(_instructions);
            _builtPanel.Controls.Add(_outlineLabel);
            _builtPanel.Controls.Add(_outline);
            _builtPanel.Controls.Add(_addStationButton);
            _builtPanel.Controls.Add(_addBetweenRestButton);
            _builtPanel.Controls.Add(_addBetweenMoveButton);
            _builtPanel.Controls.Add(_duplicateStationButton);
            _builtPanel.Controls.Add(_deleteStationButton);
            _builtPanel.Controls.Add(_moveStationUpButton);
            _builtPanel.Controls.Add(_moveStationDownButton);
            _builtPanel.Controls.Add(_timelineLabel);
            _builtPanel.Controls.Add(_timelineList);
            _builtPanel.Controls.Add(_addWorkButton);
            _builtPanel.Controls.Add(_addRecoveryButton);
            _builtPanel.Controls.Add(_addRestButton);
            _builtPanel.Controls.Add(_addMoveButton);
            _builtPanel.Controls.Add(_addRepeatGroupButton);
            _builtPanel.Controls.Add(_addPatternButton);
            _builtPanel.Controls.Add(_duplicateBlockButton);
            _builtPanel.Controls.Add(_deleteBlockButton);
            _builtPanel.Controls.Add(_moveBlockUpButton);
            _builtPanel.Controls.Add(_moveBlockDownButton);
            _builtPanel.Controls.Add(_propertiesLabel);
            _builtPanel.Controls.Add(_properties);
            _builtPanel.Controls.Add(_scriptLabel);
            _builtPanel.Controls.Add(_script);

            // ================================================= FOOTER
            //
            // _pathLabel
            //
            _pathLabel.Font = new Font("Segoe UI", 8F);
            _pathLabel.ForeColor = Color.DimGray;
            _pathLabel.Location = new Point(16, 782);
            _pathLabel.Size = new Size(650, 40);
            _pathLabel.Text = "";
            //
            // _saveStatusLabel
            //
            _saveStatusLabel.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            _saveStatusLabel.ForeColor = Color.FromArgb(15, 138, 70);
            _saveStatusLabel.Location = new Point(16, 822);
            _saveStatusLabel.Size = new Size(650, 22);
            _saveStatusLabel.Text = "";
            //
            // _saveButton
            //
            _saveButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _saveButton.Location = new Point(720, 812);
            _saveButton.Size = new Size(90, 38);
            _saveButton.Text = "Save";
            _saveButton.Click += Save_Click;
            //
            // _okButton
            //
            _okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _okButton.Location = new Point(816, 812);
            _okButton.Size = new Size(90, 38);
            _okButton.Text = "OK";
            _okButton.Click += Ok_Click;
            //
            // _cancelButton
            //
            _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Location = new Point(912, 812);
            _cancelButton.Size = new Size(90, 38);
            _cancelButton.Text = "Cancel";
            //
            // ProgramEditorForm
            //
            AcceptButton = null;
            CancelButton = _cancelButton;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            ClientSize = new Size(1014, 870);
            MinimumSize = new Size(700, 500);
            Controls.Add(_sourceLabel);
            Controls.Add(_sourceSimple);
            Controls.Add(_sourceBuilt);
            Controls.Add(_sourceDesc);
            Controls.Add(_generalSettingsButton);
            Controls.Add(_simplePanel);
            Controls.Add(_builtPanel);
            Controls.Add(_pathLabel);
            Controls.Add(_saveStatusLabel);
            Controls.Add(_saveButton);
            Controls.Add(_okButton);
            Controls.Add(_cancelButton);
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.Sizable;
            KeyPreview = true;
            MaximizeBox = true;
            MinimizeBox = false;
            Name = "ProgramEditorForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "GymClock - Program & Settings";
            // MainForm can be TopMost ("keep window on top") - without this, this
            // window could end up hidden behind it despite being modal, since
            // TopMost wins the OS z-order regardless of ownership.
            TopMost = true;

            _tip.AutoPopDelay = 15000;
            _tip.InitialDelay = 300;
            _tip.ReshowDelay = 200;
            _tip.SetToolTip(_outline, "Pick a station (or the Shared Timeline) to work on. Add, duplicate, delete and reorder stations below.");
            _tip.SetToolTip(_timelineList, "The blocks that make up whatever is selected on the left. Double-click a repeat group to edit its own blocks.");
            _tip.SetToolTip(_properties, "Edit whatever is selected in the outline or the timeline.");
            _tip.SetToolTip(_script, "Type here to edit the program directly - the panes above update to match a moment after you stop typing.");

            _simplePanel.ResumeLayout(false);
            _simplePanel.PerformLayout();
            _builtPanel.ResumeLayout(false);
            _builtPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_work).EndInit();
            ((System.ComponentModel.ISupportInitialize)_rest).EndInit();
            ((System.ComponentModel.ISupportInitialize)_rounds).EndInit();
            ((System.ComponentModel.ISupportInitialize)_moveSeconds).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
