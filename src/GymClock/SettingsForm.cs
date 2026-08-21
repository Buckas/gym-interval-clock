using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// Opened at any time with S or F2. Changes take effect the moment you press OK,
    /// even mid-interval, so you can shorten a round on the fly.
    /// </summary>
    public partial class SettingsForm : Form
    {
        private readonly TimerSettings _original;

        public TimerSettings Result { get; private set; }

        /// <summary>
        /// Parameterless constructor required by the Windows Forms designer at design time.
        /// Not intended to be used at runtime.
        /// </summary>
        public SettingsForm()
            : this(new TimerSettings())
        {
        }

        public SettingsForm(TimerSettings settings)
        {
            _original = settings;
            Result = settings.Clone();

            InitializeComponent();

            // The designer bakes in whatever path the developer's machine had, so
            // this must be set at runtime or every user sees somebody else's profile.
            _pathLabel.Text = "Settings file (editable in Notepad):"
                + Environment.NewLine + TimerSettings.FilePath;

            _prep.Minimum = 0;
            _prep.Maximum = 300;
            _prep.Value = Clamp(settings.PrepSeconds, _prep);

            _work.Minimum = 1;
            _work.Maximum = 3600;
            _plan.Text = settings.Plan ?? string.Empty;
            _plan.TextChanged += delegate { UpdatePlanPreview(); };
            _prep.ValueChanged += delegate { UpdatePlanPreview(); };
            _work.ValueChanged += delegate { UpdatePlanPreview(); };
            _rest.ValueChanged += delegate { UpdatePlanPreview(); };
            _rounds.ValueChanged += delegate { UpdatePlanPreview(); };

            _work.Value = Clamp(settings.WorkSeconds, _work);

            _rest.Minimum = 0;
            _rest.Maximum = 3600;
            _rest.Value = Clamp(settings.RestSeconds, _rest);

            _rounds.Minimum = 0;
            _rounds.Maximum = 999;
            _rounds.Value = Clamp(settings.Rounds, _rounds);

            _volume.Minimum = 5;
            _volume.Maximum = 100;
            _volume.Value = Clamp((int)Math.Round(settings.Volume * 100), _volume);

            _workLabel.Text = settings.WorkLabel;
            _restLabel.Text = settings.RestLabel;

            _clockFormat.Text = settings.ClockFormat;

            _restAfterFinal.Checked = settings.RestAfterFinalRound;
            _sound.Checked = settings.SoundEnabled;
            _showClock.Checked = settings.ShowClock;
            _onTop.Checked = settings.AlwaysOnTop;

            _showStationsTable.Checked = settings.ShowStationsTable;
            _useStationWording.Checked = settings.UseStationWording;
            _highlightCurrentStation.Checked = settings.HighlightCurrentStation;

            _moveSeconds.Minimum = 1;
            _moveSeconds.Maximum = 600;
            _moveSeconds.Value = Clamp(settings.MoveSeconds, _moveSeconds);
            _useMoveTime.Checked = settings.UseMoveTime;
            _moveMessage.Text = settings.MoveMessage;

            LoadStationsGrid(settings.Stations);
            RefreshStationsDiagnostics();

            // Anything that changes the estimated session length keeps the
            // stations-tab summary honest, even though it lives on the other tab.
            _prep.ValueChanged += delegate { RefreshStationsDiagnostics(); };
            _work.ValueChanged += delegate { RefreshStationsDiagnostics(); };
            _rest.ValueChanged += delegate { RefreshStationsDiagnostics(); };
            _rounds.ValueChanged += delegate { RefreshStationsDiagnostics(); };
            _plan.TextChanged += delegate { RefreshStationsDiagnostics(); };
            _restAfterFinal.CheckedChanged += delegate { RefreshStationsDiagnostics(); };
            _useMoveTime.CheckedChanged += delegate { RefreshStationsDiagnostics(); };
            _moveSeconds.ValueChanged += delegate { RefreshStationsDiagnostics(); };

            UpdatePlanPreview();

            Button[] presetButtons = new[]
            {
                _presetButton1, _presetButton2, _presetButton3, _presetButton4, _presetButton5,
                _presetButton6, _presetButton7, _presetButton8, _presetButton9, _presetButton10
            };

            for (int i = 0; i < presetButtons.Length; i++)
            {
                Button button = presetButtons[i];
                if (i < settings.Presets.Count)
                {
                    Preset preset = settings.Presets[i];
                    button.Text = (i + 1) + ": " + preset.Label;
                    button.Tag = preset;
                    button.Visible = true;
                }
                else
                {
                    button.Visible = false;
                }
            }

            }

        /// <summary>
        /// Shows what the plan actually resolves to, so a typo is obvious before you
        /// find out in front of a class.
        /// </summary>
        private void UpdatePlanPreview()
        {
            string text = _plan.Text.Trim();

            if (text.Length == 0)
            {
                IntervalPlan uniform = IntervalPlan.Uniform((int)_work.Value, (int)_rest.Value,
                    Math.Max(1, (int)_rounds.Value));

                _planPreview.Text = "Even session: " + uniform.Summary() + "   ->   "
                    + uniform.RoundCount + " rounds, "
                    + IntervalPlan.FormatDuration(uniform.TotalSeconds((int)_prep.Value, _restAfterFinal.Checked))
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
                + IntervalPlan.FormatDuration(plan.TotalSeconds((int)_prep.Value, _restAfterFinal.Checked))
                + "   (overrides work, rest and rounds above)";
        }

        // ------------------------------------------------------------ stations

        private void LoadStationsGrid(List<Station> stations)
        {
            _stationsGrid.Rows.Clear();
            foreach (Station st in stations)
            {
                _stationsGrid.Rows.Add(st.Name, st.WorkDetail, st.RestDetail, st.Colour);
            }
        }

        /// <summary>
        /// Rows with no name are skipped, so the grid's always-present blank "new
        /// row" at the bottom does not turn into a nameless station.
        /// </summary>
        private List<Station> ReadStationsFromGrid()
        {
            List<Station> list = new List<Station>();

            foreach (DataGridViewRow row in _stationsGrid.Rows)
            {
                if (row.IsNewRow) continue;

                string name = Convert.ToString(row.Cells[0].Value ?? string.Empty).Trim();
                if (name.Length == 0) continue;

                string work = Convert.ToString(row.Cells[1].Value ?? string.Empty).Trim();
                string rest = Convert.ToString(row.Cells[2].Value ?? string.Empty).Trim();
                string colour = Convert.ToString(row.Cells[3].Value ?? string.Empty).Trim();
                list.Add(new Station(name, work, rest, colour));
            }

            return list;
        }

        private void StationsGrid_Changed(object sender, EventArgs e)
        {
            RefreshStationsDiagnostics();
        }

        /// <summary>
        /// Flags a station repeated back-to-back and estimates how long the
        /// resulting session will run, including any move time - both read
        /// straight off whatever is currently in the controls, on either tab.
        /// A station reused further down the list is completely normal (e.g. a
        /// bike used at two different points in a circuit) - it is only a
        /// problem when it would run into itself with nothing in between, which
        /// includes the list wrapping from the last station back to the first.
        /// </summary>
        private void RefreshStationsDiagnostics()
        {
            List<Station> stations = ReadStationsFromGrid();

            List<string> backToBack = new List<string>();
            int count = stations.Count;
            if (count >= 2)
            {
                for (int i = 0; i < count - 1; i++)
                {
                    if (string.Equals(stations[i].Name, stations[i + 1].Name, StringComparison.OrdinalIgnoreCase))
                    {
                        backToBack.Add(stations[i].Name + " (rows " + (i + 1) + "-" + (i + 2) + ")");
                    }
                }

                // Only meaningful once there are 3+ stations - with exactly two,
                // the wrap-around pair is the same pair the loop above already checked.
                if (count > 2 && string.Equals(stations[count - 1].Name, stations[0].Name, StringComparison.OrdinalIgnoreCase))
                {
                    backToBack.Add(stations[0].Name + " (rows " + count + "-1, wraps around)");
                }
            }

            _duplicateWarningLabel.Text = backToBack.Count == 0 ? string.Empty
                : "Back-to-back repeat" + (backToBack.Count > 1 ? "s" : "") + ": " + string.Join(", ", backToBack.ToArray());

            IntervalPlan plan = IntervalPlan.Parse(_plan.Text.Trim());
            if (plan.IsEmpty)
            {
                plan = IntervalPlan.Uniform((int)_work.Value, (int)_rest.Value, Math.Max(1, (int)_rounds.Value));
            }

            bool continuous = (int)_rounds.Value == 0;
            int totalSeconds = plan.TotalSeconds((int)_prep.Value, _restAfterFinal.Checked);
            int moveSeconds = _useMoveTime.Checked ? Math.Max(0, (int)_moveSeconds.Value) : 0;
            int moveCount = 0;

            if (moveSeconds > 0)
            {
                moveCount = continuous ? plan.RoundCount : Math.Max(0, plan.RoundCount - 1);
                totalSeconds += moveSeconds * moveCount;
            }

            StringBuilder summary = new StringBuilder();
            summary.Append(stations.Count).Append(stations.Count == 1 ? " station" : " stations");
            summary.Append("  ·  ").Append(plan.RoundCount).Append(plan.RoundCount == 1 ? " round" : " rounds");
            if (continuous) summary.Append(" (repeating)");
            summary.Append("  ·  about ").Append(IntervalPlan.FormatDuration(totalSeconds)).Append(" total");
            if (moveSeconds > 0)
            {
                summary.Append("  (includes ").Append(moveCount).Append(" × ").Append(moveSeconds).Append("s moves)");
            }

            _stationsSummaryLabel.Text = summary.ToString();
        }

        private void AddStation_Click(object sender, EventArgs e)
        {
            int index = _stationsGrid.Rows.Add();
            _stationsGrid.CurrentCell = _stationsGrid.Rows[index].Cells[0];
            _stationsGrid.BeginEdit(true);
        }

        /// <summary>
        /// Copies the selected station into a new row right after it - the
        /// quickest way to reuse a station (e.g. the same bike) elsewhere in the
        /// program. It lands next to its original for now, which the back-to-back
        /// warning will immediately flag, so the natural next step is to move it
        /// with Up/Down to wherever it is actually meant to sit.
        /// </summary>
        private void DuplicateStation_Click(object sender, EventArgs e)
        {
            DataGridViewRow row = _stationsGrid.CurrentRow;
            if (row == null || row.IsNewRow) return;

            string name = Convert.ToString(row.Cells[0].Value ?? string.Empty).Trim();
            if (name.Length == 0) return;

            object work = row.Cells[1].Value;
            object rest = row.Cells[2].Value;
            object colour = row.Cells[3].Value;

            int insertAt = row.Index + 1;
            _stationsGrid.Rows.Insert(insertAt, name, work, rest, colour);
            _stationsGrid.CurrentCell = _stationsGrid.Rows[insertAt].Cells[0];
            RefreshStationsDiagnostics();
        }

        private void RemoveStation_Click(object sender, EventArgs e)
        {
            if (_stationsGrid.CurrentRow == null || _stationsGrid.CurrentRow.IsNewRow) return;
            _stationsGrid.Rows.Remove(_stationsGrid.CurrentRow);
            RefreshStationsDiagnostics();
        }

        private void MoveStationUp_Click(object sender, EventArgs e)
        {
            MoveStation(-1);
        }

        private void MoveStationDown_Click(object sender, EventArgs e)
        {
            MoveStation(1);
        }

        private void MoveStation(int direction)
        {
            DataGridViewRow row = _stationsGrid.CurrentRow;
            if (row == null || row.IsNewRow) return;

            int from = row.Index;
            int to = from + direction;
            if (to < 0 || to >= _stationsGrid.Rows.Count || _stationsGrid.Rows[to].IsNewRow) return;

            _stationsGrid.Rows.Remove(row);
            _stationsGrid.Rows.Insert(to, row);
            _stationsGrid.CurrentCell = row.Cells[0];
        }

        /// <summary>
        /// Exports the current grid to a standalone file - one station per line,
        /// so a teacher can build a circuit once and reuse it on another computer
        /// or in another session file. Uses the same Name|work|rest|colour grammar
        /// as settings.txt.
        /// </summary>
        private void ExportStations_Click(object sender, EventArgs e)
        {
            List<Station> stations = ReadStationsFromGrid();
            if (stations.Count == 0)
            {
                MessageBox.Show(this, "There are no stations to export yet.", "Export stations",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Station list (*.stations.txt)|*.stations.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = "circuit.stations.txt";
                dialog.Title = "Export station list";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("# GymClock station list - one per line: Name|during work|during rest|colour");
                    sb.AppendLine("# Import this from the Stations tab, on this computer or another one.");
                    foreach (Station st in stations) sb.AppendLine(st.ToFileLine());

                    File.WriteAllText(dialog.FileName, sb.ToString());
                    _saveStatusLabel.Text = "Exported " + stations.Count + " station(s) to " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save that file:" + Environment.NewLine + ex.Message,
                        "Export stations", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportStations_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Station list (*.stations.txt;*.txt)|*.stations.txt;*.txt|All files (*.*)|*.*";
                dialog.Title = "Import station list";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                List<Station> imported = new List<Station>();
                try
                {
                    foreach (string raw in File.ReadAllLines(dialog.FileName))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                        Station station;
                        if (Station.TryParse(line, out station)) imported.Add(station);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not read that file:" + Environment.NewLine + ex.Message,
                        "Import stations", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (imported.Count == 0)
                {
                    MessageBox.Show(this, "No stations were found in that file.", "Import stations",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                List<Station> current = ReadStationsFromGrid();
                List<Station> finalList = imported;

                if (current.Count > 0)
                {
                    DialogResult choice = MessageBox.Show(this,
                        "Add these " + imported.Count + " station(s) to the current list?" + Environment.NewLine
                        + Environment.NewLine + "Choose \"No\" to replace the current list instead.",
                        "Import stations", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                    if (choice == DialogResult.Cancel) return;
                    if (choice == DialogResult.Yes)
                    {
                        finalList = new List<Station>(current);
                        finalList.AddRange(imported);
                    }
                }

                LoadStationsGrid(finalList);
                RefreshStationsDiagnostics();
                _saveStatusLabel.Text = "Imported " + imported.Count + " station(s) from " + dialog.FileName;
            }
        }

        // --------------------------------------------------------------- save

        /// <summary>
        /// Writes straight to settings.txt without closing the dialog, so a teacher
        /// can build up a session and check it is on disk before moving on.
        /// </summary>
        private void Save_Click(object sender, EventArgs e)
        {
            ApplyControlsTo(Result);
            Result.Save();
            _saveStatusLabel.Text = "Saved to " + TimerSettings.FilePath;
        }

        private void Preset_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;

            Preset preset = button.Tag as Preset;
            if (preset == null) return;

            _plan.Text = string.Empty;
            _work.Value = Clamp(preset.Work, _work);
            _rest.Value = Clamp(preset.Rest, _rest);
            if (preset.Rounds > 0) _rounds.Value = Clamp(preset.Rounds, _rounds);
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            Result = _original.Clone();
            ApplyControlsTo(Result);
        }

        /// <summary>Copies every control's current value onto the given settings object.</summary>
        private void ApplyControlsTo(TimerSettings target)
        {
            target.PrepSeconds = (int)_prep.Value;
            target.WorkSeconds = Math.Max(1, (int)_work.Value);
            target.RestSeconds = (int)_rest.Value;
            target.Rounds = (int)_rounds.Value;

            // Keep only a plan that actually parses, so a typo cannot silently
            // replace a working session with nothing.
            string plan = _plan.Text.Trim();
            target.Plan = (plan.Length > 0 && !IntervalPlan.Parse(plan).IsEmpty) ? plan : string.Empty;
            target.Volume = (double)_volume.Value / 100.0;

            target.WorkLabel = string.IsNullOrEmpty(_workLabel.Text.Trim()) ? "WORK" : _workLabel.Text.Trim();
            target.RestLabel = string.IsNullOrEmpty(_restLabel.Text.Trim()) ? "REST" : _restLabel.Text.Trim();
            target.Stations = ReadStationsFromGrid();
            target.ShowStationsTable = _showStationsTable.Checked;
            target.UseStationWording = _useStationWording.Checked;
            target.HighlightCurrentStation = _highlightCurrentStation.Checked;
            target.UseMoveTime = _useMoveTime.Checked;
            target.MoveSeconds = Math.Max(1, (int)_moveSeconds.Value);
            target.MoveMessage = string.IsNullOrEmpty(_moveMessage.Text.Trim())
                ? "Move to the next station" : _moveMessage.Text.Trim();

            string format = _clockFormat.Text.Trim();
            if (format.Length > 0)
            {
                try
                {
                    DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
                    target.ClockFormat = format;
                }
                catch
                {
                    target.ClockFormat = "h:mm:ss tt";
                }
            }

            target.RestAfterFinalRound = _restAfterFinal.Checked;
            target.SoundEnabled = _sound.Checked;
            target.ShowClock = _showClock.Checked;
            target.AlwaysOnTop = _onTop.Checked;
        }

        private static decimal Clamp(int value, NumericUpDown target)
        {
            decimal v = value;
            if (v < target.Minimum) return target.Minimum;
            if (v > target.Maximum) return target.Maximum;
            return v;
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

    internal static class TextBoxExtensions
    {
        /// <summary>
        /// PlaceholderText only exists on newer frameworks, so set it defensively.
        /// </summary>
        public static void PlaceholderTextSafe(this TextBox box, string text)
        {
            try
            {
                System.Reflection.PropertyInfo property = typeof(TextBox).GetProperty("PlaceholderText");
                if (property != null && property.CanWrite) property.SetValue(box, text, null);
            }
            catch { }
        }
    }
}
