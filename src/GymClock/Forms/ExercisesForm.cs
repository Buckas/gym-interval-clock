using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GymClock.AI;

namespace GymClock
{
    /// <summary>
    /// Manage the exercise library used for AI suggestions: the gym's equipment (including
    /// equipment the teacher adds), add / edit / hide exercises, and download library updates.
    /// Every change is saved to the teacher's own file (my-exercises.json); library updates never touch it.
    /// </summary>
    public sealed class ExercisesForm : Form
    {
        /// <summary>One piece of equipment: built in (key = enum name) or added by the teacher (key = id).</summary>
        private sealed class EquipmentOption
        {
            public string Key;
            public string Label;
            public bool Custom;
        }

        private static readonly (ExerciseKit Kit, string Label)[] BuiltInKit =
        {
            (ExerciseKit.Dumbbell, "Dumbbells"),
            (ExerciseKit.Kettlebell, "Kettlebells"),
            (ExerciseKit.WeightPlate, "Weight plates"),
            (ExerciseKit.Band, "Resistance bands"),
            (ExerciseKit.MedicineBall, "Medicine balls"),
            (ExerciseKit.SlamBall, "Slam balls"),
            (ExerciseKit.StepOrBench, "Steps / benches"),
            (ExerciseKit.Box, "Plyo boxes"),
            (ExerciseKit.SkippingRope, "Skipping ropes"),
            (ExerciseKit.BattleRope, "Battle ropes"),
            (ExerciseKit.CardioMachine, "Cardio machines"),
            (ExerciseKit.Suspension, "TRX / suspension"),
            (ExerciseKit.Cones, "Cones"),
        };

        private static readonly (ExerciseTag Tag, string Label)[] TagChoices =
        {
            (ExerciseTag.Cardio, "Cardio"),
            (ExerciseTag.Strength, "Resistance"),
            (ExerciseTag.Core, "Core"),
            (ExerciseTag.Plyometric, "Plyometric"),
            (ExerciseTag.Mobility, "Mobility"),
            (ExerciseTag.Upper, "Upper body"),
            (ExerciseTag.Lower, "Lower body"),
            (ExerciseTag.FullBody, "Full body"),
            (ExerciseTag.LowImpact, "Low impact"),
        };

        private const int GridColumns = 6;

        private readonly CheckBox _allEquipment = new CheckBox { Text = "Don't filter - suggest any equipment", AutoSize = true };
        private readonly TableLayoutPanel _gymGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = GridColumns };
        private readonly Dictionary<string, CheckBox> _gymKit = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Button _addEquipment = new Button { Text = "Add equipment..." };
        private readonly Button _removeEquipment = new Button { Text = "Remove equipment..." };

        private readonly TextBox _search = new TextBox { Dock = DockStyle.Fill };
        private readonly CheckBox _showHidden = new CheckBox { Text = "Show hidden", AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };

        private readonly TextBox _name = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _cue = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 54 };
        private readonly Dictionary<ExerciseTag, CheckBox> _tags = new Dictionary<ExerciseTag, CheckBox>();
        private readonly FlowLayoutPanel _kitEdit = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        private readonly Dictionary<string, CheckBox> _kit = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly CheckBox _wall = new CheckBox { Text = "Wall", AutoSize = true };
        private readonly CheckBox _floor = new CheckBox { Text = "Done on the floor / a mat", AutoSize = true };
        private readonly Label _origin = new Label { AutoSize = true, ForeColor = Color.DimGray };

        private readonly Button _new = new Button { Text = "New exercise" };
        private readonly Button _save = new Button { Text = "Save" };
        private readonly Button _hide = new Button { Text = "Hide" };
        private readonly Button _revert = new Button { Text = "Revert to library" };
        private readonly Button _delete = new Button { Text = "Delete" };

        private readonly Label _status = new Label { AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left };
        private readonly Button _checkUpdates = new Button { Text = "Check for library updates" };

        private UserExerciseDocument _user;
        private string _editingId;      // null = a new exercise
        private bool _loading;

        public ExercisesForm()
        {
            Text = "Exercise Library";
            Icon = AppIconProvider.GetIcon();
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(work.Width, Math.Max(1000, work.Width * 3 / 4)), Math.Min(work.Height, Math.Max(720, work.Height * 4 / 5)));
            MinimumSize = new Size(Math.Min(work.Width, 860), Math.Min(work.Height, 600));
            ShowInTaskbar = false;

            // The Program & Settings window is TopMost when "keep window on top" is on - without
            // this, the library would open behind it.
            TopMost = true;

            _user = ExerciseCatalog.LoadUserDocument();
            BuildLayout();
            RebuildEquipment();
            RefreshList(null);
            RefreshStatus();
            ShowExercise(null);
        }

        // ------------------------------------------------------------------ equipment

        private List<EquipmentOption> EquipmentOptions()
        {
            List<EquipmentOption> options = BuiltInKit
                .Select(k => new EquipmentOption { Key = ExerciseCatalog.KitToNames(k.Kit)[0], Label = k.Label })
                .ToList();
            options.AddRange((_user.CustomEquipment ?? new List<CustomEquipment>())
                .Where(c => !string.IsNullOrWhiteSpace(c?.Id) && !string.IsNullOrWhiteSpace(c.Name))
                .OrderBy(c => c.Name)
                .Select(c => new EquipmentOption { Key = c.Id, Label = c.Name, Custom = true }));
            return options;
        }

        private string EquipmentLabel(string key)
        {
            EquipmentOption option = EquipmentOptions().FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));
            if (option != null) return option.Label;
            return string.Equals(key, "wall", StringComparison.OrdinalIgnoreCase) ? "Wall" : key;
        }

        /// <summary>(Re)creates the equipment tick-boxes in the gym grid and the exercise editor.</summary>
        private void RebuildEquipment()
        {
            _loading = true;
            List<EquipmentOption> options = EquipmentOptions();
            HashSet<string> owned = _user.Equipment == null ? null : new HashSet<string>(_user.Equipment, StringComparer.OrdinalIgnoreCase);

            _gymGrid.SuspendLayout();
            _gymGrid.Controls.Clear();
            _gymKit.Clear();
            _gymGrid.Controls.Add(_allEquipment);
            _gymGrid.SetColumnSpan(_allEquipment, 2);
            _allEquipment.Checked = owned == null;
            foreach (EquipmentOption option in options)
            {
                CheckBox box = new CheckBox
                {
                    Text = option.Label,
                    AutoSize = true,
                    Margin = new Padding(3, 3, 12, 3),
                    Checked = owned == null || owned.Contains(option.Key),
                    Enabled = owned != null,
                    ForeColor = option.Custom ? Color.SteelBlue : SystemColors.ControlText
                };
                box.CheckedChanged += (s, e) => { if (!_loading) SaveEquipment(); };
                _gymKit[option.Key] = box;
                _gymGrid.Controls.Add(box);
            }
            _gymGrid.ResumeLayout();
            _removeEquipment.Enabled = options.Any(o => o.Custom);

            // Exercise editor: keep what is ticked for the exercise being edited.
            HashSet<string> ticked = new HashSet<string>(_kit.Where(p => p.Value.Checked).Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
            _kitEdit.SuspendLayout();
            _kitEdit.Controls.Clear();
            _kit.Clear();
            _kitEdit.Controls.Add(new Label { Text = "(none ticked = bodyweight)", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 8, 3) });
            foreach (EquipmentOption option in options)
            {
                CheckBox box = new CheckBox
                {
                    Text = option.Label,
                    AutoSize = true,
                    Checked = ticked.Contains(option.Key),
                    ForeColor = option.Custom ? Color.SteelBlue : SystemColors.ControlText
                };
                _kit[option.Key] = box;
                _kitEdit.Controls.Add(box);
            }
            _kitEdit.Controls.Add(_wall);
            _kitEdit.ResumeLayout();
            _loading = false;
        }

        private void SaveEquipment()
        {
            foreach (CheckBox b in _gymKit.Values) b.Enabled = !_allEquipment.Checked;
            _user.Equipment = _allEquipment.Checked ? null : _gymKit.Where(p => p.Value.Checked).Select(p => p.Key).ToList();
            SaveUser();
        }

        private void AddEquipment()
        {
            string name = Prompt("Add equipment", "Name of the equipment (e.g. Sled, Agility ladder, Spin bikes):");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();

            if (EquipmentOptions().Any(o => string.Equals(o.Label, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "\"" + name + "\" is already in the list.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string id = ExerciseCatalog.NewEquipmentId(name);
            while (EquipmentOptions().Any(o => string.Equals(o.Key, id, StringComparison.OrdinalIgnoreCase))) id += "-2";

            _user.CustomEquipment.Add(new CustomEquipment { Id = id, Name = name });
            if (_user.Equipment != null) _user.Equipment.Add(id);   // you're adding it because the gym has it
            SaveUser();
            RebuildEquipment();
            MessageBox.Show(this, "\"" + name + "\" added. Tick it under \"Equipment needed\" on any exercise that uses it, "
                + "or create new exercises for it.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RemoveEquipment()
        {
            List<CustomEquipment> custom = _user.CustomEquipment ?? new List<CustomEquipment>();
            if (custom.Count == 0) return;

            CustomEquipment item = Choose("Remove equipment", "Which equipment do you want to remove?", custom);
            if (item == null) return;

            int used = _user.Exercises.Count(x => (x.Equipment ?? new List<string>()).Any(k => string.Equals(k, item.Id, StringComparison.OrdinalIgnoreCase)));
            string warning = used == 0 ? string.Empty
                : Environment.NewLine + used + (used == 1 ? " of your exercises uses it" : " of your exercises use it") + " - it will be taken off them.";
            if (MessageBox.Show(this, "Remove \"" + item.Name + "\"?" + warning, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            custom.RemoveAll(c => string.Equals(c.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            _user.Equipment?.RemoveAll(k => string.Equals(k, item.Id, StringComparison.OrdinalIgnoreCase));
            foreach (ExerciseRecord r in _user.Exercises) r.Equipment?.RemoveAll(k => string.Equals(k, item.Id, StringComparison.OrdinalIgnoreCase));
            SaveUser();
            RebuildEquipment();
            RefreshList(_editingId);
            ShowExercise(_editingId);
        }

        // ------------------------------------------------------------------ layout

        private void BuildLayout()
        {
            // Equipment in the gym
            GroupBox gym = new GroupBox
            {
                Text = "Equipment in our gym (suggestions only use these, unless a request names other equipment)",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8)
            };
            for (int c = 0; c < GridColumns; c++) _gymGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / GridColumns));
            _allEquipment.CheckedChanged += (s, e) => { if (!_loading) SaveEquipment(); };

            foreach (Button b in new[] { _addEquipment, _removeEquipment })
            {
                b.AutoSize = true;
                b.Padding = new Padding(8, 2, 8, 2);
            }
            _addEquipment.Click += (s, e) => AddEquipment();
            _removeEquipment.Click += (s, e) => RemoveEquipment();
            FlowLayoutPanel equipmentButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            equipmentButtons.Controls.AddRange(new Control[] { _addEquipment, _removeEquipment });

            TableLayoutPanel gymStack = Stack();
            gymStack.Dock = DockStyle.Fill;
            AddRow(gymStack, _gymGrid);
            AddRow(gymStack, equipmentButtons);
            gym.Controls.Add(gymStack);

            // Left: search + list
            TableLayoutPanel searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.Controls.Add(new Label { Text = "Search:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            searchRow.Controls.Add(_search, 1, 0);
            searchRow.Controls.Add(_showHidden, 2, 0);
            _search.TextChanged += (s, e) => RefreshList(_editingId);
            _showHidden.CheckedChanged += (s, e) => RefreshList(_editingId);

            _list.Columns.Add("Exercise", 220);
            _list.Columns.Add("Type", 260);
            _list.Columns.Add("Equipment", 190);
            _list.Columns.Add("Source", 80);
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedItems.Count == 1) ShowExercise((string)_list.SelectedItems[0].Tag);
            };

            TableLayoutPanel left = Stack();
            left.AutoSize = false;
            left.Dock = DockStyle.Fill;
            AddRow(left, searchRow);
            AddRow(left, _list, SizeType.Percent, 100);

            // Right: details
            FlowLayoutPanel tagFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            foreach (var (tag, label) in TagChoices)
            {
                CheckBox box = new CheckBox { Text = label, AutoSize = true };
                _tags[tag] = box;
                tagFlow.Controls.Add(box);
            }

            foreach (Button b in new[] { _new, _save, _hide, _revert, _delete })
            {
                b.AutoSize = true;
                b.Padding = new Padding(8, 3, 8, 3);
            }
            _new.Click += (s, e) => { _list.SelectedItems.Clear(); ShowExercise(null); _name.Focus(); };
            _save.Click += (s, e) => SaveExercise();
            _hide.Click += (s, e) => ToggleHidden();
            _revert.Click += (s, e) => RevertToLibrary();
            _delete.Click += (s, e) => DeleteCustom();
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { _new, _save, _hide, _revert, _delete });

            TableLayoutPanel right = Stack();
            right.AutoSize = false;
            right.Dock = DockStyle.Fill;
            AddRow(right, _origin);
            AddRow(right, Heading("Name"));
            AddRow(right, _name);
            AddRow(right, Heading("Coaching cue"));
            AddRow(right, _cue);
            AddRow(right, Heading("Type"));
            AddRow(right, tagFlow);
            AddRow(right, Heading("Equipment needed"));
            AddRow(right, _kitEdit);
            AddRow(right, _floor);
            AddRow(right, buttons);

            TableLayoutPanel columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columns.Controls.Add(left, 0, 0);
            columns.Controls.Add(right, 1, 0);

            // Bottom: library status + updates
            _checkUpdates.AutoSize = true;
            _checkUpdates.Padding = new Padding(8, 3, 8, 3);
            _checkUpdates.Click += async (s, e) => await CheckForUpdatesAsync();
            Button close = new Button { Text = "Close", AutoSize = true, Padding = new Padding(16, 3, 16, 3), DialogResult = DialogResult.OK };
            TableLayoutPanel bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(_status, 0, 0);
            bottom.Controls.Add(_checkUpdates, 1, 0);
            bottom.Controls.Add(close, 2, 0);
            CancelButton = close;

            TableLayoutPanel root = Stack();
            root.AutoSize = false;
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(10);
            AddRow(root, gym);
            AddRow(root, columns, SizeType.Percent, 100);
            AddRow(root, bottom);
            Controls.Add(root);
        }

        private static TableLayoutPanel Stack()
        {
            TableLayoutPanel p = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return p;
        }

        private static void AddRow(TableLayoutPanel p, Control c, SizeType type = SizeType.AutoSize, float height = 0)
        {
            int row = p.RowCount;
            p.RowCount = row + 1;
            p.RowStyles.Add(new RowStyle(type, height));
            p.Controls.Add(c, 0, row);
        }

        private Label Heading(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 2) };
        }

        // ------------------------------------------------------------------ data

        private void SaveUser()
        {
            ExerciseCatalog.SaveUserDocument(_user);
            RefreshStatus();
        }

        private CatalogEntry Find(string id)
        {
            return id == null ? null : ExerciseCatalog.Current.Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private string EquipmentText(ExerciseRecord r)
        {
            List<string> keys = (r.Equipment ?? new List<string>()).ToList();
            if (keys.Count == 0) return "Bodyweight";
            return string.Join(", ", keys.Select(EquipmentLabel));
        }

        private void RefreshList(string selectId)
        {
            string filter = _search.Text.Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (CatalogEntry entry in ExerciseCatalog.Current.Entries.OrderBy(e => e.Record.Name))
            {
                if (entry.Hidden && !_showHidden.Checked) continue;
                string types = string.Join(", ", TagChoices.Where(t => (entry.Exercise.Tags & t.Tag) != 0).Select(t => t.Label));
                string kit = EquipmentText(entry.Record);
                string match = entry.Record.Name + " " + types + " " + kit + " " + entry.Record.Cue;
                if (filter.Length > 0 && match.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                string source = entry.Origin == ExerciseOrigin.Custom ? "Mine" : entry.Origin == ExerciseOrigin.Edited ? "Edited" : "Library";
                ListViewItem item = new ListViewItem(new[] { entry.Record.Name + (entry.Hidden ? " (hidden)" : string.Empty), types, kit, source }) { Tag = entry.Id };
                if (entry.Hidden) item.ForeColor = Color.Gray;
                else if (entry.Origin != ExerciseOrigin.Library) item.ForeColor = Color.SteelBlue;
                _list.Items.Add(item);
                if (selectId != null && string.Equals(entry.Id, selectId, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
            }
            _list.EndUpdate();
            if (_list.SelectedItems.Count == 1) _list.SelectedItems[0].EnsureVisible();
        }

        private void RefreshStatus()
        {
            CatalogSnapshot s = ExerciseCatalog.Current;
            int mine = s.Entries.Count(e => e.Origin == ExerciseOrigin.Custom);
            int edited = s.Entries.Count(e => e.Origin == ExerciseOrigin.Edited);
            int hidden = s.Entries.Count(e => e.Hidden);
            _status.Text = "Library version " + s.LibraryVersion + (s.LibraryIsDownloaded ? " (downloaded)" : " (built in)")
                + "  -  " + s.Entries.Count + " exercises: " + mine + " yours, " + edited + " edited, " + hidden + " hidden";
        }

        private void ShowExercise(string id)
        {
            CatalogEntry entry = Find(id);
            _editingId = entry?.Id;
            ExerciseRecord r = entry?.Record ?? new ExerciseRecord();
            Exercise e = entry?.Exercise ?? new Exercise(string.Empty, string.Empty, ExerciseTag.None);
            HashSet<string> needs = new HashSet<string>(r.Equipment ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            _name.Text = r.Name;
            _cue.Text = r.Cue;
            foreach (var pair in _tags) pair.Value.Checked = (e.Tags & pair.Key) != 0;
            foreach (var pair in _kit) pair.Value.Checked = needs.Contains(pair.Key);
            _wall.Checked = needs.Contains("wall");
            _floor.Checked = e.Floor;

            _origin.Text = entry == null ? "New exercise - fill in the details and Save."
                : entry.Origin == ExerciseOrigin.Library ? "From the library. Saving changes keeps your version, even after library updates."
                : entry.Origin == ExerciseOrigin.Edited ? "Your edited version of a library exercise (library updates won't change it)."
                : "Your own exercise.";

            _hide.Enabled = entry != null;
            _hide.Text = entry != null && entry.Hidden ? "Show" : "Hide";
            _revert.Enabled = entry != null && entry.Origin == ExerciseOrigin.Edited;
            _delete.Enabled = entry != null && entry.Origin == ExerciseOrigin.Custom;
        }

        private void SaveExercise()
        {
            string name = _name.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "Give the exercise a name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ExerciseTag tags = _tags.Where(p => p.Value.Checked).Aggregate(ExerciseTag.None, (t, p) => t | p.Key);
            List<string> equipment = _kit.Where(p => p.Value.Checked).Select(p => p.Key).ToList();
            if (_wall.Checked) equipment.Add("wall");

            string id = _editingId ?? ExerciseCatalog.NewCustomId(name);
            ExerciseRecord record = new ExerciseRecord
            {
                Id = id,
                Name = name,
                Cue = _cue.Text.Trim(),
                Tags = ExerciseCatalog.TagsToNames(tags),
                Equipment = equipment,
                Floor = _floor.Checked
            };

            _user.Exercises.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            _user.Exercises.Add(record);
            SaveUser();
            RefreshList(id);
            ShowExercise(id);
        }

        private void ToggleHidden()
        {
            CatalogEntry entry = Find(_editingId);
            if (entry == null) return;
            if (entry.Hidden) _user.Hidden.RemoveAll(h => string.Equals(h, entry.Id, StringComparison.OrdinalIgnoreCase));
            else _user.Hidden.Add(entry.Id);
            SaveUser();
            RefreshList(entry.Id);
            ShowExercise(entry.Id);
        }

        private void RevertToLibrary()
        {
            CatalogEntry entry = Find(_editingId);
            if (entry == null || entry.Origin != ExerciseOrigin.Edited) return;
            if (MessageBox.Show(this, "Throw away your changes to \"" + entry.Record.Name + "\" and use the library version?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _user.Exercises.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            SaveUser();
            RefreshList(entry.Id);
            ShowExercise(entry.Id);
        }

        private void DeleteCustom()
        {
            CatalogEntry entry = Find(_editingId);
            if (entry == null || entry.Origin != ExerciseOrigin.Custom) return;
            if (MessageBox.Show(this, "Delete your exercise \"" + entry.Record.Name + "\"?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _user.Exercises.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            _user.Hidden.RemoveAll(h => string.Equals(h, entry.Id, StringComparison.OrdinalIgnoreCase));
            SaveUser();
            RefreshList(null);
            ShowExercise(null);
        }

        private async Task CheckForUpdatesAsync()
        {
            _checkUpdates.Enabled = false;
            string before = _status.Text;
            _status.Text = "Checking for library updates...";
            try
            {
                LibraryUpdateResult result = await ExerciseCatalog.CheckForUpdateAsync(force: true).ConfigureAwait(true);
                if (IsDisposed) return;
                RefreshStatus();
                RefreshList(_editingId);
                ShowExercise(_editingId);
                string message;
                switch (result)
                {
                    case LibraryUpdateResult.Updated:
                        message = "Library updated to version " + ExerciseCatalog.Current.LibraryVersion
                            + ". Your own exercises, edits, equipment and hidden list were kept.";
                        break;
                    case LibraryUpdateResult.AlreadyUpToDate:
                        message = "You already have the latest library (version " + ExerciseCatalog.Current.LibraryVersion + ").";
                        break;
                    case LibraryUpdateResult.Unavailable:
                        message = "Couldn't reach the library online (no internet, or blocked by the network). Nothing changed.";
                        break;
                    default:
                        message = "The downloaded library wasn't valid, so it was ignored. Nothing changed.";
                        break;
                }
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                _status.Text = before;
            }
            finally
            {
                if (!IsDisposed) _checkUpdates.Enabled = true;
            }
        }

        // ------------------------------------------------------------------ small dialogs

        private string Prompt(string title, string message)
        {
            using (Form dialog = SmallDialog(title, out TableLayoutPanel body, out Button ok))
            {
                TextBox input = new TextBox { Dock = DockStyle.Fill, Width = 360 };
                AddRow(body, new Label { Text = message, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
                AddRow(body, input);
                AddRow(body, DialogButtons(dialog, ok));
                dialog.AcceptButton = ok;
                return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
            }
        }

        private CustomEquipment Choose(string title, string message, List<CustomEquipment> items)
        {
            using (Form dialog = SmallDialog(title, out TableLayoutPanel body, out Button ok))
            {
                ComboBox combo = new ComboBox { Dock = DockStyle.Fill, Width = 360, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
                foreach (CustomEquipment item in items.OrderBy(i => i.Name)) combo.Items.Add(item);
                combo.SelectedIndex = 0;
                AddRow(body, new Label { Text = message, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
                AddRow(body, combo);
                AddRow(body, DialogButtons(dialog, ok));
                dialog.AcceptButton = ok;
                return dialog.ShowDialog(this) == DialogResult.OK ? combo.SelectedItem as CustomEquipment : null;
            }
        }

        private Form SmallDialog(string title, out TableLayoutPanel body, out Button ok)
        {
            Form dialog = new Form
            {
                Text = title,
                Font = Font,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                TopMost = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12)
            };
            body = Stack();
            body.Dock = DockStyle.Fill;
            dialog.Controls.Add(body);
            ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
            return dialog;
        }

        private static FlowLayoutPanel DialogButtons(Form dialog, Button ok)
        {
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
            dialog.CancelButton = cancel;
            FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 10, 0, 0) };
            row.Controls.Add(cancel);
            row.Controls.Add(ok);
            return row;
        }
    }
}
