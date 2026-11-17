using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using GymClock.Link;

namespace GymClock.Wpf
{
    public enum LineState { Good, Waiting, Problem, Off }

    /// <summary>One status line ("✔  Network: Wi-Fi ..."), coloured by its state.</summary>
    public sealed class StatusLine : ObservableObject
    {
        private string _text = string.Empty;
        private LineState _state = LineState.Off;

        public string Text { get { return _text; } private set { Set(ref _text, value); } }
        public LineState State { get { return _state; } private set { Set(ref _state, value); } }

        public void Show(LineState state, string text)
        {
            string mark;
            switch (state)
            {
                case LineState.Good: mark = "✔  "; break;
                case LineState.Problem: mark = "✖  "; break;
                case LineState.Waiting: mark = "●  "; break;
                default: mark = "–  "; break;
            }
            State = state;
            Text = mark + text;
        }
    }

    /// <summary>
    /// The Link displays page (build guide 7.3, mockup 8): a direct port of LinkSetupForm, with
    /// its three states (what is this PC? / main PC / second screen), its 400 ms refresh, its
    /// wording, the four-digit auto-link, "More options", and the tip after 25 seconds of nobody
    /// connecting. The built-in guide (LinkHelp) opens in place of the status column.
    /// </summary>
    public sealed class LinkViewModel : ObservableObject
    {
        public enum LinkPage { Choose, Main, Follow }

        private readonly TimerSettings _settings;
        private readonly Func<LinkStatus> _getStatus;
        private readonly Action<Action<TimerSettings>> _apply;
        private readonly IDialogService _dialogs;
        private readonly DispatcherTimer _refresh;

        private LinkPage _page;
        private DateTime _pageShownAt = DateTime.Now;
        private DateTime _environmentAt = DateTime.MinValue;
        private List<LinkNetwork> _networks = new List<LinkNetwork>();
        private bool _hasBluetooth;
        private List<BluetoothDevice> _pairedComputers = new List<BluetoothDevice>();
        private bool _loadingOptions;
        private bool _optionsLoaded;
        private bool _pausedTopmost;

        private string _codeBig = string.Empty, _mainTip = string.Empty;
        private bool _mainTipVisible;
        private string _code = string.Empty;
        private string _followHeadline = string.Empty, _followDetail = string.Empty;
        private LineState _followHeadlineState = LineState.Off;
        private bool _showOff;
        private bool _moreOptions;
        private bool _sound;
        private string _stationsFrom = string.Empty, _stationsTo = string.Empty, _address = string.Empty, _optionsError = string.Empty;
        private object _selectedBluetooth;
        private LinkHelpTopic _topic;

        /// <param name="settings">This PC's own settings (read for the starting values).</param>
        /// <param name="getStatus">The link's state right now.</param>
        /// <param name="apply">Changes this PC's link settings, saves them and restarts the link.</param>
        public LinkViewModel(TimerSettings settings, Func<LinkStatus> getStatus, Action<Action<TimerSettings>> apply, IDialogService dialogs)
        {
            _settings = settings;
            _getStatus = getStatus;
            _apply = apply;
            _dialogs = dialogs;

            BecomeMainPc = new RelayCommand(_ => DoBecomeMainPc());
            ChooseSecondScreen = new RelayCommand(_ => ShowPage(LinkPage.Follow));
            ChangeRole = new RelayCommand(_ => ShowPage(LinkPage.Choose));
            TurnOff = new RelayCommand(_ =>
            {
                _apply(s => s.LinkRole = "off");
                SetCode(string.Empty);
                ShowPage(LinkPage.Choose);
            });
            NewCode = new RelayCommand(_ =>
            {
                _apply(s => s.LinkCode = LinkProtocol.NewCode());
                RefreshStatus();
            });
            LinkNow = new RelayCommand(_ => CodeTyped(true));
            ApplyOptionsCommand = new RelayCommand(_ => ApplyOptions());
            ShowHelp = new RelayCommand(p => ShowTopic(p as string ?? (p as LinkHelpTopic)?.Id));
            BackToStatus = new RelayCommand(_ => { SelectedTopic = null; _environmentAt = DateTime.MinValue; });
            OpenBluetoothSettings = new RelayCommand(_ => OpenWindowsScreen("ms-settings:bluetooth", null));
            // The same window as the Bluetooth tray icon's "Join a Personal Area Network".
            OpenPanDevices = new RelayCommand(_ => OpenWindowsScreen("explorer.exe", "shell:::{28803F59-3A75-4058-995F-4EE5503B023C}"));

            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _refresh.Tick += delegate { RefreshStatus(); };

            if (settings.IsLinkLeader) ShowPage(LinkPage.Main);
            else if (settings.IsLinkFollower) ShowPage(LinkPage.Follow);
            else ShowPage(LinkPage.Choose);
        }

