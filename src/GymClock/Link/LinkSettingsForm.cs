using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace GymClock.Link
{
    /// <summary>
    /// "Link displays" (L): choose whether this PC is the main PC, a second screen, or not linked,
    /// and enter the link code. Laid out with auto-sizing panels so it scales with Windows text size.
    /// </summary>
    public sealed class LinkSettingsForm : Form
    {
        private readonly RadioButton _off = new RadioButton();
        private readonly RadioButton _lead = new RadioButton();
        private readonly RadioButton _follow = new RadioButton();

        private readonly TextBox _leadCode = new TextBox();
        private readonly Label _leadAddresses = new Label();

        private readonly TextBox _followCode = new TextBox();
        private readonly TextBox _address = new TextBox();
        private readonly CheckBox _sound = new CheckBox();
        private readonly TextBox _stationsFrom = new TextBox();
        private readonly TextBox _stationsTo = new TextBox();

        private readonly TableLayoutPanel _leadPanel = new TableLayoutPanel();
        private readonly TableLayoutPanel _followPanel = new TableLayoutPanel();
        private readonly Label _error = new Label();

        public string Role { get; private set; }
        public string Code { get; private set; }
        public string Address { get; private set; }
        public bool FollowerSound { get; private set; }
        public int StationsFrom { get; private set; }
        public int StationsTo { get; private set; }

        public LinkSettingsForm(TimerSettings settings)
        {
            Text = "Link displays";
            Icon = AppIconProvider.GetIcon();
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true; // the clock window is normally always-on-top
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14);

            TableLayoutPanel root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };

            root.Controls.Add(Paragraph("Show one session on two or more PCs, in step. The main PC runs the session; "
                + "second screens only display it. All PCs must be on the same network."));

            _off.Text = "Not linked";
            _lead.Text = "This is the MAIN PC (it runs the session)";
            _follow.Text = "This is a SECOND SCREEN (it follows the main PC)";
            foreach (RadioButton radio in new[] { _off, _lead, _follow })
            {
                radio.AutoSize = true;
                radio.Margin = new Padding(3, 8, 3, 2);
                radio.CheckedChanged += delegate { UpdateEnabled(); };
            }

            // ---- main PC
            _leadPanel.AutoSize = true;
            _leadPanel.ColumnCount = 3;
            _leadPanel.Margin = new Padding(24, 0, 3, 0);
            _leadCode.Width = 80;
            _leadCode.MaxLength = 8;
            Button newCode = new Button { Text = "New code", AutoSize = true };
            newCode.Click += delegate { _leadCode.Text = LinkProtocol.NewCode(); };
            _leadPanel.Controls.Add(FieldLabel("Link code:"), 0, 0);
            _leadPanel.Controls.Add(_leadCode, 1, 0);
            _leadPanel.Controls.Add(newCode, 2, 0);
            _leadAddresses.AutoSize = true;
            _leadAddresses.ForeColor = SystemColors.GrayText;
            _leadAddresses.Margin = new Padding(3, 4, 3, 0);
            _leadPanel.Controls.Add(_leadAddresses, 0, 1);
            _leadPanel.SetColumnSpan(_leadAddresses, 3);

            // ---- second screen
            _followPanel.AutoSize = true;
            _followPanel.ColumnCount = 2;
            _followPanel.Margin = new Padding(24, 0, 3, 0);
            _followCode.Width = 80;
            _followCode.MaxLength = 8;
            _address.Width = 180;
            _followPanel.Controls.Add(FieldLabel("Link code (shown on the main PC):"), 0, 0);
            _followPanel.Controls.Add(_followCode, 1, 0);
            _followPanel.Controls.Add(FieldLabel("Main PC's address:"), 0, 1);
            _followPanel.Controls.Add(_address, 1, 1);
            Label addressHelp = Hint("Leave the address blank to find the main PC automatically.");
            _followPanel.Controls.Add(addressHelp, 0, 2);
            _followPanel.SetColumnSpan(addressHelp, 2);

            _sound.Text = "Play the sounds on this screen too";
            _sound.AutoSize = true;
            _sound.Margin = new Padding(3, 8, 3, 2);
            _followPanel.Controls.Add(_sound, 0, 3);
            _followPanel.SetColumnSpan(_sound, 2);

            FlowLayoutPanel stations = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            _stationsFrom.Width = 44;
            _stationsTo.Width = 44;
            stations.Controls.Add(FieldLabel("Show stations"));
            stations.Controls.Add(_stationsFrom);
            stations.Controls.Add(FieldLabel("to"));
            stations.Controls.Add(_stationsTo);
            _followPanel.Controls.Add(stations, 0, 4);
            _followPanel.SetColumnSpan(stations, 2);
            Label stationsHelp = Hint("Only for programs where each station has its own clock (Parallel). Leave blank to show them all.");
            _followPanel.Controls.Add(stationsHelp, 0, 5);
            _followPanel.SetColumnSpan(stationsHelp, 2);

            root.Controls.Add(_off);
            root.Controls.Add(_lead);
            root.Controls.Add(_leadPanel);
            root.Controls.Add(_follow);
            root.Controls.Add(_followPanel);

            _error.AutoSize = true;
            _error.ForeColor = Color.FromArgb(170, 40, 30);
            _error.Margin = new Padding(3, 8, 3, 0);
            _error.MaximumSize = new Size(Font.Height * 28, 0);
            root.Controls.Add(_error);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0)
            };
            Button cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(90, 0) };
            Button ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(90, 0) };
            ok.Click += Ok_Click;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;

            // ---- current values
            _leadCode.Text = settings.LinkCode;
            _followCode.Text = settings.LinkCode;
            _address.Text = settings.LinkAddress;
            _sound.Checked = settings.LinkSound;
            _stationsFrom.Text = settings.LinkStationsFrom > 0 ? settings.LinkStationsFrom.ToString(CultureInfo.InvariantCulture) : string.Empty;
            _stationsTo.Text = settings.LinkStationsTo > 0 ? settings.LinkStationsTo.ToString(CultureInfo.InvariantCulture) : string.Empty;

            string addresses = string.Join(",  ", LinkProtocol.LocalAddresses().Select(a => a.Address.ToString()));
            _leadAddresses.Text = addresses.Length > 0
                ? "This PC's address: " + addresses + Environment.NewLine + "(only needed if a second screen can't find it automatically)"
                : "This PC is not connected to a network.";

            if (settings.IsLinkLeader) _lead.Checked = true;
            else if (settings.IsLinkFollower) _follow.Checked = true;
            else _off.Checked = true;
            UpdateEnabled();
        }

        private Label Paragraph(string text)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(Font.Height * 28, 0), Margin = new Padding(3, 0, 3, 6) };
        }

        private Label Hint(string text)
        {
            return new Label
            {
                Text = text, AutoSize = true, ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(Font.Height * 26, 0), Margin = new Padding(3, 2, 3, 0)
            };
        }

        private static Label FieldLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) };
        }

        private void UpdateEnabled()
        {
            _leadPanel.Enabled = _lead.Checked;
            _followPanel.Enabled = _follow.Checked;
            if (_lead.Checked && _leadCode.Text.Trim().Length == 0) _leadCode.Text = LinkProtocol.NewCode();
            _error.Text = string.Empty;
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            string role = _lead.Checked ? "lead" : _follow.Checked ? "follow" : "off";
            string code = (_lead.Checked ? _leadCode.Text : _followCode.Text).Trim();

            if (role != "off" && code.Length < 4)
            {
                _error.Text = role == "lead"
                    ? "Enter a link code of at least 4 digits (or press New code)."
                    : "Enter the link code shown at the bottom of the main PC's screen.";
                return;
            }

            int from = 0, to = 0;
            if (role == "follow")
            {
                bool fromOk = _stationsFrom.Text.Trim().Length == 0 || (int.TryParse(_stationsFrom.Text.Trim(), out from) && from >= 1 && from <= 99);
                bool toOk = _stationsTo.Text.Trim().Length == 0 || (int.TryParse(_stationsTo.Text.Trim(), out to) && to >= 1 && to <= 99);
                if (!fromOk || !toOk || (from > 0 && to > 0 && from > to))
                {
                    _error.Text = "Stations must be numbers, e.g. 3 to 4, or left blank to show them all.";
                    return;
                }
            }

            Role = role;
            Code = code;
            Address = _address.Text.Trim();
            FollowerSound = _sound.Checked;
            StationsFrom = from;
            StationsTo = to;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
