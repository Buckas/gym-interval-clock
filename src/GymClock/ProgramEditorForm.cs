using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// The single combined window for everything that used to be Settings, Quick
    /// Setup and the Program Builder. "What's running" picks one of two mutually
    /// exclusive sources - a Simple session (plain work/rest/rounds, optionally a
    /// variable plan and a move time) or a Built program (stations, repeat
    /// groups, any block anywhere) - and General Settings (prep, sound, clock,
    /// station-panel display) lives in its own separate window, since it applies
    /// regardless of which source is running.
    ///
    /// Switching the source never destroys the other one's data - see
    /// TimerSettings.UseBuiltProgram - so flicking back and forth to compare is
    /// always safe.
    /// </summary>
    public partial class ProgramEditorForm : Form
    {
        private readonly TimerSettings _original;
        public TimerSettings Result { get; private set; }

        // General Settings' fields live only in that separate window's dialog,
        // so they're tracked here exactly like _workingProgramScript below -
        // otherwise Ok_Click's "rebuild Result from _original" would silently
        // discard whatever was set there.
        private int _gPrepSeconds;
        private double _gVolume;
        private string _gClockFormat;
        private bool _gSoundEnabled;
        private bool _gShowClock;
        private bool _gAlwaysOnTop;
        private bool _gShowStationsTable;
        private bool _gShowStationNameAsDescription;

        private WorkoutProgram _program;
        private RepeatGroup _groupContext;      // non-null while drilled into a repeat group's own blocks
        private object _lastOutlineTag;
        private readonly Timer _scriptSyncTimer = new Timer { Interval = 600 };

        /// <summary>
        /// Parameterless constructor required by the Windows Forms designer at design time
        /// (so this can be opened and tweaked visually in Visual Studio). Not intended for
        /// runtime use - MainForm always calls the other constructor.
        /// </summary>
        public ProgramEditorForm()
            : this(new TimerSettings())
        {
        }

        public ProgramEditorForm(TimerSettings settings)
        {
            _original = settings;
            Result = settings.Clone();

            InitializeComponent();
            _scriptSyncTimer.Tick += ScriptSyncTimer_Tick;

            _pathLabel.Text = "Settings file (editable in Notepad):" + Environment.NewLine + TimerSettings.FilePath;

            // ---- Simple session bindings
            _work.Minimum = 1; _work.Maximum = 3600; _work.Value = Clamp(settings.WorkSeconds, _work);
            _rest.Minimum = 0; _rest.Maximum = 3600; _rest.Value = Clamp(settings.RestSeconds, _rest);
            _rounds.Minimum = 0; _rounds.Maximum = 999; _rounds.Value = Clamp(settings.Rounds, _rounds);
            _workLabelText.Text = settings.WorkLabel;
            _restLabelText.Text = settings.RestLabel;
            _restAfterFinal.Checked = settings.RestAfterFinalRound;
            _plan.Text = settings.Plan ?? string.Empty;
            _useMoveTime.Checked = settings.UseMoveTime;
            _moveSeconds.Minimum = 1; _moveSeconds.Maximum = 600; _moveSeconds.Value = Clamp(settings.MoveSeconds, _moveSeconds);
            _moveMessage.Text = settings.MoveMessage;

            EventHandler refreshPlanPreview = delegate { UpdatePlanPreview(); };
            _plan.TextChanged += refreshPlanPreview;
            _work.ValueChanged += refreshPlanPreview;
            _rest.ValueChanged += refreshPlanPreview;
            _rounds.ValueChanged += refreshPlanPreview;
            _restAfterFinal.CheckedChanged += refreshPlanPreview;
            UpdatePlanPreview();

            // ---- General settings, tracked here until their own window is opened
            _gPrepSeconds = settings.PrepSeconds;
            _gVolume = settings.Volume;
            _gClockFormat = settings.ClockFormat;
            _gSoundEnabled = settings.SoundEnabled;
            _gShowClock = settings.ShowClock;
            _gAlwaysOnTop = settings.AlwaysOnTop;
            _gShowStationsTable = settings.ShowStationsTable;
            _gShowStationNameAsDescription = settings.ShowStationNameAsDescription;

            // ---- Program (Built view)
            _program = !string.IsNullOrEmpty(settings.ProgramScript)
                ? ProgramScriptFormat.Parse(settings.ProgramScript)
                : new WorkoutProgram();

            _programName.Text = _program.Name;
            _programName.TextChanged += ProgramName_TextChanged;

            _modeShared.Checked = _program.Mode == ExecutionMode.Shared;
            _modeSequential.Checked = _program.Mode == ExecutionMode.Sequential;
            _modeParallel.Checked = _program.Mode == ExecutionMode.Parallel;
            EventHandler modeChanged = ModeRadio_CheckedChanged;
            _modeShared.CheckedChanged += modeChanged;
            _modeSequential.CheckedChanged += modeChanged;
            _modeParallel.CheckedChanged += modeChanged;
            UpdateModeDescription();

            RefreshOutline();

            // ---- Source switch
            _sourceSimple.Checked = !settings.UseBuiltProgram;
            _sourceBuilt.Checked = settings.UseBuiltProgram;
            EventHandler sourceChanged = delegate { UpdateSourceView(); };
            _sourceSimple.CheckedChanged += sourceChanged;
            _sourceBuilt.CheckedChanged += sourceChanged;
            UpdateSourceView();
        }

        private static decimal Clamp(int value, NumericUpDown target)
        {
            decimal v = value;
            if (v < target.Minimum) return target.Minimum;
            if (v > target.Maximum) return target.Maximum;
            return v;
        }

        // ============================================================ source switch

        private void UpdateSourceView()
        {
            bool simple = _sourceSimple.Checked;
            _simplePanel.Visible = simple;
            _builtPanel.Visible = !simple;

            _sourceDesc.Text = simple
                ? "Just work/rest/rounds (or a variable plan) - no stations to build, no execution mode. "
                    + "This is what's currently active."
                : "A full program you've built - stations with their own timing, repeat groups, any block "
                    + "anywhere. This is what's currently active.";
        }

        // ============================================================ simple session

        private void UpdatePlanPreview()
        {
            string text = _plan.Text.Trim();

            if (text.Length == 0)
            {
                IntervalPlan uniform = IntervalPlan.Uniform((int)_work.Value, (int)_rest.Value, Math.Max(1, (int)_rounds.Value));
                _planPreview.Text = "Even session: " + uniform.Summary() + "   ->   "
                    + uniform.RoundCount + " rounds, "
                    + IntervalPlan.FormatDuration(uniform.TotalSeconds(_gPrepSeconds, _restAfterFinal.Checked))
                    + ((int)_rounds.Value == 0 ? ", repeating continuously" : string.Empty);
                return;
            }

            IntervalPlan plan = IntervalPlan.Parse(text);
            if (plan.IsEmpty)
            {
                _planPreview.Text = "Not understood. Use blocks like  45/15x2 + 50/10x4";
                return;
            }

            _planPreview.Text = plan.Summary() + "   ->   "
                + plan.RoundCount + " rounds, "
                + IntervalPlan.FormatDuration(plan.TotalSeconds(_gPrepSeconds, _restAfterFinal.Checked))
                + "   (overrides work, rest and rounds above)";
        }

        // ============================================================ general settings

        private void GeneralSettings_Click(object sender, EventArgs e)
        {
            TimerSettings current = Result.Clone();
            current.PrepSeconds = _gPrepSeconds;
            current.Volume = _gVolume;
            current.ClockFormat = _gClockFormat;
            current.SoundEnabled = _gSoundEnabled;
            current.ShowClock = _gShowClock;
            current.AlwaysOnTop = _gAlwaysOnTop;
            current.ShowStationsTable = _gShowStationsTable;
            current.ShowStationNameAsDescription = _gShowStationNameAsDescription;

            using (GeneralSettingsForm dialog = new GeneralSettingsForm(current))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _gPrepSeconds = dialog.PrepSeconds;
                    _gVolume = dialog.Volume;
                    _gClockFormat = dialog.ClockFormat;
                    _gSoundEnabled = dialog.SoundEnabled;
                    _gShowClock = dialog.ShowClock;
                    _gAlwaysOnTop = dialog.AlwaysOnTop;
                    _gShowStationsTable = dialog.ShowStationsTable;
                    _gShowStationNameAsDescription = dialog.ShowStationNameAsDescription;
                    UpdatePlanPreview();   // prep seconds feeds the preview's total duration
                }
            }
        }

        // ============================================================ built program header

        private void ProgramName_TextChanged(object sender, EventArgs e)
        {
            _program.Name = string.IsNullOrEmpty(_programName.Text.Trim()) ? "Workout" : _programName.Text.Trim();
            RefreshOutlineLabelsOnly();
            RefreshScriptFromProgram();
        }

        private void ModeRadio_CheckedChanged(object sender, EventArgs e)
        {
            _program.Mode = _modeSequential.Checked ? ExecutionMode.Sequential
                : _modeParallel.Checked ? ExecutionMode.Parallel
                : ExecutionMode.Shared;
            UpdateModeDescription();
            RefreshOutline();
        }

        private void UpdateModeDescription()
        {
            if (_modeParallel.Checked)
            {
                _modeDesc.Text = "Every station runs its OWN pattern at the same time, independently - "
                    + "e.g. one does a 4-minute Tabata while another does a 10-minute Pyramid, "
                    + "both starting together but finishing separately.";
            }
            else if (_modeSequential.Checked)
            {
                _modeDesc.Text = "Stations run one after another - only one is active at a time, each with "
                    + "its own pattern. The panel highlights whichever station is currently active.";
            }
            else
            {
                _modeDesc.Text = "Every station follows the exact SAME countdown together - the panel is "
                    + "informational only, since every station is in use at once.";
            }
        }

        /// <summary>Lets MainForm's "B" shortcut jump straight to the wizard once this window is up, rather than duplicating its logic.</summary>
        public void LaunchQuickWizard()
        {
            QuickWizard_Click(this, EventArgs.Empty);
        }

        private void QuickWizard_Click(object sender, EventArgs e)
        {
            using (QuickSetupForm dialog = new QuickSetupForm(Result))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
                {
                    ApplyProgram(dialog.Result);
                    _sourceBuilt.Checked = true;   // a generated program should obviously become the active one
                }
            }
        }

        private void ExportProgram_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Program file (*.program.txt)|*.program.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = SafeFileName(_program.Name) + ".program.txt";
                dialog.Title = "Export program";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.WriteAllText(dialog.FileName, ProgramScriptFormat.ToScript(_program));
                    _saveStatusLabel.Text = "Exported program to " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save that file:" + Environment.NewLine + ex.Message,
                        "Export program", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportProgram_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Program file (*.program.txt;*.txt)|*.program.txt;*.txt|All files (*.*)|*.*";
                dialog.Title = "Import program";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string text;
                try
                {
                    text = File.ReadAllText(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not read that file:" + Environment.NewLine + ex.Message,
                        "Import program", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                WorkoutProgram program = ProgramScriptFormat.Parse(text);
                if (program.SharedTimeline.IsEmpty && program.Stations.Count == 0)
                {
                    MessageBox.Show(this, "No program was found in that file.", "Import program",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ApplyProgram(program);
                _sourceBuilt.Checked = true;
                _saveStatusLabel.Text = "Imported program from " + dialog.FileName;
            }
        }

        private static string SafeFileName(string name)
        {
            string text = string.IsNullOrEmpty(name) ? "workout" : name;
            foreach (char c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '-');
            return text;
        }

        /// <summary>Swaps in a whole new program - from the wizard, an import, or a script edit - and refreshes everything to match.</summary>
        private void ApplyProgram(WorkoutProgram program)
        {
            _program = program;
            _groupContext = null;
            _lastOutlineTag = null;

            _programName.Text = _program.Name;
            _modeShared.Checked = _program.Mode == ExecutionMode.Shared;
            _modeSequential.Checked = _program.Mode == ExecutionMode.Sequential;
            _modeParallel.Checked = _program.Mode == ExecutionMode.Parallel;
            UpdateModeDescription();

            RefreshOutline();
        }

        // ============================================================ outline

        private void RefreshOutline()
        {
            object previousTag = _outline.SelectedNode != null ? _outline.SelectedNode.Tag : "PROGRAM";

            _outline.BeginUpdate();
            _outline.Nodes.Clear();

            TreeNode root = new TreeNode("Program: " + _program.Name) { Tag = "PROGRAM" };
            _outline.Nodes.Add(root);

            if (_program.Mode == ExecutionMode.Shared)
            {
                root.Nodes.Add(new TreeNode("Shared Timeline") { Tag = "SHARED" });
            }
            else
            {
                for (int i = 0; i < _program.Stations.Count; i++)
                {
                    root.Nodes.Add(new TreeNode((i + 1) + ". " + _program.Stations[i].Name) { Tag = i });
                }

                if (_program.Mode == ExecutionMode.Sequential && _program.Stations.Count >= 2)
                {
                    root.Nodes.Add(new TreeNode("Between Stations") { Tag = "BETWEEN" });
                }
            }

            root.ExpandAll();
            _outline.EndUpdate();

            _outline.SelectedNode = FindNodeByTag(root, previousTag) ?? root;
            RefreshScriptFromProgram();
        }

        private void RefreshOutlineLabelsOnly()
        {
            if (_outline.Nodes.Count == 0) return;
            TreeNode root = _outline.Nodes[0];
            root.Text = "Program: " + _program.Name;

            foreach (TreeNode node in root.Nodes)
            {
                if (!(node.Tag is int)) continue;
                int i = (int)node.Tag;
                if (i >= 0 && i < _program.Stations.Count) node.Text = (i + 1) + ". " + _program.Stations[i].Name;
            }
        }

        private static TreeNode FindNodeByTag(TreeNode node, object tag)
        {
            if (Equals(node.Tag, tag)) return node;
            foreach (TreeNode child in node.Nodes)
            {
                TreeNode found = FindNodeByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }

        private void Outline_AfterSelect(object sender, TreeViewEventArgs e)
        {
            object tag = _outline.SelectedNode != null ? _outline.SelectedNode.Tag : null;
            if (!Equals(tag, _lastOutlineTag)) _groupContext = null;
            _lastOutlineTag = tag;

            RefreshPropertiesForOutline();
            RefreshTimelineList();
        }

        private int? SelectedStationIndex()
        {
            object tag = _outline.SelectedNode != null ? _outline.SelectedNode.Tag : null;
            return tag is int ? (int?)tag : null;
        }

        private Timeline CurrentTimeline()
        {
            object tag = _outline.SelectedNode != null ? _outline.SelectedNode.Tag : null;
            if (tag is int) return _program.Stations[(int)tag].CustomTimeline;
            if ("SHARED".Equals(tag)) return _program.SharedTimeline;
            if ("BETWEEN".Equals(tag)) return _program.BetweenStationsTimeline;
            return null;
        }

        private void RefreshPropertiesForOutline()
        {
            object tag = _outline.SelectedNode != null ? _outline.SelectedNode.Tag : null;

            if (tag is int) _properties.SelectedObject = _program.Stations[(int)tag];
            else if ("PROGRAM".Equals(tag)) _properties.SelectedObject = new ProgramInfo(this);
            else _properties.SelectedObject = null;
        }

        // ---- station actions

        private void AddStation_Click(object sender, EventArgs e)
        {
            if (_program.Mode != ExecutionMode.Sequential && _program.Mode != ExecutionMode.Parallel)
            {
                if (MessageBox.Show(this,
                    "Adding a station switches this program to Sequential Stations mode. Continue?",
                    "Add Station", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _program.Mode = ExecutionMode.Sequential;
                _modeSequential.Checked = true;
            }

            StationDef station = new StationDef { Name = "Station " + (_program.Stations.Count + 1), Source = TimingSource.Custom };
            station.CustomTimeline.Add(new Block(BlockType.Work, 30));
            _program.Stations.Add(station);

            RefreshOutline();
            SelectStationNode(_program.Stations.Count - 1);
        }

        private void DuplicateStation_Click(object sender, EventArgs e)
        {
            int? index = SelectedStationIndex();
            if (index == null) return;

            StationDef copy = _program.Stations[index.Value].Clone();
            copy.Name += " (copy)";
            _program.Stations.Insert(index.Value + 1, copy);

            RefreshOutline();
            SelectStationNode(index.Value + 1);
        }

        private void DeleteStation_Click(object sender, EventArgs e)
        {
            int? index = SelectedStationIndex();
            if (index == null) return;
            if (MessageBox.Show(this, "Delete this station?", "Delete Station",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _program.Stations.RemoveAt(index.Value);
            RefreshOutline();
        }

        private void MoveStationUp_Click(object sender, EventArgs e) { MoveStation(-1); }
        private void MoveStationDown_Click(object sender, EventArgs e) { MoveStation(1); }

        private void MoveStation(int direction)
        {
            int? index = SelectedStationIndex();
            if (index == null) return;

            int to = index.Value + direction;
            if (to < 0 || to >= _program.Stations.Count) return;

            StationDef station = _program.Stations[index.Value];
            _program.Stations.RemoveAt(index.Value);
            _program.Stations.Insert(to, station);

            RefreshOutline();
            SelectStationNode(to);
        }

        private void SelectStationNode(int index)
        {
            if (_outline.Nodes.Count == 0) return;
            TreeNode node = FindNodeByTag(_outline.Nodes[0], index);
            if (node != null) _outline.SelectedNode = node;
        }

        private void AddBetweenRest_Click(object sender, EventArgs e) { AddBetween(BlockType.Rest, 60); }
        private void AddBetweenMove_Click(object sender, EventArgs e) { AddBetween(BlockType.Move, 8); }

        private void AddBetween(BlockType type, int seconds)
        {
            if (_program.Mode != ExecutionMode.Sequential || _program.Stations.Count < 2)
            {
                MessageBox.Show(this, "Add at least two stations first.", "Between Stations",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _program.BetweenStationsTimeline.Add(new Block(type, seconds));
            RefreshOutline();

            TreeNode node = FindNodeByTag(_outline.Nodes[0], "BETWEEN");
            if (node != null) _outline.SelectedNode = node;
        }

        // ============================================================ timeline

        private void RefreshTimelineList()
        {
            _timelineList.Items.Clear();

            if (_groupContext != null)
            {
                _timelineList.Items.Add("< Back");
                foreach (Block block in _groupContext.Blocks) _timelineList.Items.Add(BlockRow(block));
            }
            else
            {
                Timeline timeline = CurrentTimeline();
                if (timeline != null)
                {
                    foreach (TimelineItem item in timeline.Items)
                    {
                        _timelineList.Items.Add(item.Block != null ? BlockRow(item.Block) : GroupRow(item.Group));
                    }
                }
            }

            RefreshButtonsEnabled();
            RefreshScriptFromProgram();
        }

        private static string BlockRow(Block block)
        {
            string text = block.Word + " " + Block.FormatDuration(block.Seconds);
            if (!string.IsNullOrEmpty(block.Label)) text += "  \"" + block.Label + "\"";
            return text;
        }

        private static string GroupRow(RepeatGroup group)
        {
            return "REPEAT x" + group.Count + "   [" + group.Summary() + "]";
        }

        private void TimelineList_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = _timelineList.SelectedIndex;

            if (_groupContext != null)
            {
                int blockIndex = index - 1;
                _properties.SelectedObject = (blockIndex >= 0 && blockIndex < _groupContext.Blocks.Count)
                    ? _groupContext.Blocks[blockIndex] : null;
            }
            else
            {
                Timeline timeline = CurrentTimeline();
                if (timeline != null && index >= 0 && index < timeline.Items.Count)
                {
                    TimelineItem item = timeline.Items[index];
                    _properties.SelectedObject = item.Block != null ? (object)item.Block : item.Group;
                }
            }

            RefreshButtonsEnabled();
        }

        private void TimelineList_DoubleClick(object sender, EventArgs e)
        {
            if (_groupContext != null)
            {
                if (_timelineList.SelectedIndex == 0) { _groupContext = null; RefreshTimelineList(); }
                return;
            }

            Timeline timeline = CurrentTimeline();
            int index = _timelineList.SelectedIndex;
            if (timeline == null || index < 0 || index >= timeline.Items.Count) return;

            TimelineItem item = timeline.Items[index];
            if (item.Group != null) { _groupContext = item.Group; RefreshTimelineList(); }
        }

        // ---- block actions

        private void AddWork_Click(object sender, EventArgs e) { AddBlock(BlockType.Work, 30); }
        private void AddRecovery_Click(object sender, EventArgs e) { AddBlock(BlockType.Recovery, 15); }
        private void AddRest_Click(object sender, EventArgs e) { AddBlock(BlockType.Rest, 60); }
        private void AddMove_Click(object sender, EventArgs e) { AddBlock(BlockType.Move, 8); }

        private void AddBlock(BlockType type, int defaultSeconds)
        {
            if (_groupContext != null)
            {
                _groupContext.Blocks.Add(new Block(type, defaultSeconds));
            }
            else
            {
                Timeline timeline = CurrentTimeline();
                if (timeline == null) return;
                timeline.Add(new Block(type, defaultSeconds));
            }

            RefreshTimelineList();
            _timelineList.SelectedIndex = _timelineList.Items.Count - 1;
        }

        private void AddRepeatGroup_Click(object sender, EventArgs e)
        {
            if (_groupContext != null) return;
            Timeline timeline = CurrentTimeline();
            if (timeline == null) return;

            RepeatGroup group = new RepeatGroup { Count = 3 };
            group.Blocks.Add(new Block(BlockType.Work, 30));
            group.Blocks.Add(new Block(BlockType.Recovery, 15));
            timeline.Add(group);

            RefreshTimelineList();
            _timelineList.SelectedIndex = _timelineList.Items.Count - 1;
        }

        private void AddPattern_Click(object sender, EventArgs e)
        {
            if (_groupContext != null) return;
            Timeline timeline = CurrentTimeline();
            if (timeline == null)
            {
                MessageBox.Show(this, "Select a station or the shared timeline first.", "Add Pattern",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (PatternPickerForm picker = new PatternPickerForm())
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;

                Timeline generated = PatternGenerator.Generate(picker.SelectedPattern, picker.Settings);
                foreach (TimelineItem item in generated.Items) timeline.Items.Add(item);
                timeline.Invalidate();
            }

            RefreshTimelineList();
        }

        private void DuplicateBlock_Click(object sender, EventArgs e)
        {
            int index = _timelineList.SelectedIndex;
            if (index < 0) return;

            if (_groupContext != null)
            {
                int blockIndex = index - 1;
                if (blockIndex < 0 || blockIndex >= _groupContext.Blocks.Count) return;
                _groupContext.Blocks.Insert(blockIndex + 1, _groupContext.Blocks[blockIndex].Clone());
                RefreshTimelineList();
                _timelineList.SelectedIndex = index + 1;
                return;
            }

            Timeline timeline = CurrentTimeline();
            if (timeline == null || index >= timeline.Items.Count) return;
            timeline.Items.Insert(index + 1, timeline.Items[index].Clone());
            timeline.Invalidate();
            RefreshTimelineList();
            _timelineList.SelectedIndex = index + 1;
        }

        private void DeleteBlock_Click(object sender, EventArgs e)
        {
            int index = _timelineList.SelectedIndex;
            if (index < 0) return;

            if (_groupContext != null)
            {
                int blockIndex = index - 1;
                if (blockIndex < 0 || blockIndex >= _groupContext.Blocks.Count) return;
                if (_groupContext.Blocks.Count <= 1)
                {
                    MessageBox.Show(this, "A repeat group needs at least one block.", "Delete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _groupContext.Blocks.RemoveAt(blockIndex);
                RefreshTimelineList();
                return;
            }

            Timeline timeline = CurrentTimeline();
            if (timeline == null || index >= timeline.Items.Count) return;
            timeline.Items.RemoveAt(index);
            timeline.Invalidate();
            RefreshTimelineList();
        }

        private void MoveBlockUp_Click(object sender, EventArgs e) { MoveBlock(-1); }
        private void MoveBlockDown_Click(object sender, EventArgs e) { MoveBlock(1); }

        private void MoveBlock(int direction)
        {
            int index = _timelineList.SelectedIndex;
            if (index < 0) return;

            if (_groupContext != null)
            {
                int blockIndex = index - 1;
                int target = blockIndex + direction;
                if (blockIndex < 0 || target < 0 || target >= _groupContext.Blocks.Count) return;

                Block block = _groupContext.Blocks[blockIndex];
                _groupContext.Blocks.RemoveAt(blockIndex);
                _groupContext.Blocks.Insert(target, block);
                RefreshTimelineList();
                _timelineList.SelectedIndex = target + 1;
                return;
            }

            Timeline timeline = CurrentTimeline();
            if (timeline == null) return;

            int to = index + direction;
            if (to < 0 || to >= timeline.Items.Count) return;

            TimelineItem item = timeline.Items[index];
            timeline.Items.RemoveAt(index);
            timeline.Items.Insert(to, item);
            timeline.Invalidate();
            RefreshTimelineList();
            _timelineList.SelectedIndex = to;
        }

        private void RefreshButtonsEnabled()
        {
            bool sequential = _program.Mode == ExecutionMode.Sequential;
            _addBetweenRestButton.Enabled = sequential;
            _addBetweenMoveButton.Enabled = sequential;

            int? stationIndex = SelectedStationIndex();
            bool stationSelected = stationIndex != null;
            _duplicateStationButton.Enabled = stationSelected;
            _deleteStationButton.Enabled = stationSelected;
            _moveStationUpButton.Enabled = stationSelected && stationIndex.Value > 0;
            _moveStationDownButton.Enabled = stationSelected && stationIndex.Value < _program.Stations.Count - 1;

            bool hasTimeline = CurrentTimeline() != null;
            bool canAddBlocks = hasTimeline || _groupContext != null;
            _addWorkButton.Enabled = canAddBlocks;
            _addRecoveryButton.Enabled = canAddBlocks;
            _addRestButton.Enabled = canAddBlocks;
            _addMoveButton.Enabled = canAddBlocks;
            _addRepeatGroupButton.Enabled = hasTimeline && _groupContext == null;
            _addPatternButton.Enabled = hasTimeline && _groupContext == null;

            bool rowSelected = _timelineList.SelectedIndex >= 0
                && !(_groupContext != null && _timelineList.SelectedIndex == 0);
            _duplicateBlockButton.Enabled = rowSelected;
            _deleteBlockButton.Enabled = rowSelected;
            _moveBlockUpButton.Enabled = rowSelected;
            _moveBlockDownButton.Enabled = rowSelected;
        }

        // ============================================================ properties

        private void Properties_PropertyValueChanged(object sender, PropertyValueChangedEventArgs e)
        {
            if (_properties.SelectedObject is ProgramInfo)
            {
                _programName.Text = _program.Name;
                _modeShared.Checked = _program.Mode == ExecutionMode.Shared;
                _modeSequential.Checked = _program.Mode == ExecutionMode.Sequential;
                _modeParallel.Checked = _program.Mode == ExecutionMode.Parallel;
                UpdateModeDescription();
                RefreshOutline();
            }
            else
            {
                RefreshOutlineLabelsOnly();
            }

            RefreshTimelineList();
        }

        /// <summary>PropertyGrid wrapper for the Program root - lets the same Name/Mode also be edited from the Properties pane.</summary>
        private class ProgramInfo
        {
            private readonly ProgramEditorForm _owner;
            public ProgramInfo(ProgramEditorForm owner) { _owner = owner; }

            [System.ComponentModel.Description("What appears on the TV display and in exported program files.")]
            public string Name
            {
                get { return _owner._program.Name; }
                set { _owner._program.Name = string.IsNullOrEmpty(value) ? "Workout" : value; }
            }

            [System.ComponentModel.Description(
                "Shared: every station follows the exact same countdown together. " +
                "Sequential: stations run one after another, one active at a time, each with its own pattern. " +
                "Parallel: every station runs its own pattern at once, independently, finishing at different times.")]
            public ExecutionMode Mode
            {
                get { return _owner._program.Mode; }
                set { _owner._program.Mode = value; }
            }
        }

        // ============================================================ script (live sync)

        /// <summary>Regenerates the script text from _program - skipped while the script box has focus, so it never fights active typing there.</summary>
        private void RefreshScriptFromProgram()
        {
            if (_script.Focused) return;
            _script.TextChanged -= Script_TextChanged;
            _script.Text = ProgramScriptFormat.ToScript(_program);
            _script.TextChanged += Script_TextChanged;
        }

        private void Script_TextChanged(object sender, EventArgs e)
        {
            _scriptSyncTimer.Stop();
            _scriptSyncTimer.Start();
        }

        /// <summary>
        /// Fires a moment after typing stops. Parsing is deliberately lenient
        /// (see ProgramScriptFormat) so a momentarily half-typed line just
        /// resolves to something reasonable rather than losing anything; a
        /// truly empty parse (e.g. the box was cleared) is ignored rather than
        /// wiping out whatever program existed a keystroke ago.
        /// </summary>
        private void ScriptSyncTimer_Tick(object sender, EventArgs e)
        {
            _scriptSyncTimer.Stop();

            WorkoutProgram parsed = ProgramScriptFormat.Parse(_script.Text);
            if (parsed.SharedTimeline.IsEmpty && parsed.Stations.Count == 0) return;

            _program = parsed;
            _groupContext = null;
            _lastOutlineTag = null;

            _programName.TextChanged -= ProgramName_TextChanged;
            _programName.Text = _program.Name;
            _programName.TextChanged += ProgramName_TextChanged;

            _modeShared.Checked = _program.Mode == ExecutionMode.Shared;
            _modeSequential.Checked = _program.Mode == ExecutionMode.Sequential;
            _modeParallel.Checked = _program.Mode == ExecutionMode.Parallel;
            UpdateModeDescription();

            RefreshOutline();
            RefreshPropertiesForOutline();
        }

        // ============================================================ save / ok

        private void Save_Click(object sender, EventArgs e)
        {
            ApplyControlsTo(Result);
            Result.Save();
            _saveStatusLabel.Text = "Saved to " + TimerSettings.FilePath;
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            Result = _original.Clone();
            ApplyControlsTo(Result);
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Copies every control (and every tracked field) onto the given settings object.</summary>
        private void ApplyControlsTo(TimerSettings target)
        {
            // Simple session
            target.WorkSeconds = Math.Max(1, (int)_work.Value);
            target.RestSeconds = (int)_rest.Value;
            target.Rounds = (int)_rounds.Value;
            string plan = _plan.Text.Trim();
            target.Plan = (plan.Length > 0 && !IntervalPlan.Parse(plan).IsEmpty) ? plan : string.Empty;
            target.WorkLabel = string.IsNullOrEmpty(_workLabelText.Text.Trim()) ? "WORK" : _workLabelText.Text.Trim();
            target.RestLabel = string.IsNullOrEmpty(_restLabelText.Text.Trim()) ? "REST" : _restLabelText.Text.Trim();
            target.RestAfterFinalRound = _restAfterFinal.Checked;
            target.UseMoveTime = _useMoveTime.Checked;
            target.MoveSeconds = Math.Max(1, (int)_moveSeconds.Value);
            target.MoveMessage = string.IsNullOrEmpty(_moveMessage.Text.Trim())
                ? "Move to the next station" : _moveMessage.Text.Trim();

            // Built program - kept regardless of which source is active, so
            // switching back and forth never loses either one.
            target.ProgramScript = ProgramScriptFormat.ToScript(_program);
            target.UseBuiltProgram = _sourceBuilt.Checked;

            // General settings, tracked via the separate window
            target.PrepSeconds = _gPrepSeconds;
            target.Volume = _gVolume;
            target.ClockFormat = _gClockFormat;
            target.SoundEnabled = _gSoundEnabled;
            target.ShowClock = _gShowClock;
            target.AlwaysOnTop = _gAlwaysOnTop;
            target.ShowStationsTable = _gShowStationsTable;
            target.ShowStationNameAsDescription = _gShowStationNameAsDescription;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _scriptSyncTimer.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
    }
}