        // ================================================================ pages

        public LinkPage Page
        {
            get { return _page; }
        }

        public bool IsChoose { get { return _page == LinkPage.Choose; } }
        public bool IsMain { get { return _page == LinkPage.Main; } }
        public bool IsFollow { get { return _page == LinkPage.Follow; } }

        /// <summary>The tag beside the page title.</summary>
        public string RoleTag
        {
            get { return _page == LinkPage.Main ? "THIS IS THE MAIN PC" : _page == LinkPage.Follow ? "THIS IS A SECOND SCREEN" : "NOT LINKED"; }
        }

        public void ShowPage(LinkPage page)
        {
            _page = page;
            _pageShownAt = DateTime.Now;
            ShowOff = !string.Equals(_getStatus().Role, "off", StringComparison.OrdinalIgnoreCase);
            if (page == LinkPage.Follow) LoadOptions();
            Raise(nameof(Page));
            Raise(nameof(IsChoose));
            Raise(nameof(IsMain));
            Raise(nameof(IsFollow));
            Raise(nameof(RoleTag));
            RefreshStatus();
        }

        public ICommand BecomeMainPc { get; }
        public ICommand ChooseSecondScreen { get; }
        public ICommand ChangeRole { get; }
        public ICommand TurnOff { get; }
        public ICommand NewCode { get; }
        public ICommand LinkNow { get; }
        public ICommand ApplyOptionsCommand { get; }

        /// <summary>"Turn linking off" on the choose page, only while linking is on.</summary>
        public bool ShowOff { get { return _showOff; } private set { Set(ref _showOff, value); } }

        private void DoBecomeMainPc()
        {
            _apply(s =>
            {
                s.LinkRole = "lead";
                if (string.IsNullOrWhiteSpace(s.LinkCode) || s.LinkCode.Trim().Length < 4) s.LinkCode = LinkProtocol.NewCode();
            });
            ShowPage(LinkPage.Main);
        }

        // ================================================================ main PC

        /// <summary>The code with spaces between the digits: "4 8 2 1".</summary>
        public string CodeBig { get { return _codeBig; } private set { Set(ref _codeBig, value); } }
        public StatusLine MainScreens { get; } = new StatusLine();
        public StatusLine MainNetwork { get; } = new StatusLine();
        public StatusLine MainBluetooth { get; } = new StatusLine();
        public string MainTip { get { return _mainTip; } private set { Set(ref _mainTip, value); } }
        public bool MainTipVisible { get { return _mainTipVisible; } private set { Set(ref _mainTipVisible, value); } }

        // ================================================================ second screen

        /// <summary>The code box. The fourth digit starts the link - nothing else to press.</summary>
        public string Code
        {
            get { return _code; }
            set
            {
                if (!Set(ref _code, value ?? string.Empty)) return;
                CodeTyped(false);
            }
        }

        private void SetCode(string code)
        {
            _code = code;
            Raise(nameof(Code));
        }

        public string FollowHeadline { get { return _followHeadline; } private set { Set(ref _followHeadline, value); } }
        public LineState FollowHeadlineState { get { return _followHeadlineState; } private set { Set(ref _followHeadlineState, value); } }
        public string FollowDetail { get { return _followDetail; } private set { Set(ref _followDetail, value); } }
        public StatusLine FollowNetwork { get; } = new StatusLine();
        public StatusLine FollowBluetooth { get; } = new StatusLine();

