using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// The hierarchical program editor: Program Outline (stations and standalone
    /// between-station blocks) on the left, the Selected Timeline in the middle,
    /// and a Properties panel on the right - plus a Script tab for hand-editing
    /// the same program as text. See ProgramScriptFormat for the grammar.
    /// </summary>
    public partial class ProgramBuilderForm : Form
    {
        private WorkoutProgram _program;
        private RepeatGroup _groupContext;      // non-null while drilled into a repeat group's own blocks
        private object _lastOutlineTag;

        public WorkoutProgram Result { get; private set; }

        public ProgramBuilderForm(WorkoutProgram program)
        {
            InitializeComponent();
            _program = program != null ? program.Clone() : new WorkoutProgram();
            RefreshOutline();
        }

        // ------------------------------------------------------------- outline

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
                if (_program.Stations.Count >= 2)
                {
                    root.Nodes.Add(new TreeNode("Between Stations") { Tag = "BETWEEN" });
                }
            }

            root.ExpandAll();
            _outline.EndUpdate();

            _outline.SelectedNode = FindNodeByTag(root, previousTag) ?? root;
        }

        /// <summary>Updates the outline's node text in place - for edits (a renamed station, a renamed program)
        /// that don't change which nodes exist, so the tree selection and drill-down state are left alone.</summary>
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

        /// <summary>The Timeline the middle and right panes are currently working on, or null when the Program root itself is selected.</summary>
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
            else if ("PROGRAM".Equals(tag)) _properties.SelectedObject = new ProgramInfo(_program);
            else _properties.SelectedObject = null;
        }

        // ---- station actions

        private void AddStation_Click(object sender, EventArgs e)
        {
            if (_program.Mode != ExecutionMode.Sequential)
            {
                if (MessageBox.Show(this,
                    "Adding a station switches this program to Sequential Stations mode. Continue?",
                    "Add Station", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _program.Mode = ExecutionMode.Sequential;
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

        // -------------------------------------------------------------- timeline

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

        // ------------------------------------------------------------ properties

        private void Properties_PropertyValueChanged(object sender, PropertyValueChangedEventArgs e)
        {
            if (_properties.SelectedObject is ProgramInfo) RefreshOutline();
            else RefreshOutlineLabelsOnly();

            RefreshTimelineList();
        }

        /// <summary>PropertyGrid wrapper for the Program root - only properties are ever shown, so
        /// WorkoutProgram's own Name/Mode fields aren't editable there directly.</summary>
        private class ProgramInfo
        {
            private readonly WorkoutProgram _program;
            public ProgramInfo(WorkoutProgram program) { _program = program; }

            public string Name
            {
                get { return _program.Name; }
                set { _program.Name = string.IsNullOrEmpty(value) ? "Workout" : value; }
            }

            public ExecutionMode Mode
            {
                get { return _program.Mode; }
                set { _program.Mode = value; }
            }
        }

        // ----------------------------------------------------------------- script

        private void Tabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_tabs.SelectedTab == _scriptTab)
            {
                _scriptText.Text = ProgramScriptFormat.ToScript(_program);
                _scriptStatus.Text = string.Empty;
            }
        }

        private void ApplyScript_Click(object sender, EventArgs e)
        {
            WorkoutProgram parsed = ProgramScriptFormat.Parse(_scriptText.Text);
            if (parsed.SharedTimeline.IsEmpty && parsed.Stations.Count == 0)
            {
                _scriptStatus.Text = "Nothing recognised - check the script.";
                return;
            }

            _program = parsed;
            _groupContext = null;
            _lastOutlineTag = null;
            RefreshOutline();
            _scriptStatus.Text = "Applied.";
        }

        // --------------------------------------------------------------------- ok

        private void Ok_Click(object sender, EventArgs e)
        {
            // A script edit that was never applied shouldn't silently lose out to
            // whatever the Builder tab had before it was typed.
            if (_tabs.SelectedTab == _scriptTab) ApplyScript_Click(sender, e);
            Result = _program;
        }
    }
}
