using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GymClock.Link
{
    public enum FollowerStatus
    {
        /// <summary>Looking for the main PC (network announcement, a typed address, Bluetooth).</summary>
        Searching,
        /// <summary>Found or given an address; connecting.</summary>
        Connecting,
        /// <summary>Linked and receiving.</summary>
        Connected,
        /// <summary>Was linked; trying to get back.</summary>
        Reconnecting,
        /// <summary>The main PC refused the link code.</summary>
        WrongCode
    }

    /// <summary>
    /// How a second screen should look for the main PC when it is left to work it out by itself:
    /// the route that worked last time first, then the network, then Bluetooth.
    /// </summary>
    public sealed class LinkRoutePlan
    {
        /// <summary>The route that worked last time ("tcp:192.168.1.20" or "bt:00:1A:7D:DA:71:13"), tried first.</summary>
        public string LastGoodRoute { get; set; }

        /// <summary>An address the teacher typed, if any.</summary>
        public string TypedAddress { get; set; }

        /// <summary>Listen for the main PC's announcement on the local network.</summary>
        public bool UseNetwork { get; set; } = true;

        /// <summary>Try Bluetooth as well (ignored when this PC has no Bluetooth).</summary>
        public bool UseBluetooth { get; set; } = true;

        /// <summary>The paired main PC chosen by the teacher, if any ("00:1A:7D:DA:71:13"). Otherwise every paired computer is tried.</summary>
        public string BluetoothAddress { get; set; }

        /// <summary>Bluetooth pieces, replaceable so tests need no radio.</summary>
        public Func<IReadOnlyList<BluetoothDevice>> PairedComputers { get; set; }
        public Func<ulong, CancellationToken, ILinkConnection> BluetoothConnect { get; set; }

        public const string TcpPrefix = "tcp:";
        public const string BluetoothPrefix = "bt:";
    }

    /// <summary>How good the link is right now, from the pings and the main PC's regular state messages.</summary>
    public sealed class LinkQuality
    {
        public int Samples { get; set; }
        /// <summary>The quickest recent round trip (ms).</summary>
        public long PingMs { get; set; }
        /// <summary>The slowest recent round trip (ms).</summary>
        public long WorstPingMs { get; set; }
        /// <summary>The longest recent wait between two state messages (ms; they are sent every 250 ms).</summary>
        public long MaxGapMs { get; set; }

        public enum Level { Measuring, Good, Usable, Poor }

        /// <summary>The plan's target is screens within 100 ms of each other.</summary>
        public Level Verdict
        {
            get
            {
                if (Samples < 5) return Level.Measuring;
                if (PingMs <= 60 && WorstPingMs <= 250 && MaxGapMs <= 700) return Level.Good;
                if (PingMs <= 200 && MaxGapMs <= 1500) return Level.Usable;
                return Level.Poor;
            }
        }

        /// <summary>One line for the teacher.</summary>
        public string Describe()
        {
            switch (Verdict)
            {
                case Level.Good:
                    return "Link test: GOOD - the screens will stay in step (delay " + PingMs + " ms).";
                case Level.Usable:
                    return "Link test: USABLE - it works, but the connection is slow or uneven (delay " + PingMs
                        + " ms, longest gap " + MaxGapMs + " ms). Moving the PCs closer or using another network may help.";
                case Level.Poor:
                    return "Link test: POOR - the connection is too slow or unsteady (delay " + PingMs
                        + " ms, longest gap " + MaxGapMs + " ms). Try another network, a phone hotspot, or Bluetooth.";
                default:
                    return "Link test: measuring...";
            }
        }
    }

    /// <summary>
    /// A second screen's side of linked displays: finds the main PC, connects with the link code,
    /// keeps its clock matched by pinging, and hands PROGRAM and STATE messages to MainForm. If
    /// the link drops it keeps trying to reconnect. It only ever sends HELLO, PING and BYE - a
    /// second screen has no control over the session.
    ///
    /// Given a <see cref="LinkRoutePlan"/> it works out the route by itself: the one that worked
    /// last time, a typed address, the network announcement, then Bluetooth - round and round
    /// until the main PC answers.
    ///
    /// Runs on a background thread; events are raised on that thread.
    /// </summary>
    public sealed class LinkFollower : IDisposable
    {
        private readonly string _name;
        private readonly byte[] _key;
        private readonly string _address;
        private readonly int _dataPort;
        private readonly int _discoveryPort;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly ClockSync _sync = new ClockSync();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Func<CancellationToken, ILinkConnection> _connect; // set = one fixed transport, no searching
        private readonly LinkRoutePlan _plan;                                // set = work the route out automatically
        private ILinkConnection _connection;
        private bool _everConnected;

        // Link quality: recent ping round trips and the times state messages arrived.
        private readonly object _qualityLock = new object();
        private readonly List<long> _recentPings = new List<long>();
        private readonly List<long> _recentGaps = new List<long>();
        private long _lastStateAt = -1;

        /// <summary>The main PC's settings text and program script (sent on connect and whenever they change).</summary>
        public event Action<string, string> ProgramReceived;
        /// <summary>Where the main PC's session is.</summary>
        public event Action<LinkMessage> StateReceived;
        /// <summary>Status, Activity or the route changed.</summary>
        public event Action StatusChanged;

        public FollowerStatus Status { get; private set; } = FollowerStatus.Searching;
        public string LeaderName { get; private set; } = string.Empty;

        /// <summary>What it is doing right now, in words for the teacher ("Looking for the main PC on the network...").</summary>
        public string Activity { get; private set; } = string.Empty;

        /// <summary>"network" or "Bluetooth" while linked.</summary>
        public string ConnectedVia { get; private set; } = string.Empty;

        /// <summary>The route in use while linked ("tcp:192.168.1.20" / "bt:00:1A:..."), worth trying first next time.</summary>
        public string ConnectedRoute { get; private set; } = string.Empty;

        /// <summary>How many times every route has been tried without finding the main PC (automatic mode).</summary>
        public int FailedRounds { get; private set; }

        /// <param name="address">The main PC's address, or blank to find it by its announcement.</param>
        public LinkFollower(string name, string code, string address,
            int dataPort = LinkProtocol.DataPort, int discoveryPort = LinkProtocol.DiscoveryPort)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "Second screen" : name;
            _key = LinkProtocol.KeyFromCode(code);
            _address = (address ?? string.Empty).Trim();
            _dataPort = dataPort;
            _discoveryPort = discoveryPort;
        }

        /// <summary>
        /// A second screen that reaches the main PC over one given transport: <paramref name="connect"/>
        /// opens the connection (or returns null / throws when the main PC can't be reached yet).
        /// </summary>
        public LinkFollower(string name, string code, Func<CancellationToken, ILinkConnection> connect)
            : this(name, code, string.Empty)
        {
            _connect = connect;
        }

        /// <summary>A second screen that works the route out by itself (see <see cref="LinkRoutePlan"/>).</summary>
        public LinkFollower(string name, string code, LinkRoutePlan plan,
            int dataPort = LinkProtocol.DataPort, int discoveryPort = LinkProtocol.DiscoveryPort)
            : this(name, code, string.Empty, dataPort, discoveryPort)
        {
            _plan = plan ?? new LinkRoutePlan();
        }

        public void Start()
        {
            new Thread(Run) { IsBackground = true, Name = "GymClock link follower" }.Start();
        }

        /// <summary>The main PC's link clock right now (the time base of STATE messages). False until the first pings are back.</summary>
        public bool TryLeaderNow(out long leaderMs)
        {
            leaderMs = _sync.ToLeaderTime(_clock.ElapsedMilliseconds);
            return _sync.HasOffset;
        }

        /// <summary>The measured round trip to the main PC, in milliseconds.</summary>
        public long RoundTripMs
        {
            get { return _sync.RoundTripMs; }
        }

        /// <summary>The link test: how good the connection has been over the last few seconds.</summary>
        public LinkQuality Quality
        {
            get
            {
                lock (_qualityLock)
                {
                    return new LinkQuality
                    {
                        Samples = _recentPings.Count,
                        PingMs = _recentPings.Count > 0 ? _recentPings.Min() : 0,
                        WorstPingMs = _recentPings.Count > 0 ? _recentPings.Max() : 0,
                        MaxGapMs = _recentGaps.Count > 0 ? _recentGaps.Max() : 0
                    };
                }
            }
        }

        private void SetStatus(FollowerStatus status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke();
        }

        private void SetActivity(string activity)
        {
            if (Activity == activity) return;
            Activity = activity;
            StatusChanged?.Invoke();
        }

        // ------------------------------------------------------------------ finding the main PC

        private void Run()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    // "Wrong code" stays on screen while we keep retrying (the teacher may fix it on either PC).
                    if (Status != FollowerStatus.WrongCode)
                    {
                        SetStatus(_everConnected ? FollowerStatus.Reconnecting : FollowerStatus.Searching);
                    }

                    if (_plan != null)
                    {
                        if (!TryEveryRoute()) FailedRounds++;
                    }
                    else
                    {
                        ILinkConnection connection;
                        if (_connect != null)
                        {
                            if (Status == FollowerStatus.Searching) SetStatus(FollowerStatus.Connecting);
                            connection = _connect(_stop.Token);
                        }
                        else
                        {
                            IPEndPoint leader = FindLeader(6000);
                            if (leader == null) continue; // nothing heard yet - keep listening

                            if (Status == FollowerStatus.Searching) SetStatus(FollowerStatus.Connecting);
                            connection = TcpLinkConnection.Connect(leader, 4000, _stop.Token);
                        }

                        if (connection != null) Session(connection, string.Empty, string.Empty);
                    }
                }
                catch { /* any network failure: wait a moment and try again */ }

                _stop.Token.WaitHandle.WaitOne(Status == FollowerStatus.WrongCode ? 5000 : 1500);
            }
        }

        /// <summary>
        /// One pass over every route in the plan. Returns true if a connection was made (it has since
        /// ended, or the code was refused); false if the main PC could not be reached by any route.
        /// </summary>
        private bool TryEveryRoute()
        {
            bool connected = false;
            HashSet<string> tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Whatever worked last time: usually right, and much quicker than searching.
            string last = ConnectedRoute.Length > 0 ? ConnectedRoute : (_plan.LastGoodRoute ?? string.Empty);
            if (last.Length > 0) connected |= TryRoute(last, tried);

            // 2. An address the teacher typed.
            string typed = (_plan.TypedAddress ?? string.Empty).Trim();
            if (!connected && typed.Length > 0) connected |= TryRoute(LinkRoutePlan.TcpPrefix + typed, tried);

            // 3. The main PC's announcement on the local network.
            if (!connected && _plan.UseNetwork && !_stop.IsCancellationRequested)
            {
                SetActivity("Looking for the main PC on the network...");
                IPEndPoint found = FindLeader(4000);
                if (found != null)
                {
                    string route = LinkRoutePlan.TcpPrefix + found.Address + (found.Port != _dataPort ? ":" + found.Port : string.Empty);
                    tried.Remove(route); // it is announcing right now, so worth trying even if a moment ago it was not
                    connected |= TryRoute(route, tried);
                }
            }

            // 4. Bluetooth: the chosen main PC, or every paired computer.
            if (!connected && _plan.UseBluetooth && !_stop.IsCancellationRequested)
            {
                List<string> candidates = new List<string>();
                if (BluetoothLink.TryParseAddress(_plan.BluetoothAddress, out ulong chosen))
                {
                    candidates.Add(LinkRoutePlan.BluetoothPrefix + BluetoothLink.FormatAddress(chosen));
                }
                else
                {
                    IReadOnlyList<BluetoothDevice> paired = null;
                    try { paired = (_plan.PairedComputers ?? DefaultPairedComputers)(); } catch { }
                    foreach (BluetoothDevice device in paired ?? new List<BluetoothDevice>())
                    {
                        candidates.Add(LinkRoutePlan.BluetoothPrefix + device.AddressText);
                    }
                }
                foreach (string candidate in candidates)
                {
                    if (connected || _stop.IsCancellationRequested) break;
                    connected |= TryRoute(candidate, tried);
                }
            }

            if (!connected) SetActivity("The main PC was not found. Trying again...");
            return connected;
        }

        private static IReadOnlyList<BluetoothDevice> DefaultPairedComputers()
        {
            if (!BluetoothLink.HasRadio()) return new List<BluetoothDevice>();
            return BluetoothLink.PairedDevices().Where(d => d.IsComputer).ToList();
        }

        /// <summary>Tries one route. True if it connected (the session has since ended).</summary>
        private bool TryRoute(string route, HashSet<string> tried)
        {
            if (_stop.IsCancellationRequested || !tried.Add(route)) return false;

            ILinkConnection connection = null;
            string via;
            try
            {
                if (route.StartsWith(LinkRoutePlan.BluetoothPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    via = "Bluetooth";
                    if (!_plan.UseBluetooth) return false;
                    if (!BluetoothLink.TryParseAddress(route.Substring(LinkRoutePlan.BluetoothPrefix.Length), out ulong address)) return false;
                    SetActivity("Trying Bluetooth (" + BluetoothNameFor(address) + ")...");
                    connection = (_plan.BluetoothConnect ?? BluetoothLinkConnection.Connect)(address, _stop.Token);
                }
                else
                {
                    via = "network";
                    IPEndPoint endpoint = ParseTcpRoute(route.StartsWith(LinkRoutePlan.TcpPrefix, StringComparison.OrdinalIgnoreCase)
                        ? route.Substring(LinkRoutePlan.TcpPrefix.Length) : route);
                    if (endpoint == null) return false;
                    SetActivity("Connecting to the main PC at " + endpoint.Address + "...");
                    connection = TcpLinkConnection.Connect(endpoint, 2500, _stop.Token);
                }
            }
            catch
            {
                return false;
            }
            if (connection == null) return false;

            Session(connection, via, route);
            return true;
        }

        private string BluetoothNameFor(ulong address)
        {
            try
            {
                foreach (BluetoothDevice device in (_plan.PairedComputers ?? DefaultPairedComputers)())
                {
                    if (device.Address == address && !string.IsNullOrWhiteSpace(device.Name)) return device.Name;
                }
            }
            catch { }
            return BluetoothLink.FormatAddress(address);
        }

        private IPEndPoint ParseTcpRoute(string text)
        {
            string host = text.Trim();
            int port = _dataPort;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && int.TryParse(host.Substring(colon + 1), out int parsedPort))
            {
                port = parsedPort;
                host = host.Substring(0, colon);
            }
            if (IPAddress.TryParse(host, out IPAddress ip)) return new IPEndPoint(ip, port);
            try
            {
                IPAddress resolved = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (resolved != null) return new IPEndPoint(resolved, port);
            }
            catch { }
            return null;
        }

        private IPEndPoint FindLeader(int listenMs)
        {
            if (_plan == null && _address.Length > 0) return ParseTcpRoute(_address);

            // Listen for an announcement carrying the tag only our link code can produce.
            using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
                udp.Client.ReceiveTimeout = 500;

                Stopwatch waited = Stopwatch.StartNew();
                while (!_stop.IsCancellationRequested && waited.ElapsedMilliseconds < listenMs)
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] bytes;
                    try { bytes = udp.Receive(ref from); }
                    catch (SocketException) { continue; }

                    if (!LinkProtocol.TryDecodeAnnounce(bytes, _key, out LinkMessage announce)) continue;
                    LeaderName = announce.Name ?? string.Empty;
                    return new IPEndPoint(from.Address, announce.Port ?? _dataPort);
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ one connection

        private void Session(ILinkConnection connection, string via, string route)
        {
            using (connection)
            {
                _connection = connection;

                StreamWriter writer = new StreamWriter(connection.Stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                object sendLock = new object();
                bool alive = true;

                lock (sendLock) writer.WriteLine(LinkProtocol.Encode(new LinkMessage { Type = LinkMessage.Hello, Name = _name }, _key));

                // A new connection may be to a restarted main PC with a new clock: measure afresh.
                _sync.Reset();
                lock (_qualityLock)
                {
                    _recentPings.Clear();
                    _recentGaps.Clear();
                    _lastStateAt = -1;
                }

                Thread pinger = new Thread(() =>
                {
                    int sent = 0;
                    while (alive && !_stop.IsCancellationRequested)
                    {
                        try
                        {
                            lock (sendLock) writer.WriteLine(LinkProtocol.Encode(
                                new LinkMessage { Type = LinkMessage.Ping, T0 = _clock.ElapsedMilliseconds }, _key));
                        }
                        catch { break; }
                        sent++;
                        // A quick burst first so the clocks match within a second of connecting, then once a second.
                        _stop.Token.WaitHandle.WaitOne(sent < 6 ? 120 : 1000);
                    }
                }) { IsBackground = true, Name = "GymClock link ping" };
                pinger.Start();

                try
                {
                    // The main PC sends state about 4 times a second; 5 s of silence means the link is gone.
                    connection.SetReceiveTimeout(5000);
                    using (StreamReader reader = new StreamReader(connection.Stream, Encoding.UTF8))
                    {
                        string line;
                        while (!_stop.IsCancellationRequested && (line = reader.ReadLine()) != null)
                        {
                            long received = _clock.ElapsedMilliseconds;
                            if (line == LinkProtocol.DeniedLine)
                            {
                                SetActivity("The main PC did not accept the link code.");
                                SetStatus(FollowerStatus.WrongCode);
                                return;
                            }
                            if (!LinkProtocol.TryDecode(line, _key, out LinkMessage message)) continue;

                            if (!_everConnected || Status != FollowerStatus.Connected)
                            {
                                _everConnected = true;
                                FailedRounds = 0;
                                ConnectedVia = via;
                                ConnectedRoute = route;
                                Activity = string.Empty;
                                SetStatus(FollowerStatus.Connected);
                            }

                            switch (message.Type)
                            {
                                case LinkMessage.Pong:
                                    if (message.T0.HasValue && message.LeaderTimeMs.HasValue)
                                    {
                                        _sync.AddSample(message.T0.Value, message.LeaderTimeMs.Value, received);
                                        lock (_qualityLock)
                                        {
                                            _recentPings.Add(received - message.T0.Value);
                                            if (_recentPings.Count > 12) _recentPings.RemoveAt(0);
                                        }
                                    }
                                    break;
                                case LinkMessage.Program:
                                    if (!string.IsNullOrWhiteSpace(message.Name)) LeaderName = message.Name; // known even when connected by address
                                    ProgramReceived?.Invoke(message.Settings ?? string.Empty, message.Script ?? string.Empty);
                                    break;
                                case LinkMessage.State:
                                    lock (_qualityLock)
                                    {
                                        if (_lastStateAt >= 0)
                                        {
                                            _recentGaps.Add(received - _lastStateAt);
                                            if (_recentGaps.Count > 40) _recentGaps.RemoveAt(0); // about the last 10 seconds
                                        }
                                        _lastStateAt = received;
                                    }
                                    StateReceived?.Invoke(message);
                                    break;
                                case LinkMessage.Bye:
                                    return;
                            }
                        }
                    }
                }
                finally
                {
                    alive = false;
                    _connection = null;
                    ConnectedVia = string.Empty;
                }
            }
        }

        public void Dispose()
        {
            if (_stop.IsCancellationRequested) return;
            _stop.Cancel();
            try { _connection?.Dispose(); } catch { }
        }
    }
}