        public bool MoreOptions { get { return _moreOptions; } set { Set(ref _moreOptions, value); } }
        public bool Sound { get { return _sound; } set { Set(ref _sound, value); } }
        public string StationsFrom { get { return _stationsFrom; } set { Set(ref _stationsFrom, value ?? string.Empty); } }
        public string StationsTo { get { return _stationsTo; } set { Set(ref _stationsTo, value ?? string.Empty); } }
        public string Address { get { return _address; } set { Set(ref _address, value ?? string.Empty); } }
        public string OptionsError { get { return _optionsError; } private set { Set(ref _optionsError, value); } }

        /// <summary>"Automatic - any paired computer" then each paired computer (a BluetoothDevice).</summary>
        public ObservableCollection<object> BluetoothDevices { get; } = new ObservableCollection<object>();
        public object SelectedBluetooth { get { return _selectedBluetooth; } set { Set(ref _selectedBluetooth, value); } }

        /// <summary>The code box changed (or Link/Enter was pressed): four digits is a complete code, so start linking.</summary>
        private void CodeTyped(bool force)
        {
            if (_loadingOptions) return;
            string code = _code.Trim();
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
                if (_settings.IsLinkFollower && _code.Length == 0) SetCode(_settings.LinkCode);
                Sound = _settings.LinkSound;
                StationsFrom = _settings.LinkStationsFrom > 0 ? _settings.LinkStationsFrom.ToString(CultureInfo.InvariantCulture) : string.Empty;
                StationsTo = _settings.LinkStationsTo > 0 ? _settings.LinkStationsTo.ToString(CultureInfo.InvariantCulture) : string.Empty;
                Address = _settings.LinkAddress;
                FillBluetoothDevices();
                _optionsLoaded = true;
            }
            finally
            {
                _loadingOptions = false;
            }
        }

        private void FillBluetoothDevices()
        {
            ulong wanted = 0;
            BluetoothDevice current = _selectedBluetooth as BluetoothDevice;
            if (current != null) wanted = current.Address;
            else if (BluetoothDevices.Count == 0) BluetoothLink.TryParseAddress(_settings.LinkBluetoothAddress, out wanted);

            BluetoothDevices.Clear();
            const string automatic = "Automatic - any paired computer";
            BluetoothDevices.Add(automatic);
            object selected = automatic;
            foreach (BluetoothDevice device in LinkEnvironment.PairedComputers())
            {
                BluetoothDevices.Add(device);
                if (device.Address == wanted) selected = device;
            }
            if (wanted != 0 && ReferenceEquals(selected, automatic))
            {
                // Keep a saved choice even if Windows does not list it right now.
                BluetoothDevice saved = new BluetoothDevice { Address = wanted, Name = _settings.LinkBluetoothName, IsComputer = true };
                BluetoothDevices.Add(saved);
                selected = saved;
            }
            SelectedBluetooth = selected;
        }

        /// <summary>Saves "More options" if anything in it changed. Returns false if something typed is not usable.</summary>
        public bool ApplyOptions()
        {
            if (_page != LinkPage.Follow && !_settings.IsLinkFollower) return true;
            if (!_optionsLoaded) return true; // the page was never opened

            int from = 0, to = 0;
            bool fromOk = _stationsFrom.Trim().Length == 0 || (int.TryParse(_stationsFrom.Trim(), out from) && from >= 1 && from <= 99);
            bool toOk = _stationsTo.Trim().Length == 0 || (int.TryParse(_stationsTo.Trim(), out to) && to >= 1 && to <= 99);
            if (!fromOk || !toOk || (from > 0 && to > 0 && from > to))
            {
                OptionsError = "Stations must be numbers, e.g. 3 to 4, or left blank to show them all.";
                return false;
            }
            OptionsError = string.Empty;

            BluetoothDevice device = _selectedBluetooth as BluetoothDevice;
            string bluetoothAddress = device != null ? device.AddressText : string.Empty;
            string bluetoothName = device != null ? (device.Name ?? string.Empty) : string.Empty;
            string address = _address.Trim();
            bool sound = _sound;

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
            _dialogs?.Notify("Options saved.", InfoKind.Success);
            return true;
        }

