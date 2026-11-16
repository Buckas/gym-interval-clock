using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using GymClock.Link;
using Xunit;
using Xunit.Abstractions;

namespace GymClock.Tests
{
    /// <summary>
    /// Linked displays over Bluetooth (plan Phase 4). A real Bluetooth connection needs two paired
    /// PCs, so it cannot be tested here. What is tested: the link running over a transport that is
    /// not the network, the address/structure handling, and - on a PC with Bluetooth - that the
    /// main PC's listener really starts, advertises and stops.
    /// </summary>
    [Collection("LinkNetwork")]
    public class LinkBluetoothTests
    {
        private readonly ITestOutputHelper _output;
        public LinkBluetoothTests(ITestOutputHelper output) { _output = output; }

        private static bool WaitFor(Func<bool> condition, int timeoutMs = 8000)
        {
            Stopwatch waited = Stopwatch.StartNew();
            while (waited.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(20);
            }
            return condition();
        }

        // ---------------------------------------------------------------- a stand-in transport (two pipes = one two-way stream)

        private sealed class DuplexStream : Stream
        {
            private readonly Stream _read, _write;
            public DuplexStream(Stream read, Stream write) { _read = read; _write = write; }
            public override int Read(byte[] buffer, int offset, int count) { return _read.Read(buffer, offset, count); }
            public override void Write(byte[] buffer, int offset, int count) { _write.Write(buffer, offset, count); _write.Flush(); }
            public override void Flush() { _write.Flush(); }
            protected override void Dispose(bool disposing) { try { _read.Dispose(); } catch { } try { _write.Dispose(); } catch { } base.Dispose(disposing); }
            public override bool CanRead { get { return true; } }
            public override bool CanWrite { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
        }

        private sealed class FakeConnection : ILinkConnection
        {
            public Stream Stream { get; set; }
            public string Remote { get { return "fake"; } }
            public void SetReceiveTimeout(int milliseconds) { }
            public void Dispose() { Stream.Dispose(); }
        }

        /// <summary>A "radio" in memory: Connect() on the second screen's side hands the other end to the main PC's Accept().</summary>
        private sealed class FakeTransport : ILinkListener
        {
            private readonly BlockingCollection<ILinkConnection> _incoming = new BlockingCollection<ILinkConnection>();
            public bool FailToStart;
            public string Kind { get { return "Fake radio"; } }

            public bool Start(out string error)
            {
                error = FailToStart ? "switched off." : null;
                return !FailToStart;
            }

            public ILinkConnection Accept()
            {
                try { return _incoming.Take(); } catch (InvalidOperationException) { return null; }
            }

            public ILinkConnection Connect()
            {
                AnonymousPipeServerStream toMain = new AnonymousPipeServerStream(PipeDirection.Out);
                AnonymousPipeClientStream mainReads = new AnonymousPipeClientStream(PipeDirection.In, toMain.ClientSafePipeHandle);
                AnonymousPipeServerStream toSecond = new AnonymousPipeServerStream(PipeDirection.Out);
                AnonymousPipeClientStream secondReads = new AnonymousPipeClientStream(PipeDirection.In, toSecond.ClientSafePipeHandle);

                _incoming.Add(new FakeConnection { Stream = new DuplexStream(mainReads, toSecond) });
                return new FakeConnection { Stream = new DuplexStream(secondReads, toMain) };
            }

            public void Dispose() { _incoming.CompleteAdding(); }
        }

