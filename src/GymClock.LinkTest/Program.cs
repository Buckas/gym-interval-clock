using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace GymClock.LinkTest
{
    /// <summary>
    /// Phase 0 network test for linked displays (docs/plans/linked-displays.md).
    ///
    /// Run it on two PCs on the same network: one as the MAIN PC (leader), one as the SECOND
    /// SCREEN (follower). It checks the three things linked displays need:
    ///   1. the PCs can find each other (UDP broadcast on port 47215),
    ///   2. a connection holds (TCP on port 47216, 4 heartbeats a second), and
    ///   3. their clocks can be matched closely (ping/pong timing).
    /// Both PCs print a shared countdown once a second so the sync can be checked by eye,
    /// and the follower saves a short report file with the result.
    /// </summary>
    internal static class Program
    {
        private const int DiscoveryPort = 47215;
        private const int DataPort = 47216;
        private const int ProtocolVersion = 1;
        private const int HeartbeatMs = 250;
        private const int TestSeconds = 30;

        private static readonly object ConsoleLock = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static int _autoExitSeconds; // for unattended runs: --seconds N

        private static int Main(string[] args)
        {
            List<string> words = new List<string>(args);
            int secondsFlag = words.FindIndex(w => w.Equals("--seconds", StringComparison.OrdinalIgnoreCase));
            if (secondsFlag >= 0 && secondsFlag + 1 < words.Count)
            {
                int.TryParse(words[secondsFlag + 1], out _autoExitSeconds);
                words.RemoveRange(secondsFlag, 2);
            }

            Console.WriteLine("Gym Interval Clock - linked displays network test (Phase 0)");
            Console.WriteLine("This PC: " + Environment.MachineName + "   Addresses: " + string.Join(", ", LocalAddresses().Select(a => a.Address.ToString())));
            Console.WriteLine();

            string choice = words.Count > 0 ? words[0].ToLowerInvariant() : null;
            string address = words.Count > 1 ? words[1] : null;

            if (choice == null)
            {
                Console.WriteLine("Run this on two PCs on the same network. What is this PC?");
                Console.WriteLine("  1) The MAIN PC (start this one first)");
                Console.WriteLine("  2) The SECOND SCREEN - find the main PC automatically");
                Console.WriteLine("  3) The SECOND SCREEN - type the main PC's address");
                Console.Write("Type 1, 2 or 3 and press Enter: ");
                string reply = (Console.ReadLine() ?? string.Empty).Trim();
                if (reply == "1") choice = "lead";
                else if (reply == "2") choice = "follow";
                else if (reply == "3")
                {
                    choice = "follow";
                    Console.Write("Main PC's address (shown on its screen, e.g. 192.168.1.20): ");
                    address = (Console.ReadLine() ?? string.Empty).Trim();
                }
                else
                {
                    Console.WriteLine("Nothing chosen. Closing.");
                    Pause();
                    return 1;
                }
                Console.WriteLine();
            }

            try
            {
                if (choice == "lead") return RunLeader();
                if (choice == "follow") return RunFollower(address);
                Console.WriteLine("Usage: GymClock.LinkTest [lead | follow [address]] [--seconds N]");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("The test stopped with an error: " + ex.Message);
                Pause();
                return 2;
            }
        }

        // =================================================================== leader

        private sealed class FollowerLink
        {
            public string Name = "?";
            public TcpClient Client;
            public StreamWriter Writer;
            public readonly object SendLock = new object();
            public volatile bool Alive = true;
        }

        private static int RunLeader()
        {
            List<FollowerLink> followers = new List<FollowerLink>();
            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Any, DataPort);
                listener.Start();
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Could not open port " + DataPort + " (" + ex.SocketErrorCode + "). Is the test already running on this PC?");
                Pause();
                return 2;
            }

            Console.WriteLine("MAIN PC is ready. On the second PC, run this test and choose 2.");
            Console.WriteLine("If the second PC can't find this one, choose 3 there and type one of these addresses:");
            foreach (UnicastIPAddressInformation a in LocalAddresses()) Console.WriteLine("    " + a.Address);
            Console.WriteLine();
            Console.WriteLine("If Windows asks about the firewall, choose Allow.");
            Console.WriteLine("Press Enter to stop.");
            Console.WriteLine();

            CancellationTokenSource stop = new CancellationTokenSource();

            // 1. Discovery: announce on every network, every 2 seconds.
            Thread announce = new Thread(() => AnnounceLoop(stop.Token)) { IsBackground = true };
            announce.Start();

            // 2. Accept followers.
            Thread accept = new Thread(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = listener.AcceptTcpClient(); }
                    catch { break; }
                    client.NoDelay = true;
                    FollowerLink link = new FollowerLink
                    {
                        Client = client,
                        Writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }
                    };
                    lock (followers) followers.Add(link);
                    new Thread(() => ServeFollower(link, followers)) { IsBackground = true }.Start();
                }
            }) { IsBackground = true };
            accept.Start();

            // 3. Heartbeats, 4 a second, to every follower.
            Thread heartbeat = new Thread(() =>
            {
                long seq = 0;
                long next = Clock.ElapsedMilliseconds;
                while (!stop.IsCancellationRequested)
                {
                    next += HeartbeatMs;
                    long wait = next - Clock.ElapsedMilliseconds;
                    if (wait > 0) Thread.Sleep((int)wait);
                    seq++;
                    string line = Json(new Dictionary<string, object>
                    {
                        { "v", ProtocolVersion }, { "type", "STATE" }, { "seq", seq }, { "leaderTimeMs", Clock.ElapsedMilliseconds }
                    });
                    FollowerLink[] current;
                    lock (followers) current = followers.ToArray();
                    foreach (FollowerLink f in current) Send(f, line);
                }
            }) { IsBackground = true };
            heartbeat.Start();

            // 4. The shared countdown, printed on each whole second of the leader's clock.
            Thread ticker = new Thread(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    long now = Clock.ElapsedMilliseconds;
                    Thread.Sleep((int)(1000 - now % 1000));
                    long shared = Clock.ElapsedMilliseconds;
                    string names;
                    lock (followers) names = followers.Count == 0 ? "none yet" : string.Join(", ", followers.Select(f => f.Name));
                    Log("shared clock " + SharedClockText(shared) + "   second screens connected: " + names);
                }
            }) { IsBackground = true };
            ticker.Start();

            WaitForEnterOrTimeout();
            stop.Cancel();
            try { listener.Stop(); } catch { }
            lock (followers) foreach (FollowerLink f in followers) { try { f.Client.Close(); } catch { } }
            return 0;
        }

        private static void AnnounceLoop(CancellationToken stop)
        {
            using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.EnableBroadcast = true;
                while (!stop.IsCancellationRequested)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(Json(new Dictionary<string, object>
                    {
                        { "v", ProtocolVersion }, { "type", "ANNOUNCE" }, { "name", Environment.MachineName }, { "port", DataPort }
                    }));

                    // The general broadcast address, plus each network's own (some routers only pass one of them).
                    HashSet<IPAddress> targets = new HashSet<IPAddress> { IPAddress.Broadcast };
                    foreach (UnicastIPAddressInformation a in LocalAddresses())
                    {
                        IPAddress directed = DirectedBroadcast(a);
                        if (directed != null) targets.Add(directed);
                    }
                    foreach (IPAddress target in targets)
                    {
                        try { udp.Send(bytes, bytes.Length, new IPEndPoint(target, DiscoveryPort)); } catch { }
                    }

                    if (stop.WaitHandle.WaitOne(2000)) break;
                }
            }
        }

        private static void ServeFollower(FollowerLink link, List<FollowerLink> followers)
        {
            try
            {
                using (StreamReader reader = new StreamReader(link.Client.GetStream(), Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        using (JsonDocument doc = JsonDocument.Parse(line))
                        {
                            JsonElement root = doc.RootElement;
                            string type = root.GetProperty("type").GetString();
                            if (type == "HELLO")
                            {
                                link.Name = root.GetProperty("name").GetString();
                                Log(">>> " + link.Name + " connected from " + ((IPEndPoint)link.Client.Client.RemoteEndPoint).Address);
                            }
                            else if (type == "PING")
                            {
                                Send(link, Json(new Dictionary<string, object>
                                {
                                    { "v", ProtocolVersion }, { "type", "PONG" }, { "t0", root.GetProperty("t0").GetInt64() },
                                    { "leaderTimeMs", Clock.ElapsedMilliseconds }
                                }));
                            }
                            else if (type == "BYE") break;
                        }
                    }
                }
            }
            catch { /* connection dropped */ }
            finally
            {
                link.Alive = false;
                lock (followers) followers.Remove(link);
                try { link.Client.Close(); } catch { }
                Log("<<< " + link.Name + " disconnected");
            }
        }

        private static void Send(FollowerLink link, string line)
        {
            if (!link.Alive) return;
            try { lock (link.SendLock) link.Writer.WriteLine(line); }
            catch { link.Alive = false; try { link.Client.Close(); } catch { } }
        }

        // =================================================================== follower

        private static int RunFollower(string typedAddress)
        {
            StringBuilder report = new StringBuilder();
            void Note(string text) { Console.WriteLine(text); report.AppendLine(text); }

            report.AppendLine("Gym Interval Clock - linked displays network test (Phase 0)");
            report.AppendLine("Date: " + DateTime.Now.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture));
            report.AppendLine("Second screen PC: " + Environment.MachineName + "   Addresses: " + string.Join(", ", LocalAddresses().Select(a => a.Address.ToString())));
            report.AppendLine();

            // ---- 1. Find the main PC
            IPAddress leader = null;
            string leaderName = null;
            bool foundByBroadcast = false;
            bool searched = false;
            double discoverySeconds = 0;

            if (string.IsNullOrWhiteSpace(typedAddress))
            {
                Console.WriteLine("Looking for the main PC (up to 15 seconds). If Windows asks about the firewall, choose Allow.");
                searched = true;
                Stopwatch search = Stopwatch.StartNew();
                if (Discover(15000, out leader, out leaderName))
                {
                    foundByBroadcast = true;
                    discoverySeconds = search.Elapsed.TotalSeconds;
                    Note("1. FIND:     OK - found main PC \"" + leaderName + "\" at " + leader + " in " + discoverySeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                }
                else
                {
                    Note("1. FIND:     NOT FOUND - no announcement arrived in 15 s (the network may block broadcast, or the main PC's firewall is blocking).");
                    if (Console.IsInputRedirected) { SaveReport(report); return 3; }
                    Console.Write("   Type the main PC's address to carry on (or just Enter to stop): ");
                    typedAddress = (Console.ReadLine() ?? string.Empty).Trim();
                    if (typedAddress.Length == 0) { SaveReport(report); Pause(); return 3; }
                }
            }

            if (leader == null)
            {
                if (!IPAddress.TryParse(typedAddress, out leader))
                {
                    try { leader = Dns.GetHostAddresses(typedAddress).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork); } catch { }
                }
                if (leader == null)
                {
                    Note("   \"" + typedAddress + "\" is not an address I can use.");
                    SaveReport(report); Pause();
                    return 3;
                }
                Note("1. FIND:     using the typed address " + leader + (searched ? string.Empty : " (automatic finding was not tested)"));
            }

            // ---- 2. Connect
            TcpClient client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            try
            {
                if (!client.ConnectAsync(leader, DataPort).Wait(5000)) throw new TimeoutException("no answer in 5 s");
            }
            catch (Exception ex)
            {
                Note("2. CONNECT:  FAILED - could not connect to " + leader + ":" + DataPort + " (" + (ex.InnerException ?? ex).Message + ").");
                Note("             The network may stop PCs talking to each other (client isolation), or the main PC's firewall blocked it.");
                Note(string.Empty);
                Note("RESULT: NOT WORKING on this network. Try a phone hotspot, or ask IT about ports " + DiscoveryPort + " (UDP) and " + DataPort + " (TCP).");
                SaveReport(report); Pause();
                return 4;
            }
            Note("2. CONNECT:  OK - connected to " + leader + ":" + DataPort);

            StreamWriter writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            object sendLock = new object();
            void SendLine(string line) { lock (sendLock) writer.WriteLine(line); }
            SendLine(Json(new Dictionary<string, object> { { "v", ProtocolVersion }, { "type", "HELLO" }, { "name", Environment.MachineName } }));

            // ---- 3. Measure for TestSeconds: ping times, clock offset, heartbeat delay
            object statsLock = new object();
            List<long> rtts = new List<long>();
            List<(long rtt, long offset)> recent = new List<(long, long)>();
            long offsetMs = 0;            // leader clock = local clock + offset
            bool haveOffset = false;
            List<long> heartbeatDelays = new List<long>();
            long heartbeats = 0, missed = 0, lastSeq = -1, lastArrival = -1, maxGap = 0;
            bool connected = true;

            Thread reader = new Thread(() =>
            {
                try
                {
                    using (StreamReader r = new StreamReader(client.GetStream(), Encoding.UTF8))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            long now = Clock.ElapsedMilliseconds;
                            using (JsonDocument doc = JsonDocument.Parse(line))
                            {
                                JsonElement root = doc.RootElement;
                                string type = root.GetProperty("type").GetString();
                                if (type == "PONG")
                                {
                                    long t0 = root.GetProperty("t0").GetInt64();
                                    long leaderTime = root.GetProperty("leaderTimeMs").GetInt64();
                                    long rtt = now - t0;
                                    long offset = leaderTime + rtt / 2 - now;
                                    lock (statsLock)
                                    {
                                        rtts.Add(rtt);
                                        recent.Add((rtt, offset));
                                        if (recent.Count > 8) recent.RemoveAt(0);
                                        // The quickest recent round trip gives the most trustworthy offset.
                                        offsetMs = recent.OrderBy(x => x.rtt).First().offset;
                                        haveOffset = true;
                                    }
                                }
                                else if (type == "STATE")
                                {
                                    long seq = root.GetProperty("seq").GetInt64();
                                    long leaderTime = root.GetProperty("leaderTimeMs").GetInt64();
                                    lock (statsLock)
                                    {
                                        heartbeats++;
                                        if (lastSeq >= 0 && seq > lastSeq + 1) missed += seq - lastSeq - 1;
                                        lastSeq = seq;
                                        if (lastArrival >= 0) maxGap = Math.Max(maxGap, now - lastArrival);
                                        lastArrival = now;
                                        if (haveOffset) heartbeatDelays.Add(Math.Max(0, now + offsetMs - leaderTime));
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
                connected = false;
            }) { IsBackground = true };
            reader.Start();

            Thread pinger = new Thread(() =>
            {
                while (connected)
                {
                    try
                    {
                        SendLine(Json(new Dictionary<string, object> { { "v", ProtocolVersion }, { "type", "PING" }, { "t0", Clock.ElapsedMilliseconds } }));
                    }
                    catch { break; }
                    Thread.Sleep(1000);
                }
            }) { IsBackground = true };
            pinger.Start();

            Console.WriteLine();
            Console.WriteLine("Measuring for " + TestSeconds + " seconds. Compare the \"shared clock\" below with the main PC's screen -");
            Console.WriteLine("the two should change at the same moment.");
            Console.WriteLine();

            Stopwatch test = Stopwatch.StartNew();
            bool summarised = false;
            bool enterPressed = false;
            new Thread(() => { if (!Console.IsInputRedirected) { Console.ReadLine(); enterPressed = true; } }) { IsBackground = true }.Start();

            while (connected && !enterPressed)
            {
                // Sleep to the next whole second of the *shared* (leader) clock, so both PCs print together.
                long offset; bool known;
                lock (statsLock) { offset = offsetMs; known = haveOffset; }
                long sharedNow = Clock.ElapsedMilliseconds + offset;
                Thread.Sleep((int)(1000 - ((sharedNow % 1000) + 1000) % 1000));

                lock (statsLock)
                {
                    long shared = Clock.ElapsedMilliseconds + offsetMs;
                    long ping = rtts.Count > 0 ? rtts[rtts.Count - 1] : -1;
                    Log("shared clock " + (known ? SharedClockText(shared) : "--:--") + "   ping " + (ping < 0 ? "-" : ping + " ms")
                        + "   heartbeats " + heartbeats + (missed > 0 ? " (" + missed + " missed)" : string.Empty));
                }

                if (!summarised && test.Elapsed.TotalSeconds >= TestSeconds)
                {
                    summarised = true;
                    Console.WriteLine();
                    lock (statsLock)
                    {
                        WriteSummary(Note, foundByBroadcast, searched, rtts, heartbeatDelays, heartbeats, missed, maxGap, test.Elapsed.TotalSeconds);
                    }
                    SaveReport(report);
                    Console.WriteLine();
                    if (_autoExitSeconds > 0) break;
                    Console.WriteLine("The test carries on so you can keep comparing the shared clock. Press Enter to stop.");
                    Console.WriteLine();
                }
                if (_autoExitSeconds > 0 && test.Elapsed.TotalSeconds >= _autoExitSeconds) break;
            }

            if (!connected && !enterPressed)
            {
                Note(string.Empty);
                Note("The connection to the main PC was lost after " + test.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s.");
                if (!summarised)
                {
                    lock (statsLock) WriteSummary(Note, foundByBroadcast, searched, rtts, heartbeatDelays, heartbeats, missed, maxGap, test.Elapsed.TotalSeconds);
                    SaveReport(report);
                }
                Pause();
            }

            try { SendLine(Json(new Dictionary<string, object> { { "v", ProtocolVersion }, { "type", "BYE" } })); } catch { }
            try { client.Close(); } catch { }
            return 0;
        }

        private static void WriteSummary(Action<string> note, bool foundByBroadcast, bool searched, List<long> rtts, List<long> delays,
            long heartbeats, long missed, long maxGap, double seconds)
        {
            if (rtts.Count == 0 || heartbeats == 0)
            {
                note("3. TIMING:   no measurements arrived.");
                note(string.Empty);
                note("RESULT: NOT WORKING on this network.");
                return;
            }

            double avgPing = rtts.Average();
            long maxPing = rtts.Max();
            double avgDelay = delays.Count > 0 ? delays.Average() : 0;
            long maxDelay = delays.Count > 0 ? delays.Max() : 0;
            long expected = (long)(seconds * 1000 / HeartbeatMs);

            note("3. TIMING over " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s:");
            note("   Ping (there and back):   average " + avgPing.ToString("0", CultureInfo.InvariantCulture) + " ms, slowest " + maxPing + " ms  (" + rtts.Count + " pings)");
            note("   Heartbeat delay:         average " + avgDelay.ToString("0", CultureInfo.InvariantCulture) + " ms, slowest " + maxDelay + " ms");
            note("   Heartbeats received:     " + heartbeats + " of about " + expected + (missed > 0 ? ", " + missed + " missed" : ", none missed"));
            note("   Longest gap between heartbeats: " + maxGap + " ms (normal is " + HeartbeatMs + " ms)");
            note(string.Empty);

            // The plan's target: screens within 100 ms of each other.
            bool timingGood = avgPing <= 50 && avgDelay <= 50 && maxGap <= 1000 && missed == 0;
            bool timingOk = avgPing <= 150 && avgDelay <= 100 && maxGap <= 2000;

            if (timingGood && foundByBroadcast)
                note("RESULT: GOOD - this network will work for linked displays, including finding the main PC automatically.");
            else if (timingGood && searched)
                note("RESULT: GOOD, BY ADDRESS - timing is fine, but the second screen will need the main PC's address typed in on this network.");
            else if (timingGood)
                note("RESULT: GOOD - timing is fine using the typed address. Run it again with option 2 to test finding the main PC automatically.");
            else if (timingOk)
                note("RESULT: USABLE - it works, but the timing is slower or less steady than ideal (target: under 100 ms). A hotspot or cable may be better.");
            else
                note("RESULT: POOR - the connection is too slow or unsteady for screens to stay in step. Try a phone hotspot or a network cable.");
        }

        private static bool Discover(int timeoutMs, out IPAddress leader, out string name)
        {
            leader = null;
            name = null;
            try
            {
                using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork))
                {
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                    Stopwatch waited = Stopwatch.StartNew();
                    while (waited.ElapsedMilliseconds < timeoutMs)
                    {
                        udp.Client.ReceiveTimeout = (int)Math.Max(100, timeoutMs - waited.ElapsedMilliseconds);
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] bytes;
                        try { bytes = udp.Receive(ref from); }
                        catch (SocketException) { break; }

                        try
                        {
                            using (JsonDocument doc = JsonDocument.Parse(bytes))
                            {
                                JsonElement root = doc.RootElement;
                                if (root.GetProperty("type").GetString() != "ANNOUNCE") continue;
                                name = root.GetProperty("name").GetString();
                                leader = from.Address;
                                return true;
                            }
                        }
                        catch { /* not one of ours */ }
                    }
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine("   (Could not listen on port " + DiscoveryPort + ": " + ex.SocketErrorCode + ")");
            }
            return false;
        }

        // =================================================================== helpers

        /// <summary>A 60-second countdown driven by the leader's clock, e.g. "0:42.".</summary>
        private static string SharedClockText(long sharedMs)
        {
            long second = (long)Math.Round(sharedMs / 1000.0);
            long remaining = 59 - (((second % 60) + 60) % 60);
            return "0:" + remaining.ToString("00", CultureInfo.InvariantCulture);
        }

        private static List<UnicastIPAddressInformation> LocalAddresses()
        {
            List<UnicastIPAddressInformation> result = new List<UnicastIPAddressInformation>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation a in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)) continue; // no network on that adapter
                        result.Add(a);
                    }
                }
            }
            catch { }
            return result;
        }

        private static IPAddress DirectedBroadcast(UnicastIPAddressInformation a)
        {
            try
            {
                byte[] ip = a.Address.GetAddressBytes();
                byte[] mask = a.IPv4Mask.GetAddressBytes();
                byte[] broadcast = new byte[4];
                for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                return new IPAddress(broadcast);
            }
            catch { return null; }
        }

        private static string Json(Dictionary<string, object> message)
        {
            return JsonSerializer.Serialize(message);
        }

        private static void Log(string text)
        {
            lock (ConsoleLock)
            {
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text);
            }
        }

        private static void SaveReport(StringBuilder report)
        {
            string name = "LinkTest-report-" + Environment.MachineName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";
            foreach (string folder in new[] { AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.GetTempPath() })
            {
                try
                {
                    string path = Path.Combine(folder, name);
                    File.WriteAllText(path, report.ToString());
                    Console.WriteLine("Report saved: " + path);
                    return;
                }
                catch { /* try the next folder */ }
            }
            Console.WriteLine("(The report could not be saved - please copy the text above.)");
        }

        private static void WaitForEnterOrTimeout()
        {
            if (_autoExitSeconds > 0) { Thread.Sleep(_autoExitSeconds * 1000); return; }
            if (Console.IsInputRedirected) { Thread.Sleep(Timeout.Infinite); return; }
            Console.ReadLine();
        }

        private static void Pause()
        {
            if (Console.IsInputRedirected || _autoExitSeconds > 0) return;
            Console.WriteLine();
            Console.Write("Press Enter to close.");
            Console.ReadLine();
        }
    }
}