        // ================================================================ the guide

        public IReadOnlyList<LinkHelpTopic> Topics { get { return LinkHelp.Topics; } }

        /// <summary>The guide topic showing in place of the status, or null for the status.</summary>
        public LinkHelpTopic SelectedTopic
        {
            get { return _topic; }
            set
            {
                if (!Set(ref _topic, value)) return;
                Raise(nameof(ShowingHelp));
                Raise(nameof(ShowingStatus));
                Raise(nameof(ShowBluetoothButtons));
            }
        }

        public bool ShowingHelp { get { return _topic != null; } }
        public bool ShowingStatus { get { return _topic == null; } }
        public bool ShowBluetoothButtons { get { return _topic != null && _topic.Id == LinkHelp.Bluetooth; } }

        public ICommand ShowHelp { get; }
        public ICommand BackToStatus { get; }
        public ICommand OpenBluetoothSettings { get; }
        public ICommand OpenPanDevices { get; }

        /// <summary>Opens a guide topic; null opens the one that fits what is on screen.</summary>
        public void ShowTopic(string topic)
        {
            if (topic == null)
            {
                LinkStatus status = _getStatus();
                topic = LinkHelp.Start;
                if (_page == LinkPage.Follow && status.Role == "follow" && status.FollowerState != FollowerStatus.Connected) topic = LinkHelp.Trouble;
                else if (_page == LinkPage.Follow && status.FollowerState == FollowerStatus.Connected) topic = LinkHelp.SecondScreen;
            }
            SelectedTopic = LinkHelp.Find(topic) ?? LinkHelp.Topics[0];
        }

        /// <summary>
        /// Opens a Windows screen. The clock and its windows are normally always-on-top, which would
        /// hide it - so that is dropped until the teacher leaves this page.
        /// </summary>
        private void OpenWindowsScreen(string target, string arguments)
        {
            if (_dialogs == null) return;
            if (!_dialogs.Open(target, arguments))
            {
                _dialogs.Info("Link displays", "That Windows screen could not be opened from here. Open Settings > Bluetooth & devices by hand.");
                return;
            }
            if (!_pausedTopmost) { TopmostPause.Pause(); _pausedTopmost = true; }
            _environmentAt = DateTime.MinValue; // the PCs may be about to be paired
        }

        // ================================================================ life

        /// <summary>The page is on screen: refresh every 400 ms.</summary>
        public void Start()
        {
            if (_page == LinkPage.Follow) LoadOptions();
            RefreshStatus();
            _refresh.Start();
        }

        /// <summary>The page has gone: stop refreshing, keep anything typed under "More options", restore always-on-top.</summary>
        public void Stop()
        {
            _refresh.Stop();
            ApplyOptions();
            if (_pausedTopmost) { TopmostPause.Resume(); _pausedTopmost = false; }
        }

        // ================================================================ live status

        private void RefreshEnvironment()
        {
            if ((DateTime.Now - _environmentAt).TotalSeconds < 3) return;
            _environmentAt = DateTime.Now;
            _networks = LinkEnvironment.Networks();
            _hasBluetooth = LinkEnvironment.HasBluetooth();
            _pairedComputers = _hasBluetooth ? LinkEnvironment.PairedComputers() : new List<BluetoothDevice>();
        }

        public void RefreshStatus()
        {
            RefreshEnvironment();
            LinkStatus status = _getStatus();
            if (_page == LinkPage.Main) RefreshMainPage(status);
            if (_page == LinkPage.Follow) RefreshFollowPage(status);
        }

        private string PairedNames()
        {
            return string.Join(", ", _pairedComputers.Select(d => string.IsNullOrWhiteSpace(d.Name) ? d.AddressText : d.Name));
        }