        [Fact]
        public void TheLinkRunsOverAnyTransport_NotJustTheNetwork()
        {
            FakeTransport radio = new FakeTransport();
            using (LinkLeader leader = new LinkLeader("MAIN", "4821", 47416, 47415))
            using (LinkFollower follower = new LinkFollower("SECOND", "4821", stop => radio.Connect()))
            {
                leader.AddListener(radio);
                Assert.True(leader.Start(), leader.StartError);
                Assert.Equal(new[] { "network", "Fake radio" }, leader.ActiveTransports);
                Assert.Empty(leader.Warnings);

                string script = null;
                LinkMessage state = null;
                follower.ProgramReceived += (settings, s) => script = s;
                follower.StateReceived += m => state = m;
                follower.Start();

                Assert.True(WaitFor(() => leader.FollowerNames.Count == 1), "the second screen never joined over the fake radio");
                leader.Broadcast(new LinkMessage { Type = LinkMessage.Program, Name = "MAIN", Settings = "prep=5", Script = "PROGRAM \"T\"" });
                leader.Broadcast(new LinkMessage { Type = LinkMessage.State, Phase = 2, Index = 4, PhaseElapsedMs = 100, LeaderTimeMs = leader.NowMs });

                Assert.True(WaitFor(() => script != null && state != null), "program/state did not arrive");
                Assert.Equal("PROGRAM \"T\"", script);
                Assert.Equal(4, state.Index);
                Assert.Equal("MAIN", follower.LeaderName);
                Assert.True(WaitFor(() => follower.TryLeaderNow(out _)), "the clocks were never matched");
                Assert.Equal(FollowerStatus.Connected, follower.Status);
            }
        }

        [Fact]
        public void WrongCode_IsRefusedOverAnyTransport()
        {
            FakeTransport radio = new FakeTransport();
            using (LinkLeader leader = new LinkLeader("MAIN", "4821", 47416, 47415))
            using (LinkFollower follower = new LinkFollower("SECOND", "0000", stop => radio.Connect()))
            {
                leader.AddListener(radio);
                Assert.True(leader.Start(), leader.StartError);
                follower.Start();
                Assert.True(WaitFor(() => follower.Status == FollowerStatus.WrongCode), "status was " + follower.Status);
                Assert.Empty(leader.FollowerNames);
            }
        }

        [Fact]
        public void AnExtraTransportThatCannotStart_IsAWarning_NotAFailure()
        {
            using (LinkLeader leader = new LinkLeader("MAIN", "4821", 47416, 47415))
            {
                leader.AddListener(new FakeTransport { FailToStart = true });
                Assert.True(leader.Start(), leader.StartError); // the network link still runs
                Assert.Equal(new[] { "network" }, leader.ActiveTransports);
                Assert.Equal(new[] { "Fake radio: switched off." }, leader.Warnings);
            }
        }

        // ---------------------------------------------------------------- Bluetooth details

        [Theory]
        [InlineData("00:1A:7D:DA:71:13", 0x001A7DDA7113UL)]
        [InlineData("00-1a-7d-da-71-13", 0x001A7DDA7113UL)]
        [InlineData("001A7DDA7113", 0x001A7DDA7113UL)]
        public void Address_ParsesAndFormats(string text, ulong expected)
        {
            Assert.True(BluetoothLink.TryParseAddress(text, out ulong address));
            Assert.Equal(expected, address);
            Assert.Equal("00:1A:7D:DA:71:13", BluetoothLink.FormatAddress(address));
        }

        [Theory]
        [InlineData("")]
        [InlineData("192.168.1.20")]
        [InlineData("00:1A:7D:DA:71")]
        [InlineData("00:00:00:00:00:00")]
        [InlineData("ZZ:1A:7D:DA:71:13")]
        public void Address_RejectsAnythingElse(string text)
        {
            Assert.False(BluetoothLink.TryParseAddress(text, out _));
        }

        // ---------------------------------------------------------------- the second screen works the route out itself

        private static readonly BluetoothDevice FakeMainPc = new BluetoothDevice { Address = 0x001A7DDA7113UL, Name = "GYM-MAIN", IsComputer = true };

