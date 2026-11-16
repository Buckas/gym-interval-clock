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
    /// <summary>
    /// The main PC's side of linked displays: announces itself on the local network, accepts
    /// second screens that know the link code, answers their pings and sends them whatever
    /// MainForm broadcasts (PROGRAM and STATE). Second screens never control anything - the only
    /// messages read from them are HELLO, PING and BYE.
    ///
    /// Second screens can arrive over more than one transport at once (see LinkTransport.cs): the
    /// local network always, and Bluetooth when it is switched on. After the connection is open
    /// they are treated identically.
    ///
    /// Everything runs on background threads; events are raised on those threads.
    /// </summary>
    public sealed class LinkLeader : IDisposable
    {
        private sealed class Follower
        {
            public string Name = "?";
            public ILinkConnection Connection;
            public StreamWriter Writer;
            public readonly object SendLock = new object();
            public volatile bool Alive = true;
        }

        private readonly string _name;
        private readonly byte[] _key;
        private readonly int _dataPort;
        private readonly int _discoveryPort;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<Follower> _followers = new List<Follower>();
        private readonly List<ILinkListener> _listeners = new List<ILinkListener>();
        private readonly List<ILinkListener> _extraListeners = new List<ILinkListener>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private long _seq;

        /// <summary>A second screen connected with the right code (its name).</summary>
        public event Action<string> FollowerJoined;
        /// <summary>A second screen connected or left.</summary>
        public event Action FollowersChanged;

        /// <summary>Why the link could not start (e.g. the port is in use), or null.</summary>
        public string StartError { get; private set; }

        /// <summary>Problems with optional transports (e.g. "Bluetooth: no radio"). The link still runs without them.</summary>
        public IReadOnlyList<string> Warnings { get { return _warnings; } }
        private readonly List<string> _warnings = new List<string>();

        /// <summary>The transports actually listening, e.g. "network", "Bluetooth".</summary>
        public IReadOnlyList<string> ActiveTransports
        {
            get { lock (_listeners) return _listeners.Select(l => l.Kind).ToList(); }
        }

        public LinkLeader(string name, string code, int dataPort = LinkProtocol.DataPort, int discoveryPort = LinkProtocol.DiscoveryPort)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "Main PC" : name;
            _key = LinkProtocol.KeyFromCode(code);
            _dataPort = dataPort;
            _discoveryPort = discoveryPort;
        }

        /// <summary>Adds another way for second screens to connect (e.g. Bluetooth). Call before Start.</summary>
        public void AddListener(ILinkListener listener)
        {
            _extraListeners.Add(listener);
        }

        /// <summary>Milliseconds on the main PC's link clock - the time base every STATE message uses.</summary>
        public long NowMs
        {
            get { return _clock.ElapsedMilliseconds; }
        }

        public IReadOnlyList<string> FollowerNames
        {
            get { lock (_followers) return _followers.Select(f => f.Name).ToList(); }
        }

        public bool Start()
        {
            // The local network is the main route: without it the link does not start.
            TcpLinkListener network = new TcpLinkListener(_dataPort);
            if (!network.Start(out string error))
            {
                StartError = error;
                network.Dispose();
                return false;
            }
            lock (_listeners) _listeners.Add(network);

            // Extra transports are optional: note the problem and carry on without them.
            foreach (ILinkListener extra in _extraListeners)
            {
                if (extra.Start(out string extraError))
                {
                    lock (_listeners) _listeners.Add(extra);
                }
                else
                {
                    _warnings.Add(extra.Kind + ": " + extraError);
                    extra.Dispose();
                }
            }

            ILinkListener[] started;
            lock (_listeners) started = _listeners.ToArray();
            foreach (ILinkListener listener in started)
            {
                ILinkListener current = listener;
                new Thread(() => AcceptLoop(current)) { IsBackground = true, Name = "GymClock link accept (" + current.Kind + ")" }.Start();
            }
            new Thread(AnnounceLoop) { IsBackground = true, Name = "GymClock link announce" }.Start();
            return true;
        }

        /// <summary>Sends a message to every connected second screen.</summary>
        public void Broadcast(LinkMessage message)
        {
            message.Seq = Interlocked.Increment(ref _seq);
            string line = LinkProtocol.Encode(message, _key);
            Follower[] current;
            lock (_followers) current = _followers.ToArray();
            foreach (Follower follower in current) Send(follower, line);
        }

        private void AnnounceLoop()
        {
            try
            {
                using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork))
                {
                    udp.EnableBroadcast = true;
                    byte[] bytes = Encoding.UTF8.GetBytes(LinkProtocol.EncodeAnnounce(_name, _key, _dataPort));
                    while (!_stop.IsCancellationRequested)
                    {
                        HashSet<IPAddress> targets = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
                        foreach (var address in LinkProtocol.LocalAddresses())
                        {
                            IPAddress directed = LinkProtocol.DirectedBroadcast(address);
                            if (directed != null) targets.Add(directed);
                        }
                        foreach (IPAddress target in targets)
                        {
                            try { udp.Send(bytes, bytes.Length, new IPEndPoint(target, _discoveryPort)); } catch { }
                        }
                        if (_stop.Token.WaitHandle.WaitOne(2000)) break;
                    }
                }
            }
            catch { /* announcing is best effort - a second screen can still connect by address */ }
        }

        private void AcceptLoop(ILinkListener listener)
        {
            while (!_stop.IsCancellationRequested)
            {
                ILinkConnection connection;
                try { connection = listener.Accept(); }
                catch { break; }
                if (connection == null) break;

                Follower follower = new Follower
                {
                    Connection = connection,
                    Writer = new StreamWriter(connection.Stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }
                };
                new Thread(() => Serve(follower)) { IsBackground = true, Name = "GymClock link follower" }.Start();
            }
        }

        private void Serve(Follower follower)
        {
            bool joined = false;
            try
            {
                // The first line must be a HELLO signed with our link code, and promptly.
                follower.Connection.SetReceiveTimeout(5000);
                using (StreamReader reader = new StreamReader(follower.Connection.Stream, Encoding.UTF8))
                {
                    string first = reader.ReadLine();
                    if (!LinkProtocol.TryDecode(first, _key, out LinkMessage hello) || hello.Type != LinkMessage.Hello)
                    {
                        try { lock (follower.SendLock) follower.Writer.WriteLine(LinkProtocol.DeniedLine); } catch { }
                        return;
                    }

                    follower.Name = string.IsNullOrWhiteSpace(hello.Name) ? "Second screen" : hello.Name;
                    lock (_followers) _followers.Add(follower);
                    joined = true;
                    FollowerJoined?.Invoke(follower.Name);
                    FollowersChanged?.Invoke();

                    // Pings arrive every second or so; a long silence means the screen has gone.
                    follower.Connection.SetReceiveTimeout(15000);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!LinkProtocol.TryDecode(line, _key, out LinkMessage message)) continue;
                        if (message.Type == LinkMessage.Ping)
                        {
                            Send(follower, LinkProtocol.Encode(new LinkMessage
                            {
                                Type = LinkMessage.Pong, T0 = message.T0, LeaderTimeMs = NowMs,
                                Seq = Interlocked.Increment(ref _seq)
                            }, _key));
                        }
                        else if (message.Type == LinkMessage.Bye)
                        {
                            break;
                        }
                        // Anything else from a second screen is ignored: they have no control.
                    }
                }
            }
            catch { /* dropped or timed out */ }
            finally
            {
                follower.Alive = false;
                try { follower.Connection.Dispose(); } catch { }
                if (joined)
                {
                    lock (_followers) _followers.Remove(follower);
                    FollowersChanged?.Invoke();
                }
            }
        }

        private static void Send(Follower follower, string line)
        {
            if (!follower.Alive) return;
            try
            {
                lock (follower.SendLock) follower.Writer.WriteLine(line);
            }
            catch
            {
                follower.Alive = false;
                try { follower.Connection.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            if (_stop.IsCancellationRequested) return;
            try
            {
                Broadcast(new LinkMessage { Type = LinkMessage.Bye });
            }
            catch { }
            _stop.Cancel();
            lock (_listeners)
            {
                foreach (ILinkListener listener in _listeners) { try { listener.Dispose(); } catch { } }
                _listeners.Clear();
            }
            lock (_followers)
            {
                foreach (Follower follower in _followers) { try { follower.Connection.Dispose(); } catch { } }
                _followers.Clear();
            }
        }
    }
}