        private void RefreshMainPage(LinkStatus status)
        {
            CodeBig = string.Join(" ", (status.Code ?? string.Empty).ToCharArray());

            if (!status.LeaderRunning)
            {
                MainScreens.Show(LineState.Problem, "The link could not start. "
                    + (status.LeaderError.Length > 0 ? status.LeaderError : "Close any other copy of Gym Interval Clock and try again."));
            }
            else if (status.Screens.Count == 0)
            {
                MainScreens.Show(LineState.Waiting, "Second screens: none yet - waiting...");
            }
            else
            {
                MainScreens.Show(LineState.Good, "Linked: " + string.Join(", ", status.Screens));
            }

            List<LinkNetwork> real = _networks.Where(n => !n.IsBluetooth).ToList();
            if (_networks.Count == 0)
            {
                MainNetwork.Show(_hasBluetooth && _pairedComputers.Count > 0 ? LineState.Off : LineState.Problem,
                    "Network: this PC is not connected to a network");
            }
            else
            {
                MainNetwork.Show(LineState.Good, "Network: " + LinkEnvironment.DescribeNetworks(_networks)
                    + Environment.NewLine + "This PC's address: " + string.Join("  or  ", (real.Count > 0 ? real : _networks).Select(n => n.Address)));
            }

            if (!_hasBluetooth) MainBluetooth.Show(LineState.Off, "Bluetooth: not available on this PC (not needed if the PCs share a network)");
            else if (!status.BluetoothListening && status.LeaderRunning) MainBluetooth.Show(LineState.Off, "Bluetooth: could not be started");
            else if (_pairedComputers.Count == 0) MainBluetooth.Show(LineState.Off, "Bluetooth: ready, but no computer is paired with this PC yet");
            else MainBluetooth.Show(LineState.Good, "Bluetooth: ready (paired with " + PairedNames() + ")");

            // After a while with nobody connecting, say what to check - without the teacher having to ask.
            bool stuck = status.LeaderRunning && status.Screens.Count == 0 && (DateTime.Now - _pageShownAt).TotalSeconds > 25;
            if (stuck)
            {
                MainTip = "Nothing yet? On the second PC press L, choose \"This is a SECOND SCREEN\" and type "
                    + status.Code + ". Both PCs must be on the same network"
                    + (_hasBluetooth ? " (or paired by Bluetooth)" : string.Empty)
                    + ". If Windows asked about the firewall, choose Allow.";
            }
            MainTipVisible = stuck;
        }

        private void RefreshFollowPage(LinkStatus status)
        {
            string headline;
            string detail;
            LineState state = LineState.Off;

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
                        state = LineState.Good;
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
                        state = LineState.Problem;
                        headline = "That code was not accepted.";
                        detail = "Check the link code at the bottom of the main PC's screen and type it again.";
                        break;

                    case FollowerStatus.Reconnecting:
                        state = LineState.Waiting;
                        headline = "Reconnecting to the main PC...";
                        detail = (status.Activity.Length > 0 ? status.Activity + Environment.NewLine : string.Empty)
                            + "This screen keeps counting by itself and catches up when the link returns.";
                        break;

                    default:
                        state = LineState.Waiting;
                        headline = "Looking for the main PC...";
                        detail = status.Activity;
                        if (status.FailedRounds >= 2)
                        {
                            state = LineState.Problem;
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

            FollowHeadline = headline;
            FollowHeadlineState = state;
            FollowDetail = detail;

            if (_networks.Count == 0) FollowNetwork.Show(LineState.Problem, "Network: this PC is not connected to a network");
            else FollowNetwork.Show(LineState.Good, "Network: " + LinkEnvironment.DescribeNetworks(_networks));

            if (!_hasBluetooth) FollowBluetooth.Show(LineState.Off, "Bluetooth: not available on this PC (not needed if the PCs share a network)");
            else if (_pairedComputers.Count == 0) FollowBluetooth.Show(LineState.Off, "Bluetooth: on, but no computer is paired with this PC yet");
            else FollowBluetooth.Show(LineState.Good, "Bluetooth: paired with " + PairedNames() + " - used automatically if the network fails");
        }
    }
}