        [Fact]
        public void Automatic_FindsTheMainPcOnTheNetwork_WithOnlyTheCode()
        {
            using (LinkLeader leader = new LinkLeader("MAIN", "4821", 47416, 47415))
            using (LinkFollower follower = new LinkFollower("SECOND", "4821", new LinkRoutePlan { UseBluetooth = false }, 47416, 47415))
            {
                Assert.True(leader.Start(), leader.StartError);
                follower.Start();

                Assert.True(WaitFor(() => leader.FollowerNames.Count == 1, 12000), "not found; doing: " + follower.Activity);
                leader.Broadcast(new LinkMessage { Type = LinkMessage.State, Phase = 0 });
                Assert.True(WaitFor(() => follower.Status == FollowerStatus.Connected));
                Assert.Equal("network", follower.ConnectedVia);
                Assert.StartsWith("tcp:", follower.ConnectedRoute);
                Assert.Equal(new[] { "SECOND (network)" }, leader.FollowerDescriptions);
            }
        }

        [Fact]
        public void Automatic_FallsBackToBluetooth_WhenTheNetworkHasNoMainPc()
        {
            FakeTransport radio = new FakeTransport();
            List<ulong> dialled = new List<ulong>();
            LinkRoutePlan plan = new LinkRoutePlan
            {
                UseNetwork = false, // as if the two PCs share no network
                PairedComputers = () => new[] { FakeMainPc },
                BluetoothConnect = (address, stop) => { dialled.Add(address); return radio.Connect(); }
            };

            using (LinkLeader leader = new LinkLeader("MAIN", "4821", 47416, 47415))
            using (LinkFollower follower = new LinkFollower("SECOND", "4821", plan, 47416, 47415))
            {
                leader.AddListener(radio);
                Assert.True(leader.Start(), leader.StartError);
                follower.Start();

                Assert.True(WaitFor(() => leader.FollowerNames.Count == 1), "never linked; doing: " + follower.Activity);
                leader.Broadcast(new LinkMessage { Type = LinkMessage.State, Phase = 0 });
                Assert.True(WaitFor(() => follower.Status == FollowerStatus.Connected));
                Assert.Equal("Bluetooth", follower.ConnectedVia);
                Assert.Equal("bt:00:1A:7D:DA:71:13", follower.ConnectedRoute);   // remembered for next time
                Assert.Equal(new[] { FakeMainPc.Address }, dialled.ToArray());
                Assert.Equal(new[] { "SECOND (Fake radio)" }, leader.FollowerDescriptions);
            }
        }

        [Fact]
        public void Automatic_TriesTheRouteThatWorkedLastTime_First()
        {
            List<string> seen = new List<string>();
            using (LinkLeader leader = new LinkLeader("MAIN", "4821", 47416, 47415))
            using (LinkFollower follower = new LinkFollower("SECOND", "4821",
                new LinkRoutePlan { LastGoodRoute = "tcp:127.0.0.1", UseNetwork = false, UseBluetooth = false }, 47416, 47415))
            {
                follower.StatusChanged += () => { lock (seen) seen.Add(follower.Activity); };
                Assert.True(leader.Start(), leader.StartError);
                follower.Start();

                Assert.True(WaitFor(() => leader.FollowerNames.Count == 1), "the remembered route was not used");
                lock (seen) Assert.Contains("Connecting to the main PC at 127.0.0.1...", seen);
            }
        }

        [Fact]
        public void Automatic_KeepsTrying_AndSaysWhatItIsDoing_WhenThereIsNoMainPc()
        {
            List<string> seen = new List<string>();
            LinkRoutePlan plan = new LinkRoutePlan
            {
                TypedAddress = "127.0.0.1", UseNetwork = false,
                PairedComputers = () => new[] { FakeMainPc },
                BluetoothConnect = (address, stop) => null // out of range
            };
            using (LinkFollower follower = new LinkFollower("SECOND", "4821", plan, 47416, 47415))
            {
                follower.StatusChanged += () => { lock (seen) seen.Add(follower.Activity); };
                follower.Start();

                Assert.True(WaitFor(() => follower.FailedRounds >= 1, 15000), "no round finished");
                Assert.Equal(FollowerStatus.Searching, follower.Status);
                lock (seen)
                {
                    Assert.Contains("Connecting to the main PC at 127.0.0.1...", seen);
                    Assert.Contains("Trying Bluetooth (GYM-MAIN)...", seen);
                    Assert.Contains("The main PC was not found. Trying again...", seen);
                }
            }
        }

