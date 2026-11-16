using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace GymClock.Link
{
    /// <summary>
    /// "Link displays" (L): a guided setup that does the work itself.
    ///
    ///   Main PC:        one click. The link starts, the code is made, Bluetooth is used if the PC has it.
    ///   Second screen:  type the 4-digit code. It finds the main PC (network, then Bluetooth),
    ///                   remembers the route for next time and runs a link test.
    ///
    /// Every choice takes effect at once (through <c>apply</c>), and the window shows live what the
    /// link is doing and what to check if it cannot connect. The full guide is one click away (Help).
    /// </summary>
    public sealed class LinkSetupForm : Form
    {
        private enum Line { Good, Waiting, Problem, Off }

        private readonly TimerSettings _settings;
        private readonly Func<LinkStatus> _getStatus;
        private readonly Action<Action<TimerSettings>> _apply;
        private readonly Timer _refresh = new Timer();

        private readonly Panel _choosePage = new Panel();
        private readonly Panel _mainPage = new Panel();
        private readonly Panel _followPage = new Panel();

        // ---- main PC page
        private readonly Label _codeBig = new Label();
        private readonly Label _mainScreens = new Label();
        private readonly Label _mainNetwork = new Label();
        private readonly Label _mainBluetooth = new Label();
        private readonly Label _mainTip = new Label();

        // ---- second screen page
        private readonly TextBox _codeBox = new TextBox();
        private readonly Label _followHeadline = new Label();
        private readonly Label _followDetail = new Label();
        private readonly Label _followNetwork = new Label();
        private readonly Label _followBluetooth = new Label();
        private readonly CheckBox _moreOptions = new CheckBox();
        private readonly TableLayoutPanel _optionsPanel = new TableLayoutPanel();
        private readonly CheckBox _sound = new CheckBox();
        private readonly TextBox _stationsFrom = new TextBox();
        private readonly TextBox _stationsTo = new TextBox();
        private readonly TextBox _address = new TextBox();
        private readonly ComboBox _bluetoothDevice = new ComboBox();
        private readonly Label _optionsError = new Label();

        private readonly Button _offButton = new Button();
        private readonly Font _titleFont;
        private readonly Font _codeFont;
        private readonly Font _headlineFont;
        private readonly int _textWidth;

        // Environment checks are slow-ish (Bluetooth device list), so they are refreshed every few seconds.
        private DateTime _environmentAt = DateTime.MinValue;
        private List<LinkNetwork> _networks = new List<LinkNetwork>();
        private bool _hasBluetooth;
        private List<BluetoothDevice> _pairedComputers = new List<BluetoothDevice>();
        private DateTime _pageShownAt = DateTime.Now;
        private bool _loadingOptions;

        /// <param name="settings">This PC's own settings (read for the starting values).</param>
        /// <param name="getStatus">The link's state right now.</param>
        /// <param name="apply">Changes this PC's link settings, saves them and restarts the link.</param>
        public LinkSetupForm(TimerSettings settings, Func<LinkStatus> getStatus, Action<Action<TimerSettings>> apply)
        {
            _settings = settings;
            _getStatus = getStatus;
            _apply = apply;

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
            Padding = new Padding(16);

            _titleFont = new Font(Font.FontFamily, Font.Size + 5f, FontStyle.Bold);
            _codeFont = new Font(Font.FontFamily, Font.Size + 22f, FontStyle.Bold);
            _headlineFont = new Font(Font.FontFamily, Font.Size + 2f, FontStyle.Bold);
            _textWidth = Font.Height * 30;

            TableLayoutPanel root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };

            BuildChoosePage();
            BuildMainPage();
            BuildFollowPage();
            foreach (Panel page in new[] { _choosePage, _mainPage, _followPage })
            {
                page.AutoSize = true;
                page.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                page.Margin = new Padding(0);
                root.Controls.Add(page);
            }

            // ---- always there: Help on the left, Close on the right
            TableLayoutPanel bottom = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button help = new Button { Text = "Help and setup guide", AutoSize = true };
            help.Click += delegate { ShowHelp(null); };
            Button close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(Font.Height * 5, 0), DialogResult = DialogResult.OK };
            bottom.Controls.Add(help, 0, 0);
            bottom.Controls.Add(close, 2, 0);
            root.Controls.Add(bottom);

            Controls.Add(root);
            CancelButton = close;

            _refresh.Interval = 400;
            _refresh.Tick += delegate { RefreshStatus(); };

            if (settings.IsLinkLeader) ShowPage(_mainPage);
            else if (settings.IsLinkFollower) ShowPage(_followPage);
            else ShowPage(_choosePage);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshStatus();
            _refresh.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _refresh.Stop();
            ApplyOptions(); // anything typed under "More options" is not lost by closing
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refresh.Dispose();
                _titleFont.Dispose();
                _codeFont.Dispose();
                _headlineFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ building blocks

        private Label Title(string text)
        {
            return new Label { Text = text, UseMnemonic = false, AutoSize = true, Font = _titleFont, Margin = new Padding(3, 0, 3, 8) };
        }

        private Label Paragraph(string text)
        {
            return new Label { Text = text, UseMnemonic = false, AutoSize = true, MaximumSize = new Size(_textWidth, 0), Margin = new Padding(3, 0, 3, 8) };
        }

        private Label Hint(string text)
        {
            return new Label
            {
                Text = text, UseMnemonic = false, AutoSize = true, ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(_textWidth - Font.Height * 2, 0), Margin = new Padding(3, 0, 3, 6)
            };
        }

        private void SetUpLine(Label label)
        {
            label.AutoSize = true;
            label.UseMnemonic = false;
            label.MaximumSize = new Size(_textWidth, 0);
            label.Margin = new Padding(3, 4, 3, 0);
        }

        private static void SetLine(Label label, Line state, string text)
        {
            string mark;
            Color colour;
            switch (state)
            {
                case Line.Good: mark = "✔  "; colour = Color.FromArgb(15, 110, 60); break;
                case Line.Problem: mark = "✖  "; colour = Color.FromArgb(170, 40, 30); break;
                case Line.Waiting: mark = "●  "; colour = Color.FromArgb(150, 95, 10); break;
                default: mark = "–  "; colour = SystemColors.GrayText; break;
            }
            string full = mark + text;
            if (label.Text != full) label.Text = full;
            if (label.ForeColor != colour) label.ForeColor = colour;
        }

        private static TableLayoutPanel Column()
        {
            return new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = new Padding(0) };
        }

        private Button BigChoice(string text, string detail, EventHandler click)
        {
            Button button = new Button
            {
                Text = text + Environment.NewLine + detail,
                UseMnemonic = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 6, 10, 6),
                Size = new Size(_textWidth, Font.Height * 4),
                Margin = new Padding(3, 6, 3, 2)
            };
            button.Click += click;
            return button;
        }

        private void ShowPage(Panel page)
        {
            _choosePage.Visible = page == _choosePage;
            _mainPage.Visible = page == _mainPage;
            _followPage.Visible = page == _followPage;
            _pageShownAt = DateTime.Now;
            _offButton.Visible = !string.Equals(_getStatus().Role, "off", StringComparison.OrdinalIgnoreCase);
            if (page == _followPage)
            {
                LoadOptions();
                _codeBox.Focus();
            }
            RefreshStatus();
        }

        private void ShowHelp(string topic)
        {
            if (topic == null)
            {
                // Open at the page that fits what is on screen.
                LinkStatus status = _getStatus();
                topic = LinkHelp.Start;
                if (_followPage.Visible && status.Role == "follow" && status.FollowerState != FollowerStatus.Connected) topic = LinkHelp.Trouble;
                else if (_followPage.Visible && status.FollowerState == FollowerStatus.Connected) topic = LinkHelp.SecondScreen;
            }
            using (LinkHelpForm help = new LinkHelpForm(topic)) help.ShowDialog(this);
            _environmentAt = DateTime.MinValue; // the PCs may have just been paired
        }

        private LinkLabel HelpLink(string text, string topic)
        {
            LinkLabel link = new LinkLabel { Text = text, AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 6, 3, 0) };
            link.LinkClicked += delegate { ShowHelp(topic); };
            return link;
        }

        // ------------------------------------------------------------------ page: what is this PC?

        private void BuildChoosePage()
        {
            TableLayoutPanel column = Column();
            column.Controls.Add(Title("Link displays"));
            column.Controls.Add(Paragraph("Show one session on two or more PCs, in step. One PC runs the session; the others follow it. What is this PC?"));

            column.Controls.Add(BigChoice("This is the MAIN PC",
                "It runs the session. One click and the link is on.", delegate { BecomeMainPc(); }));
            column.Controls.Add(BigChoice("This is a SECOND SCREEN",
                "It follows the main PC. You only type the link code.", delegate { ShowPage(_followPage); }));

            _offButton.Text = "Turn linking off";
            _offButton.AutoSize = true;
            _offButton.Margin = new Padding(3, 12, 3, 0);
            _offButton.Click += delegate
            {
                _apply(s => s.LinkRole = "off");
                ShowPage(_choosePage);
            };
            column.Controls.Add(_offButton);
            _choosePage.Controls.Add(column);
        }

        /// <summary>"Turn linking off", on the main PC and second screen pages: one click, back to an unlinked PC.</summary>
        private Button TurnOffButton()
        {
            Button off = new Button { Text = "Turn linking off", AutoSize = true };
            off.Click += delegate
            {
                _apply(s => s.LinkRole = "off");
                _codeBox.Text = string.Empty;
                ShowPage(_choosePage);
            };
            return off;
        }

        private void BecomeMainPc()
        {
            _apply(s =>
            {
                s.LinkRole = "lead";
                if (string.IsNullOrWhiteSpace(s.LinkCode) || s.LinkCode.Trim().Length < 4) s.LinkCode = LinkProtocol.NewCode();
            });
            ShowPage(_mainPage);
        }

        // ------------------------------------------------------------------ page: main PC

        private void BuildMainPage()
        {
            TableLayoutPanel column = Column();
            column.Controls.Add(Title("This is the MAIN PC"));
            column.Controls.Add(new Label { Text = "Link code", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 0) });

            _codeBig.AutoSize = true;
            _codeBig.UseMnemonic = false;
            _codeBig.Font = _codeFont;
            _codeBig.Margin = new Padding(0, 0, 3, 4);
            column.Controls.Add(_codeBig);

            column.Controls.Add(Paragraph("On each second PC: press L, choose \"This is a SECOND SCREEN\" and type this code. "
                + "That is all - this PC is ready and waiting."));

            foreach (Label line in new[] { _mainScreens, _mainNetwork, _mainBluetooth }) { SetUpLine(line); column.Controls.Add(line); }

            _mainTip.AutoSize = true;
            _mainTip.UseMnemonic = false;
            _mainTip.MaximumSize = new Size(_textWidth, 0);
            _mainTip.Margin = new Padding(3, 10, 3, 0);
            _mainTip.Visible = false;
            column.Controls.Add(_mainTip);
            column.Controls.Add(HelpLink("Second screen not connecting? See what to check", LinkHelp.Trouble));

            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
            Button newCode = new Button { Text = "New code", AutoSize = true };
            newCode.Click += delegate
            {
                _apply(s => s.LinkCode = LinkProtocol.NewCode());
                RefreshStatus();
            };
            Button change = new Button { Text = "Change what this PC is...", AutoSize = true };
            change.Click += delegate { ShowPage(_choosePage); };
            buttons.Controls.Add(newCode);
            buttons.Controls.Add(change);
            buttons.Controls.Add(TurnOffButton());
            column.Controls.Add(buttons);
            _mainPage.Controls.Add(column);
        }

        // ------------------------------------------------------------------ page: second screen

        private void BuildFollowPage()
        {
            TableLayoutPanel column = Column();
            column.Controls.Add(Title("This is a SECOND SCREEN"));

            FlowLayoutPanel codeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            codeRow.Controls.Add(new Label { Text = "Link code from the main PC:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) });
            _codeBox.Font = _headlineFont;
            _codeBox.Width = Font.Height * 5;
            _codeBox.MaxLength = 8;
            _codeBox.TextAlign = HorizontalAlignment.Center;
            _codeBox.TextChanged += delegate { CodeTyped(false); };
            _codeBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { CodeTyped(true); e.SuppressKeyPress = true; } };
            codeRow.Controls.Add(_codeBox);
            Button link = new Button { Text = "Link", AutoSize = true, Margin = new Padding(8, 3, 3, 3) };
            link.Click += delegate { CodeTyped(true); };
            codeRow.Controls.Add(link);
            column.Controls.Add(codeRow);
            column.Controls.Add(Hint("The code is at the bottom of the main PC's screen. Linking starts as soon as it is typed."));

            _followHeadline.AutoSize = true;
            _followHeadline.UseMnemonic = false;
            _followHeadline.Font = _headlineFont;
            _followHeadline.MaximumSize = new Size(_textWidth, 0);
            _followHeadline.Margin = new Padding(3, 8, 3, 2);
            column.Controls.Add(_followHeadline);

            _followDetail.AutoSize = true;
            _followDetail.UseMnemonic = false;
            _followDetail.MaximumSize = new Size(_textWidth, 0);
            _followDetail.Margin = new Padding(3, 0, 3, 6);
            column.Controls.Add(_followDetail);

            foreach (Label line in new[] { _followNetwork, _followBluetooth }) { SetUpLine(line); column.Controls.Add(line); }
            column.Controls.Add(HelpLink("Not connecting? See what to check", LinkHelp.Trouble));

            // ---- more options (none are needed to get linked)
            _moreOptions.Text = "More options";
            _moreOptions.AutoSize = true;
            _moreOptions.Appearance = Appearance.Button;
            _moreOptions.Margin = new Padding(3, 14, 3, 4);
            _moreOptions.CheckedChanged += delegate { _optionsPanel.Visible = _moreOptions.Checked; };

            _optionsPanel.AutoSize = true;
            _optionsPanel.ColumnCount = 2;
            _optionsPanel.Visible = false;
            _optionsPanel.Margin = new Padding(3, 0, 3, 0);

            _sound.Text = "Play the sounds on this screen too";
            _sound.AutoSize = true;
            _optionsPanel.Controls.Add(_sound, 0, 0);
            _optionsPanel.SetColumnSpan(_sound, 2);

            FlowLayoutPanel stations = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            _stationsFrom.Width = Font.Height * 3;
            _stationsTo.Width = Font.Height * 3;
            stations.Controls.Add(FieldLabel("Show stations"));
            stations.Controls.Add(_stationsFrom);
            stations.Controls.Add(FieldLabel("to"));
            stations.Controls.Add(_stationsTo);
            _optionsPanel.Controls.Add(stations, 0, 1);
            _optionsPanel.SetColumnSpan(stations, 2);
            Label stationsHint = Hint("Only for programs where each station has its own clock (Parallel). Blank = all stations.");
            _optionsPanel.Controls.Add(stationsHint, 0, 2);
            _optionsPanel.SetColumnSpan(stationsHint, 2);

            _address.Width = Font.Height * 11;
            _optionsPanel.Controls.Add(FieldLabel("Main PC's address:"), 0, 3);
            _optionsPanel.Controls.Add(_address, 1, 3);
            Label addressHint = Hint("Only if the main PC cannot be found automatically. It is shown in the main PC's Link displays window.");
            _optionsPanel.Controls.Add(addressHint, 0, 4);
            _optionsPanel.SetColumnSpan(addressHint, 2);

            _bluetoothDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            _bluetoothDevice.Width = Font.Height * 14;
            _optionsPanel.Controls.Add(FieldLabel("Bluetooth main PC:"), 0, 5);
            _optionsPanel.Controls.Add(_bluetoothDevice, 1, 5);

            _optionsError.AutoSize = true;
            _optionsError.ForeColor = Color.FromArgb(170, 40, 30);
            _optionsError.MaximumSize = new Size(_textWidth, 0);
            _optionsPanel.Controls.Add(_optionsError, 0, 6);
            _optionsPanel.SetColumnSpan(_optionsError, 2);

            Button applyOptions = new Button { Text = "Apply options", AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
            applyOptions.Click += delegate { ApplyOptions(); };
            _optionsPanel.Controls.Add(applyOptions, 0, 7);

            column.Controls.Add(_moreOptions);
            column.Controls.Add(_optionsPanel);

            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
            Button change = new Button { Text = "Change what this PC is...", AutoSize = true };
            change.Click += delegate { ShowPage(_choosePage); };
            buttons.Controls.Add(change);
            buttons.Controls.Add(TurnOffButton());
            column.Controls.Add(buttons);
            _followPage.Controls.Add(column);
        }

        private static Label FieldLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) };
        }

        /// <summary>The code box changed (or Link/Enter was pressed): four digits is a complete code, so start linking.</summary>
        private void CodeTyped(bool force)
        {
            if (_loadingOptions) return;
            string code = _codeBox.Text.Trim();
            bool complete = code.Length == 4 && code.All(char.IsDigit);
            if (!complete && !(force && code.Length >= 4)) return;

            LinkStatus status = _getStatus();
            if (status.Role == "follow" && status.Code == code) return; // already linking with this code

            _apply(s =>
            {
                s.LinkRole = "follow";
                s.LinkCode = code;
            });
            RefreshStatus();
        }

        private void LoadOptions()
        {
            _loadingOptions = true;
            try
            {
                if (_settings.IsLinkFollower && _codeBox.Text.Length == 0) _codeBox.Text = _settings.LinkCode;
                _sound.Checked = _settings.LinkSound;
                _stationsFrom.Text = _settings.LinkStationsFrom > 0 ? _settings.LinkStationsFrom.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _stationsTo.Text = _settings.LinkStationsTo > 0 ? _settings.LinkStationsTo.ToString(CultureInfo.InvariantCulture) : string.Empty;
                _address.Text = _settings.LinkAddress;
                FillBluetoothDevices();
            }
            finally
            {
                _loadingOptions = false;
            }
        }

        private void FillBluetoothDevices()
        {
            ulong wanted = 0;
            BluetoothDevice current = _bluetoothDevice.SelectedItem as BluetoothDevice;
            if (current != null) wanted = current.Address;
            else if (_bluetoothDevice.Items.Count == 0) BluetoothLink.TryParseAddress(_settings.LinkBluetoothAddress, out wanted);

            _bluetoothDevice.Items.Clear();
            _bluetoothDevice.Items.Add("Automatic - any paired computer");
            _bluetoothDevice.SelectedIndex = 0;
            foreach (BluetoothDevice device in LinkEnvironment.PairedComputers())
            {
                _bluetoothDevice.Items.Add(device);
                if (device.Address == wanted) _bluetoothDevice.SelectedItem = device;
            }
            if (wanted != 0 && _bluetoothDevice.SelectedIndex == 0)
            {
                // Keep a saved choice even if Windows does not list it right now.
                BluetoothDevice saved = new BluetoothDevice { Address = wanted, Name = _settings.LinkBluetoothName, IsComputer = true };
                _bluetoothDevice.Items.Add(saved);
                _bluetoothDevice.SelectedItem = saved;
            }
        }

        /// <summary>Saves "More options" if anything in it changed. Returns false if something typed is not usable.</summary>
        private bool ApplyOptions()
        {
            if (!_followPage.Visible && !_settings.IsLinkFollower) return true;
            if (_bluetoothDevice.Items.Count == 0) return true; // the page was never opened

            int from = 0, to = 0;
            bool fromOk = _stationsFrom.Text.Trim().Length == 0 || (int.TryParse(_stationsFrom.Text.Trim(), out from) && from >= 1 && from <= 99);
            bool toOk = _stationsTo.Text.Trim().Length == 0 || (int.TryParse(_stationsTo.Text.Trim(), out to) && to >= 1 && to <= 99);
            if (!fromOk || !toOk || (from > 0 && to > 0 && from > to))
            {
                _optionsError.Text = "Stations must be numbers, e.g. 3 to 4, or left blank to show them all.";
                return false;
            }
            _optionsError.Text = string.Empty;

            BluetoothDevice device = _bluetoothDevice.SelectedItem as BluetoothDevice;
            string bluetoothAddress = device != null ? device.AddressText : string.Empty;
            string bluetoothName = device != null ? (device.Name ?? string.Empty) : string.Empty;
            string address = _address.Text.Trim();
            bool sound = _sound.Checked;

            bool changed = _settings.LinkSound != sound || _settings.LinkStationsFrom != from || _settings.LinkStationsTo != to
                || !string.Equals(_settings.LinkAddress, address, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_settings.LinkBluetoothAddress, bluetoothAddress, StringComparison.OrdinalIgnoreCase);
            if (!changed) return true;

            _apply(s =>
            {
                s.LinkSound = sound;
                s.LinkStationsFrom = from;
                s.LinkStationsTo = to;
                s.LinkAddress = address;
                s.LinkBluetoothAddress = bluetoothAddress;
                s.LinkBluetoothName = bluetoothName;
            });
            return true;
        }

        // ------------------------------------------------------------------ live status

        private void RefreshEnvironment()
        {
            if ((DateTime.Now - _environmentAt).TotalSeconds < 3) return;
            _environmentAt = DateTime.Now;
            _networks = LinkEnvironment.Networks();
            _hasBluetooth = LinkEnvironment.HasBluetooth();
            _pairedComputers = _hasBluetooth ? LinkEnvironment.PairedComputers() : new List<BluetoothDevice>();
        }

        private void RefreshStatus()
        {
            if (IsDisposed) return;
            RefreshEnvironment();
            LinkStatus status = _getStatus();

            if (_mainPage.Visible) RefreshMainPage(status);
            if (_followPage.Visible) RefreshFollowPage(status);
        }

        private string PairedNames()
        {
            return string.Join(", ", _pairedComputers.Select(d => string.IsNullOrWhiteSpace(d.Name) ? d.AddressText : d.Name));
        }

        private void RefreshMainPage(LinkStatus status)
        {
            string spaced = string.Join(" ", (status.Code ?? string.Empty).ToCharArray());
            if (_codeBig.Text != spaced) _codeBig.Text = spaced;

            if (!status.LeaderRunning)
            {
                SetLine(_mainScreens, Line.Problem, "The link could not start. "
                    + (status.LeaderError.Length > 0 ? status.LeaderError : "Close any other copy of Gym Interval Clock and try again."));
            }
            else if (status.Screens.Count == 0)
            {
                SetLine(_mainScreens, Line.Waiting, "Second screens: none yet - waiting...");
            }
            else
            {
                SetLine(_mainScreens, Line.Good, "Linked: " + string.Join(", ", status.Screens));
            }

            List<LinkNetwork> real = _networks.Where(n => !n.IsBluetooth).ToList();
            if (_networks.Count == 0)
            {
                SetLine(_mainNetwork, _hasBluetooth && _pairedComputers.Count > 0 ? Line.Off : Line.Problem,
                    "Network: this PC is not connected to a network");
            }
            else
            {
                SetLine(_mainNetwork, Line.Good, "Network: " + LinkEnvironment.DescribeNetworks(_networks)
                    + Environment.NewLine + "     This PC's address: " + string.Join("  or  ", (real.Count > 0 ? real : _networks).Select(n => n.Address)));
            }

            if (!_hasBluetooth) SetLine(_mainBluetooth, Line.Off, "Bluetooth: not available on this PC (not needed if the PCs share a network)");
            else if (!status.BluetoothListening && status.LeaderRunning) SetLine(_mainBluetooth, Line.Off, "Bluetooth: could not be started");
            else if (_pairedComputers.Count == 0) SetLine(_mainBluetooth, Line.Off, "Bluetooth: ready, but no computer is paired with this PC yet");
            else SetLine(_mainBluetooth, Line.Good, "Bluetooth: ready (paired with " + PairedNames() + ")");

            // After a while with nobody connecting, say what to check - without the teacher having to ask.
            bool stuck = status.LeaderRunning && status.Screens.Count == 0 && (DateTime.Now - _pageShownAt).TotalSeconds > 25;
            if (stuck)
            {
                _mainTip.Text = "Nothing yet? On the second PC press L, choose \"This is a SECOND SCREEN\" and type "
                    + status.Code + ". Both PCs must be on the same network"
                    + (_hasBluetooth ? " (or paired by Bluetooth)" : string.Empty)
                    + ". If Windows asked about the firewall, choose Allow.";
            }
            if (_mainTip.Visible != stuck) _mainTip.Visible = stuck;
        }

        private void RefreshFollowPage(LinkStatus status)
        {
            string headline;
            string detail;
            Color colour = SystemColors.ControlText;
            Color good = Color.FromArgb(15, 110, 60), bad = Color.FromArgb(170, 40, 30), waiting = Color.FromArgb(150, 95, 10);

            if (status.Role != "follow")
            {
                headline = "Type the link code to start.";
                detail = string.Empty;
            }
            else
            {
                switch (status.FollowerState)
                {
                    case FollowerStatus.Connected:
                        colour = good;
                        headline = "Linked to " + (status.LeaderName.Length > 0 ? status.LeaderName : "the main PC")
                            + (status.Via.Length > 0 ? " over " + (status.Via == "network" ? "the network" : status.Via) : string.Empty);
                        detail = status.Quality.Describe();
                        if (status.SessionRunning && status.ClockMatched)
                        {
                            detail += Environment.NewLine + "Now: in step within "
                                + (long)Math.Ceiling(status.DriftPeakMs + (status.Quality.PingMs / 2.0)) + " ms.";
                        }
                        detail += Environment.NewLine + "This screen now shows the main PC's session. You can close this window.";
                        break;

                    case FollowerStatus.WrongCode:
                        colour = bad;
                        headline = "That code was not accepted.";
                        detail = "Check the link code at the bottom of the main PC's screen and type it again.";
                        break;

                    case FollowerStatus.Reconnecting:
                        colour = waiting;
                        headline = "Reconnecting to the main PC...";
                        detail = (status.Activity.Length > 0 ? status.Activity + Environment.NewLine : string.Empty)
                            + "This screen keeps counting by itself and catches up when the link returns.";
                        break;

                    default:
                        colour = waiting;
                        headline = "Looking for the main PC...";
                        detail = status.Activity;
                        if (status.FailedRounds >= 2)
                        {
                            colour = bad;
                            headline = "The main PC has not been found yet.";
                            detail = "Still trying. Check that:" + Environment.NewLine
                                + "  •  Gym Interval Clock on the other PC is set as the MAIN PC" + Environment.NewLine
                                + "  •  both PCs are on the same network" + (_hasBluetooth ? ", or paired by Bluetooth" : string.Empty) + Environment.NewLine
                                + "  •  Windows was allowed through the firewall on both PCs" + Environment.NewLine
                                + "Or open \"More options\" and type the main PC's address.";
                        }
                        break;
                }
            }

            if (_followHeadline.Text != headline) _followHeadline.Text = headline;
            if (_followHeadline.ForeColor != colour) _followHeadline.ForeColor = colour;
            if (_followDetail.Text != detail) _followDetail.Text = detail;

            if (_networks.Count == 0) SetLine(_followNetwork, Line.Problem, "Network: this PC is not connected to a network");
            else SetLine(_followNetwork, Line.Good, "Network: " + LinkEnvironment.DescribeNetworks(_networks));

            if (!_hasBluetooth) SetLine(_followBluetooth, Line.Off, "Bluetooth: not available on this PC (not needed if the PCs share a network)");
            else if (_pairedComputers.Count == 0) SetLine(_followBluetooth, Line.Off, "Bluetooth: on, but no computer is paired with this PC yet");
            else SetLine(_followBluetooth, Line.Good, "Bluetooth: paired with " + PairedNames() + " - used automatically if the network fails");
        }
    }
}
