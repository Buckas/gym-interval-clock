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
    /// Manage the exercise library used for AI suggestions: the gym's equipment, add / edit /
    /// hide exercises, and download library updates. Every change is saved to the teacher's own
    /// file (my-exercises.json); library updates never touch it.
    /// </summary>
    public sealed class ExercisesForm : Form
    {
        private static readonly (ExerciseKit Kit, string Label)[] KitChoices =
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

        private readonly Dictionary<ExerciseKit, CheckBox> _gymKit = new Dictionary<ExerciseKit, CheckBox>();
        private readonly CheckBox _allEquipment = new CheckBox { Text = "Don't filter - suggest any equipment", AutoSize = true };
        private readonly TextBox _search = new TextBox { Dock = DockStyle.Fill };
        private readonly CheckBox _showHidden = new CheckBox { Text = "Show hidden", AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };

        private readonly TextBox _name = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _cue = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 54 };
        private readonly Dictionary<ExerciseTag, CheckBox> _tags = new Dictionary<ExerciseTag, CheckBox>();
        private readonly Dictionary<ExerciseKit, CheckBox> _kit = new Dictionary<ExerciseKit, CheckBox>();
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

            BuildLayout();
            LoadUser();
            RefreshList(null);
            RefreshStatus();
            ShowExercise(null);
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
            // A fixed grid (a wrapping flow panel inside a group box doesn't grow to show its second line).
            const int gridColumns = 7;
            TableLayoutPanel kitFlow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = gridColumns
            };
            for (int c = 0; c < gridColumns; c++) kitFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / gridColumns));
            kitFlow.Controls.Add(_allEquipment);
            kitFlow.SetColumnSpan(_allEquipment, 2);
            foreach (var (kit, label) in KitChoices)
            {
                CheckBox box = new CheckBox { Text = label, AutoSize = true, Margin = new Padding(3, 3, 12, 3) };
                box.CheckedChanged += (s, e) => { if (!_loading) SaveEquipment(); };
                _gymKit[kit] = box;
                kitFlow.Controls.Add(box);
            }
            _allEquipment.CheckedChanged += (s, e) =>
            {
                foreach (CheckBox b in _gymKit.Values) b.Enabled = !_allEquipment.Checked;
                if (!_loading) SaveEquipment();
            };
            gym.Controls.Add(kitFlow);

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
            FlowLayoutPanel kitEdit = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            kitEdit.Controls.Add(new Label { Text = "(none ticked = bodyweight)", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 8, 3) });
            foreach (var (kit, label) in KitChoices)
            {
                CheckBox box = new CheckBox { Text = label, AutoSize = true };
                _kit[kit] = box;
                kitEdit.Controls.Add(box);
            }
            kitEdit.Controls.Add(_wall);

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
            right.Dock = DockStyle.Fill;
            AddRow(right, _origin);
            AddRow(right, Heading("Name"));
            AddRow(right, _name);
            AddRow(right, Heading("Coaching cue"));
            AddRow(right, _cue);
            AddRow(right, Heading("Type"));
            AddRow(right, tagFlow);
            AddRow(right, Heading("Equipment needed"));
            AddRow(right, kitEdit);
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
            AcceptButton = null;
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

        private void LoadUser()
        {
            _loading = true;
            _user = ExerciseCatalog.LoadUserDocument();
            ExerciseKit? owned = _user.Equipment == null ? (ExerciseKit?)null : ExerciseCatalog.ParseKit(_user.Equipment);
            _allEquipment.Checked = owned == null;
            foreach (var pair in _gymKit)
            {
                pair.Value.Checked = owned == null || (owned.Value & pair.Key) != 0;
                pair.Value.Enabled = owned != null;
            }
            _loading = false;
        }

        private void SaveUser()
        {
            ExerciseCatalog.SaveUserDocument(_user);
            RefreshStatus();
        }

        private void SaveEquipment()
        {
            if (_allEquipment.Checked)
            {
                _user.Equipment = null;
            }
            else
            {
                ExerciseKit owned = _gymKit.Where(p => p.Value.Checked).Aggregate(ExerciseKit.None, (k, p) => k | p.Key);
                _user.Equipment = ExerciseCatalog.KitToNames(owned);
            }
            SaveUser();
        }

        private CatalogEntry Find(string id)
        {
            return id == null ? null : ExerciseCatalog.Current.Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
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
                string kit = entry.Exercise.Kit == ExerciseKit.None ? "Bodyweight"
                    : string.Join(", ", KitChoices.Where(k => (entry.Exercise.Kit & k.Kit) != 0).Select(k => k.Label)
                        .Concat((entry.Exercise.Kit & ExerciseKit.Wall) != 0 ? new[] { "Wall" } : Array.Empty<string>()));
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

            _name.Text = r.Name;
            _cue.Text = r.Cue;
            foreach (var pair in _tags) pair.Value.Checked = (e.Tags & pair.Key) != 0;
            foreach (var pair in _kit) pair.Value.Checked = (e.Kit & pair.Key) != 0;
            _wall.Checked = (e.Kit & ExerciseKit.Wall) != 0;
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
            ExerciseKit kit = _kit.Where(p => p.Value.Checked).Aggregate(ExerciseKit.None, (k, p) => k | p.Key);
            if (_wall.Checked) kit |= ExerciseKit.Wall;

            string id = _editingId ?? ExerciseCatalog.NewCustomId(name);
            ExerciseRecord record = new ExerciseRecord
            {
                Id = id,
                Name = name,
                Cue = _cue.Text.Trim(),
                Tags = ExerciseCatalog.TagsToNames(tags),
                Equipment = ExerciseCatalog.KitToNames(kit),
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
                            + ". Your own exercises, edits, hidden list and equipment were kept.";
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
    }
}