        // ---------------------------------------------------------------- the built-in guide and the setup window

        [Fact]
        public void Help_CoversSetupOnANetwork_OverBluetooth_AndTroubleshooting_InTheAppsOwnWords()
        {
            Assert.Equal(new[] { "Quick start", "Network setup", "Bluetooth setup", "Not connecting?", "On a second screen", "Checking it's in step" },
                LinkHelp.Topics.Select(t => t.Title).ToArray());

            string start = string.Join(" ", LinkHelp.Find(LinkHelp.Start).Lines);
            Assert.Contains("\"This is the MAIN PC\"", start);           // the setup window's buttons, word for word
            Assert.Contains("\"This is a SECOND SCREEN\"", start);
            Assert.Contains("\"Turn linking off\"", start);

            string network = string.Join(" ", LinkHelp.Find(LinkHelp.Network).Lines);
            Assert.Contains("firewall", network);
            Assert.Contains("47215", network);
            Assert.Contains("47216", network);
            Assert.Contains("\"Main PC's address\"", network);
            Assert.Contains("hotspot", network);

            string bluetooth = string.Join(" ", LinkHelp.Find(LinkHelp.Bluetooth).Lines);
            Assert.Contains("Bluetooth & devices", bluetooth);
            Assert.Contains("Add device", bluetooth);
            Assert.Contains("Join a Personal Area Network", bluetooth);
            Assert.Contains("Direct connection", bluetooth);

            string trouble = string.Join(" ", LinkHelp.Find(LinkHelp.Trouble).Lines);
            Assert.Contains("Link code not accepted", trouble);
            Assert.Contains("Reconnecting", trouble);
        }

        private static void RunSta(Action action)
        {
            Exception error = null;
            Thread t = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw new Exception(error.ToString(), error);
        }

        [Fact]
        public void HelpWindow_OpensOnEveryTopic()
        {
            RunSta(() =>
            {
                foreach (LinkHelpTopic topic in LinkHelp.Topics)
                {
                    using (LinkHelpForm help = new LinkHelpForm(topic.Id)) { IntPtr h = help.Handle; }
                }
            });
        }

