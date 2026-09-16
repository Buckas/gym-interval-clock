using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    public partial class ProgramBuilderForm
    {
        private System.ComponentModel.IContainer components = null;

        private TabControl _tabs;
        private TabPage _builderTab;
        private TabPage _scriptTab;

        // ---- Builder tab: three panes
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

        // ---- Script tab
        private Label _scriptHelp;
        private TextBox _scriptText;
        private Button _applyScriptButton;
        private Label _scriptStatus;

        private Button _okButton;
        private Button _cancelButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _tabs = new TabControl();
            _builderTab = new TabPage();
            _scriptTab = new TabPage();

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

            _scriptHelp = new Label();
            _scriptText = new TextBox();
            _applyScriptButton = new Button();
            _scriptStatus = new Label();

            _okButton = new Button();
            _cancelButton = new Button();

            SuspendLayout();
            //
            // _outlineLabel
            //
            _outlineLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _outlineLabel.Location = new Point(8, 8);
            _outlineLabel.Size = new Size(260, 22);
            _outlineLabel.Text = "Program Outline";
            //
            // _outline
            //
            _outline.Location = new Point(8, 32);
            _outline.Size = new Size(260, 380);
            _outline.HideSelection = false;
            _outline.AfterSelect += Outline_AfterSelect;
            //
            // _addStationButton
            //
            _addStationButton.Location = new Point(8, 418);
            _addStationButton.Size = new Size(84, 30);
            _addStationButton.Text = "+Station";
            _addStationButton.Click += AddStation_Click;
            //
            // _addBetweenRestButton
            //
            _addBetweenRestButton.Location = new Point(96, 418);
            _addBetweenRestButton.Size = new Size(84, 30);
            _addBetweenRestButton.Text = "+Rest";
            _addBetweenRestButton.Click += AddBetweenRest_Click;
            //
            // _addBetweenMoveButton
            //
            _addBetweenMoveButton.Location = new Point(184, 418);
            _addBetweenMoveButton.Size = new Size(84, 30);
            _addBetweenMoveButton.Text = "+Move";
            _addBetweenMoveButton.Click += AddBetweenMove_Click;
            //
            // _duplicateStationButton
            //
            _duplicateStationButton.Location = new Point(8, 452);
            _duplicateStationButton.Size = new Size(84, 30);
            _duplicateStationButton.Text = "Duplicate";
            _duplicateStationButton.Click += DuplicateStation_Click;
            //
            // _deleteStationButton
            //
            _deleteStationButton.Location = new Point(96, 452);
            _deleteStationButton.Size = new Size(84, 30);
            _deleteStationButton.Text = "Delete";
            _deleteStationButton.Click += DeleteStation_Click;
            //
            // _moveStationUpButton
            //
            _moveStationUpButton.Location = new Point(184, 452);
            _moveStationUpButton.Size = new Size(40, 30);
            _moveStationUpButton.Text = "Up";
            _moveStationUpButton.Click += MoveStationUp_Click;
            //
            // _moveStationDownButton
            //
            _moveStationDownButton.Location = new Point(228, 452);
            _moveStationDownButton.Size = new Size(40, 30);
            _moveStationDownButton.Text = "Dn";
            _moveStationDownButton.Click += MoveStationDown_Click;
            //
            // _timelineLabel
            //
            _timelineLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _timelineLabel.Location = new Point(284, 8);
            _timelineLabel.Size = new Size(300, 22);
            _timelineLabel.Text = "Selected Timeline";
            //
            // _timelineList
            //
            _timelineList.Location = new Point(284, 32);
            _timelineList.Size = new Size(300, 380);
            _timelineList.IntegralHeight = false;
            _timelineList.SelectedIndexChanged += TimelineList_SelectedIndexChanged;
            _timelineList.DoubleClick += TimelineList_DoubleClick;
            //
            // _addWorkButton
            //
            _addWorkButton.Location = new Point(284, 418);
            _addWorkButton.Size = new Size(70, 30);
            _addWorkButton.Text = "+Work";
            _addWorkButton.Click += AddWork_Click;
            //
            // _addRecoveryButton
            //
            _addRecoveryButton.Location = new Point(358, 418);
            _addRecoveryButton.Size = new Size(70, 30);
            _addRecoveryButton.Text = "+Recov";
            _addRecoveryButton.Click += AddRecovery_Click;
            //
            // _addRestButton
            //
            _addRestButton.Location = new Point(432, 418);
            _addRestButton.Size = new Size(70, 30);
            _addRestButton.Text = "+Rest";
            _addRestButton.Click += AddRest_Click;
            //
            // _addMoveButton
            //
            _addMoveButton.Location = new Point(506, 418);
            _addMoveButton.Size = new Size(78, 30);
            _addMoveButton.Text = "+Move";
            _addMoveButton.Click += AddMove_Click;
            //
            // _addRepeatGroupButton
            //
            _addRepeatGroupButton.Location = new Point(284, 452);
            _addRepeatGroupButton.Size = new Size(110, 30);
            _addRepeatGroupButton.Text = "+Repeat Group";
            _addRepeatGroupButton.Click += AddRepeatGroup_Click;
            //
            // _addPatternButton
            //
            _addPatternButton.Location = new Point(398, 452);
            _addPatternButton.Size = new Size(90, 30);
            _addPatternButton.Text = "+Pattern...";
            _addPatternButton.Click += AddPattern_Click;
            //
            // _duplicateBlockButton
            //
            _duplicateBlockButton.Location = new Point(284, 486);
            _duplicateBlockButton.Size = new Size(84, 30);
            _duplicateBlockButton.Text = "Duplicate";
            _duplicateBlockButton.Click += DuplicateBlock_Click;
            //
            // _deleteBlockButton
            //
            _deleteBlockButton.Location = new Point(372, 486);
            _deleteBlockButton.Size = new Size(84, 30);
            _deleteBlockButton.Text = "Delete";
            _deleteBlockButton.Click += DeleteBlock_Click;
            //
            // _moveBlockUpButton
            //
            _moveBlockUpButton.Location = new Point(460, 486);
            _moveBlockUpButton.Size = new Size(56, 30);
            _moveBlockUpButton.Text = "Up";
            _moveBlockUpButton.Click += MoveBlockUp_Click;
            //
            // _moveBlockDownButton
            //
            _moveBlockDownButton.Location = new Point(520, 486);
            _moveBlockDownButton.Size = new Size(64, 30);
            _moveBlockDownButton.Text = "Down";
            _moveBlockDownButton.Click += MoveBlockDown_Click;
            //
            // _propertiesLabel
            //
            _propertiesLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _propertiesLabel.Location = new Point(600, 8);
            _propertiesLabel.Size = new Size(260, 22);
            _propertiesLabel.Text = "Properties";
            //
            // _properties
            //
            _properties.Location = new Point(600, 32);
            _properties.Size = new Size(268, 484);
            _properties.ToolbarVisible = false;
            _properties.PropertyValueChanged += Properties_PropertyValueChanged;
            //
            // _builderTab
            //
            _builderTab.Name = "_builderTab";
            _builderTab.Text = "Builder";
            _builderTab.UseVisualStyleBackColor = true;
            _builderTab.Controls.Add(_outlineLabel);
            _builderTab.Controls.Add(_outline);
            _builderTab.Controls.Add(_addStationButton);
            _builderTab.Controls.Add(_addBetweenRestButton);
            _builderTab.Controls.Add(_addBetweenMoveButton);
            _builderTab.Controls.Add(_duplicateStationButton);
            _builderTab.Controls.Add(_deleteStationButton);
            _builderTab.Controls.Add(_moveStationUpButton);
            _builderTab.Controls.Add(_moveStationDownButton);
            _builderTab.Controls.Add(_timelineLabel);
            _builderTab.Controls.Add(_timelineList);
            _builderTab.Controls.Add(_addWorkButton);
            _builderTab.Controls.Add(_addRecoveryButton);
            _builderTab.Controls.Add(_addRestButton);
            _builderTab.Controls.Add(_addMoveButton);
            _builderTab.Controls.Add(_addRepeatGroupButton);
            _builderTab.Controls.Add(_addPatternButton);
            _builderTab.Controls.Add(_duplicateBlockButton);
            _builderTab.Controls.Add(_deleteBlockButton);
            _builderTab.Controls.Add(_moveBlockUpButton);
            _builderTab.Controls.Add(_moveBlockDownButton);
            _builderTab.Controls.Add(_propertiesLabel);
            _builderTab.Controls.Add(_properties);
            //
            // _scriptHelp
            //
            _scriptHelp.Location = new Point(8, 8);
            _scriptHelp.Size = new Size(860, 40);
            _scriptHelp.Text = "Advanced: edit the program directly. Commands: WORK RECOVERY REST MOVE PREPARE COUNTDOWN " +
                "WATER INSTRUCTION CUSTOM STATION REPEAT BETWEEN END. Durations: 20, 20s, 1m, 1m 30s. " +
                "Click Apply to load changes into the Builder tab.";
            //
            // _scriptText
            //
            _scriptText.Location = new Point(8, 52);
            _scriptText.Size = new Size(860, 460);
            _scriptText.Multiline = true;
            _scriptText.ScrollBars = ScrollBars.Vertical;
            _scriptText.AcceptsReturn = true;
            _scriptText.AcceptsTab = true;
            _scriptText.Font = new Font("Consolas", 10F);
            //
            // _applyScriptButton
            //
            _applyScriptButton.Location = new Point(8, 520);
            _applyScriptButton.Size = new Size(120, 34);
            _applyScriptButton.Text = "Apply Script";
            _applyScriptButton.Click += ApplyScript_Click;
            //
            // _scriptStatus
            //
            _scriptStatus.ForeColor = Color.DimGray;
            _scriptStatus.Location = new Point(140, 524);
            _scriptStatus.Size = new Size(600, 26);
            _scriptStatus.Text = "";
            //
            // _scriptTab
            //
            _scriptTab.Name = "_scriptTab";
            _scriptTab.Text = "Script";
            _scriptTab.UseVisualStyleBackColor = true;
            _scriptTab.Controls.Add(_scriptHelp);
            _scriptTab.Controls.Add(_scriptText);
            _scriptTab.Controls.Add(_applyScriptButton);
            _scriptTab.Controls.Add(_scriptStatus);
            //
            // _tabs
            //
            _tabs.Location = new Point(8, 8);
            _tabs.Size = new Size(880, 560);
            _tabs.Controls.Add(_builderTab);
            _tabs.Controls.Add(_scriptTab);
            _tabs.SelectedIndexChanged += Tabs_SelectedIndexChanged;
            //
            // _okButton
            //
            _okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _okButton.DialogResult = DialogResult.OK;
            _okButton.Location = new Point(704, 578);
            _okButton.Size = new Size(90, 38);
            _okButton.Text = "OK";
            _okButton.Click += Ok_Click;
            //
            // _cancelButton
            //
            _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Location = new Point(800, 578);
            _cancelButton.Size = new Size(90, 38);
            _cancelButton.Text = "Cancel";
            //
            // ProgramBuilderForm
            //
            AcceptButton = null;
            CancelButton = _cancelButton;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(900, 630);
            Controls.Add(_tabs);
            Controls.Add(_okButton);
            Controls.Add(_cancelButton);
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ProgramBuilderForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Program Builder";
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
