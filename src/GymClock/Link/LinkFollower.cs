using System;
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
        /// <summary>Listening for the main PC's announcement.</summary>
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
    /// A second screen's side of linked displays: finds the main PC (by its announcement, or a
    /// typed address), connects with the link code, keeps its clock matched by pinging, and hands
    /// PROGRAM and STATE messages to MainForm. If the link drops it keeps trying to reconnect.
    /// It only ever sends HELLO, PING and BYE - a second screen has no control over the session.
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
        private readonly Func<CancellationToken, ILinkConnection> _connect; // set = a fixed transport (Bluetooth), no searching
        private ILinkConnection _connection;
        private bool _everConnected;

        /// <summary>The main PC's settings text and program script (sent on connect and whenever they change).</summary>
        public event Action<string, string> ProgramReceived;
        /// <summary>Where the main PC's session is.</summary>
        public event Action<LinkMessage> StateReceived;
        public event Action StatusChanged;

        public FollowerStatus Status { get; private set; } = FollowerStatus.Searching;
        public string LeaderName { get; private set; } = string.Empty;

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
        /// A second screen that reaches the main PC over a given transport instead of the local
        /// network - Bluetooth, where there is nothing to search for: <paramref name="connect"/> opens
        /// the connection (or returns null / throws when the main PC can't be reached yet).
        /// </summary>
        public LinkFollower(string name, string code, Func<CancellationToken, ILinkConnection> connect)
            : this(name, code, string.Empty)
        {
            _connect = connect;
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

        private void SetStatus(FollowerStatus status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke();
        }

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

                    ILinkConnection connection;
                    if (_connect != null)
                    {
                        if (Status == FollowerStatus.Searching) SetStatus(FollowerStatus.Connecting);
                        connection = _connect(_stop.Token);
                    }
                    else
                    {
                        IPEndPoint leader = FindLeader();
                        if (leader == null) continue; // nothing heard yet - keep listening

                        if (Status == FollowerStatus.Searching) SetStatus(FollowerStatus.Connecting);
                        connection = TcpLinkConnection.Connect(leader, 4000, _stop.Token);
                    }

                    if (connection != null) Session(connection);
                }
                catch { /* any network failure: wait a moment and try again */ }

                _stop.Token.WaitHandle.WaitOne(Status == FollowerStatus.WrongCode ? 5000 : 1500);
            }
        }

        private IPEndPoint FindLeader()
        {
            if (_address.Length > 0)
            {
                if (IPAddress.TryParse(_address, out IPAddress typed)) return new IPEndPoint(typed, _dataPort);
                try
                {
                    IPAddress resolved = Dns.GetHostAddresses(_address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (resolved != null) return new IPEndPoint(resolved, _dataPort);
                }
                catch { }
                return null;
            }

            // Listen for an announcement carrying the tag only our link code can produce.
            using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
                udp.Client.ReceiveTimeout = 500;

                Stopwatch waited = Stopwatch.StartNew();
                while (!_stop.IsCancellationRequested && waited.ElapsedMilliseconds < 6000)
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

        private void Session(ILinkConnection connection)
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
                                SetStatus(FollowerStatus.WrongCode);
                                return;
                            }
                            if (!LinkProtocol.TryDecode(line, _key, out LinkMessage message)) continue;

                            if (!_everConnected || Status != FollowerStatus.Connected)
                            {
                                _everConnected = true;
                                SetStatus(FollowerStatus.Connected);
                            }

                            switch (message.Type)
                            {
                                case LinkMessage.Pong:
                                    if (message.T0.HasValue && message.LeaderTimeMs.HasValue)
                                        _sync.AddSample(message.T0.Value, message.LeaderTimeMs.Value, received);
                                    break;
                                case LinkMessage.Program:
                                    if (!string.IsNullOrWhiteSpace(message.Name)) LeaderName = message.Name; // known even when connected by address
                                    ProgramReceived?.Invoke(message.Settings ?? string.Empty, message.Script ?? string.Empty);
                                    break;
                                case LinkMessage.State:
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