        [Fact]
        public void SetupWindow_MainPcIsOneClick_AndASecondScreenOnlyTypesTheCode()
        {
            RunSta(() =>
            {
                System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                TimerSettings settings = new TimerSettings();
                int applied = 0;
                Func<LinkStatus> status = () => new LinkStatus
                {
                    Role = settings.IsLinkLeader ? "lead" : settings.IsLinkFollower ? "follow" : "off",
                    Code = settings.LinkCode, LeaderRunning = settings.IsLinkLeader
                };
                Action<Action<TimerSettings>> apply = change => { change(settings); applied++; };

                // ---- main PC: one click turns the link on and makes the code
                using (LinkSetupForm setup = new LinkSetupForm(settings, status, apply))
                {
                    IntPtr h = setup.Handle;
                    typeof(LinkSetupForm).GetMethod("BecomeMainPc", priv).Invoke(setup, null);
                    Assert.True(settings.IsLinkLeader);
                    Assert.Equal(4, settings.LinkCode.Length);
                    Assert.Equal(1, applied);
                }

                // ---- second screen: the fourth digit starts the link - no OK button, nothing else to choose
                settings.LinkRole = "off";
                settings.LinkCode = string.Empty;
                applied = 0;
                using (LinkSetupForm setup = new LinkSetupForm(settings, status, apply))
                {
                    IntPtr h = setup.Handle;
                    typeof(LinkSetupForm).GetMethod("ShowPage", priv).Invoke(setup,
                        new object[] { typeof(LinkSetupForm).GetField("_followPage", priv).GetValue(setup) });
                    System.Windows.Forms.TextBox code = (System.Windows.Forms.TextBox)typeof(LinkSetupForm).GetField("_codeBox", priv).GetValue(setup);

                    code.Text = "482";
                    Assert.False(settings.IsLinkFollower);
                    code.Text = "4821";
                    Assert.True(settings.IsLinkFollower);
                    Assert.Equal("4821", settings.LinkCode);
                    Assert.Equal(1, applied);

                    // "More options" are optional extras; applying them changes only what was typed
                    ((System.Windows.Forms.TextBox)typeof(LinkSetupForm).GetField("_stationsFrom", priv).GetValue(setup)).Text = "3";
                    ((System.Windows.Forms.TextBox)typeof(LinkSetupForm).GetField("_stationsTo", priv).GetValue(setup)).Text = "4";
                    Assert.True((bool)typeof(LinkSetupForm).GetMethod("ApplyOptions", priv).Invoke(setup, null));
                    Assert.Equal(3, settings.LinkStationsFrom);
                    Assert.Equal(4, settings.LinkStationsTo);
                    Assert.Equal("4821", settings.LinkCode);
                }
            });
        }

        [Fact]
        public void WindowsStructures_AreTheSizesWindowsExpects()
        {
            Assert.Equal(30, BluetoothLink.SocketAddressSize);  // SOCKADDR_BTH (1-byte packed)
            Assert.Equal(560, BluetoothLink.DeviceInfoSize);    // BLUETOOTH_DEVICE_INFO
            if (IntPtr.Size == 8)
            {
                Assert.Equal(40, BluetoothLink.ServiceAddressSize); // CSADDR_INFO: two padded 16-byte addresses + two ints
                Assert.Equal(120, BluetoothLink.ServiceQuerySize);  // WSAQUERYSETW
            }
        }

        [Fact]
        public void OnThisPc_ListingAndListening_WorkOrFailSoftly()
        {
            bool radio = BluetoothLink.HasRadio();
            List<BluetoothDevice> paired = BluetoothLink.PairedDevices(); // must never throw
            _output.WriteLine("Bluetooth radio: " + radio + ", paired devices: " + paired.Count);
            foreach (BluetoothDevice device in paired) _output.WriteLine("  PAIRED " + device + (device.IsComputer ? "  [computer]" : string.Empty));

            using (BluetoothLinkListener listener = new BluetoothLinkListener())
            {
                bool started = listener.Start(out string error);
                _output.WriteLine("Listener started: " + started + (started ? ", RFCOMM channel " + listener.Channel : ", " + error));

                if (!radio)
                {
                    Assert.False(started);
                    Assert.False(string.IsNullOrEmpty(error));
                    return;
                }

                // With a radio: the main PC's side really opens an RFCOMM channel and advertises the service.
                Assert.True(started, error);
                Assert.InRange(listener.Channel, 1, 30);

                // Closing it wakes a waiting Accept (returns null) instead of leaving a thread stuck.
                ILinkConnection accepted = null;
                bool returned = false;
                Thread waiter = new Thread(() => { accepted = listener.Accept(); returned = true; }) { IsBackground = true };
                waiter.Start();
                Thread.Sleep(200);
                listener.Dispose();
                Assert.True(WaitFor(() => returned, 5000), "Accept did not return after the listener was closed");
                Assert.Null(accepted);
            }

            // A second screen asked to connect to a PC that isn't there gets "no connection", not an exception.
            using (CancellationTokenSource stop = new CancellationTokenSource(1500))
            {
                Assert.Null(BluetoothLinkConnection.Connect(0x0000DEADBEEFUL, stop.Token));
            }
        }
    }
}
