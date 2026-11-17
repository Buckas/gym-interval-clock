using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using GymClock.Link;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>
    /// Linked displays over a real connection, both ends on this PC (loopback): the network classes
    /// by themselves, then two whole sessions linked together (no windows). One at a time - they share test ports.
    /// </summary>
    [Collection("LinkNetwork")]
    public class LinkNetworkTests
    {
        // Not the app's real ports, so a running Gym Interval Clock doesn't get in the way.
        private const int DataPort = 47416;
        private const int DiscoveryPort = 47415;

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

        // ---------------------------------------------------------------- LinkLeader + LinkFollower

        [Fact]
        public void SecondScreen_Connects_GetsProgramAndState_AndMatchesTheClock()
        {
            using (LinkLeader leader = new LinkLeader("MAIN", "4821", DataPort, DiscoveryPort))
            using (LinkFollower follower = new LinkFollower("SECOND", "4821", "127.0.0.1", DataPort, DiscoveryPort))
            {
                string joined = null;
                leader.FollowerJoined += name => joined = name;
                Assert.True(leader.Start(), leader.StartError);

                string script = null;
                LinkMessage state = null;
                follower.ProgramReceived += (settings, s) => script = s;
                follower.StateReceived += m => state = m;
                follower.Start();

                Assert.True(WaitFor(() => joined != null), "the main PC never saw the second screen");
                Assert.Equal("SECOND", joined);
                Assert.Equal(new[] { "SECOND" }, leader.FollowerNames);

                leader.Broadcast(new LinkMessage { Type = LinkMessage.Program, Settings = "prep=5", Script = "PROGRAM \"T\"\nWORK 30s" });
                leader.Broadcast(new LinkMessage { Type = LinkMessage.State, Phase = 2, Index = 1, PhaseElapsedMs = 1500, Paused = false, LeaderTimeMs = leader.NowMs });

                Assert.True(WaitFor(() => script != null && state != null), "program/state did not arrive");
                Assert.Equal("PROGRAM \"T\"\nWORK 30s", script);
                Assert.Equal(1, state.Index);
                Assert.Equal(FollowerStatus.Connected, follower.Status);

                // After the ping burst the second screen knows the main PC's clock to within a few ms.
                Assert.True(WaitFor(() => follower.TryLeaderNow(out _)), "the clocks were never matched");
                Thread.Sleep(300);
                Assert.True(follower.TryLeaderNow(out long estimate));
                Assert.InRange(estimate - leader.NowMs, -25, 25);
                Assert.InRange(follower.RoundTripMs, 0, 50);
            }
        }

        [Fact]
        public void SecondScreen_WithTheWrongCode_IsRefused_AndGetsNothing()
        {
            using (LinkLeader leader = new LinkLeader("MAIN", "4821", DataPort, DiscoveryPort))
            using (LinkFollower follower = new LinkFollower("SECOND", "9999", "127.0.0.1", DataPort, DiscoveryPort))
            {
                Assert.True(leader.Start(), leader.StartError);
                bool gotAnything = false;
                follower.ProgramReceived += (a, b) => gotAnything = true;
                follower.StateReceived += m => gotAnything = true;
                follower.Start();

                Assert.True(WaitFor(() => follower.Status == FollowerStatus.WrongCode), "status was " + follower.Status);
                leader.Broadcast(new LinkMessage { Type = LinkMessage.Program, Script = "PROGRAM \"T\"" });
                Thread.Sleep(300);
                Assert.Empty(leader.FollowerNames);
                Assert.False(gotAnything);
            }
        }

        [Fact]
        public void SecondScreen_FindsTheMainPcByItsAnnouncement()
        {
            using (LinkLeader leader = new LinkLeader("MAIN", "4821", DataPort, DiscoveryPort))
            using (LinkFollower follower = new LinkFollower("SECOND", "4821", string.Empty, DataPort, DiscoveryPort))
            {
                Assert.True(leader.Start(), leader.StartError);
                follower.Start();

                Assert.True(WaitFor(() => leader.FollowerNames.Count == 1, 12000), "not found by announcement; status " + follower.Status);
                Assert.Equal("MAIN", follower.LeaderName);
            }
        }

        [Fact]
        public void SecondScreen_Reconnects_WhenTheMainPcComesBack()
        {
            using (LinkFollower follower = new LinkFollower("SECOND", "4821", "127.0.0.1", DataPort, DiscoveryPort))
            {
                LinkLeader first = new LinkLeader("MAIN", "4821", DataPort, DiscoveryPort);
                Assert.True(first.Start(), first.StartError);
                follower.Start();
                Assert.True(WaitFor(() => first.FollowerNames.Count == 1));
                first.Broadcast(new LinkMessage { Type = LinkMessage.State, Phase = 0 });
                Assert.True(WaitFor(() => follower.Status == FollowerStatus.Connected));

                first.Dispose(); // the main PC closes
                Assert.True(WaitFor(() => follower.Status == FollowerStatus.Reconnecting), "status was " + follower.Status);

                using (LinkLeader second = new LinkLeader("MAIN", "4821", DataPort, DiscoveryPort))
                {
                    Assert.True(WaitFor(() => second.Start(), 5000), second.StartError);
                    Assert.True(WaitFor(() => second.FollowerNames.Count == 1, 12000), "did not reconnect");
                    second.Broadcast(new LinkMessage { Type = LinkMessage.State, Phase = 0 });
                    Assert.True(WaitFor(() => follower.Status == FollowerStatus.Connected));
                }
            }
        }

        // ---------------------------------------------------------------- two linked sessions

        private const string CircuitScript = @"PROGRAM ""Link Test""
MODE CIRCUIT

REPEAT 2
    WORK 20s
    REST 10s
END

BETWEEN
    MOVE 15s ""Rotate""
END BETWEEN

STATION ""Bike""
END STATION

STATION ""Rower""
END STATION
";

        /// <summary>
        /// Two sessions in one test, with no windows. Each session's link events are queued and run
        /// here, on the test's thread, in place of a window's message loop.
        /// </summary>
        private sealed class LinkedPair : IDisposable
        {
            private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
            private readonly int _oldData = LinkController.DataPort, _oldDiscovery = LinkController.DiscoveryPort;
            public readonly AppSession Main, Second;
            public readonly List<string> SecondToasts = new List<string>();

            public LinkedPair(TimerSettings mainSettings, TimerSettings secondSettings)
            {
                LinkController.DataPort = DataPort;
                LinkController.DiscoveryPort = DiscoveryPort;
                LinkController.BluetoothEnabled = false; // tests never touch a real radio (or a paired PC)

                Main = new AppSession(mainSettings, new SilentCuePlayer());
                Second = new AppSession(secondSettings, new SilentCuePlayer());
                Main.Link.Post = action => _queue.Enqueue(action);
                Second.Link.Post = action => _queue.Enqueue(action);
                Second.Link.ToastRequested += (message, seconds) => SecondToasts.Add(message);
            }

            /// <summary>Keeps both sessions turning until the condition holds.</summary>
            public bool Pump(Func<bool> condition, int timeoutMs = 8000)
            {
                Stopwatch waited = Stopwatch.StartNew();
                while (waited.ElapsedMilliseconds < timeoutMs)
                {
                    while (_queue.TryDequeue(out Action action)) action();
                    Main.Tick();
                    Second.Tick();
                    if (condition()) return true;
                    Thread.Sleep(15);
                }
                return condition();
            }

            public void PumpFor(int ms)
            {
                Stopwatch waited = Stopwatch.StartNew();
                Pump(() => waited.ElapsedMilliseconds >= ms, ms + 1000);
            }

            /// <summary>How far the second screen's countdown is from the main PC's, in seconds.</summary>
            public double Gap()
            {
                return Second.Engine.PhaseClock.Elapsed.TotalSeconds - Main.Engine.PhaseClock.Elapsed.TotalSeconds;
            }

            public void Dispose()
            {
                Main.Dispose();
                Second.Dispose();
                LinkController.DataPort = _oldData;
                LinkController.DiscoveryPort = _oldDiscovery;
                LinkController.BluetoothEnabled = true;
            }
        }

        [Fact]
        public void TwoClocks_SecondScreenMirrors_Start_Pause_Skip_Back_Reset_AndProgramChanges()
        {
            TimerSettings mainSettings = new TimerSettings
            {
                ProgramScript = CircuitScript, UseBuiltProgram = true, PrepSeconds = 5, SoundEnabled = false,
                WorkLabel = "MAIN WORK", LinkRole = "lead", LinkCode = "4821"
            };
            TimerSettings secondSettings = new TimerSettings
            {
                ProgramScript = string.Empty, UseBuiltProgram = false, PrepSeconds = 30, SoundEnabled = false,
                WorkLabel = "OWN WORK", LinkRole = "follow", LinkCode = "4821", LinkAddress = "127.0.0.1", LinkSound = false
            };

            using (LinkedPair pair = new LinkedPair(mainSettings, secondSettings))
            {
                AppSession main = pair.Main, second = pair.Second;
                main.Link.Start();
                second.Link.Start();
                Assert.True(second.Link.IsFollowing);
                Assert.False(main.Link.IsFollowing);

                // ---- the second screen takes on the main PC's program and settings
                Assert.True(pair.Pump(() => second.Link.HasLeaderProgram), "the program never arrived");
                Assert.Equal("MAIN WORK", second.Settings.WorkLabel);
                Assert.Equal(5, second.Settings.PrepSeconds);
                Assert.Contains("Link Test", second.Settings.ProgramScript);
                Assert.Equal(ExecutionMode.Circuit, second.Engine.Program.Mode);
                Assert.Equal(Phase.Idle, second.Engine.Phase);

                // ...while its own settings are kept aside, untouched, for when it stops following
                Assert.True(second.ShowingLeaderSettings);
                Assert.Same(secondSettings, second.OwnSettings);
                Assert.Equal("OWN WORK", second.OwnSettings.WorkLabel);
                Assert.Contains(pair.SecondToasts, t => t.StartsWith("LINKED TO THE MAIN PC"));

                // ---- start: prep countdown on both
                main.Engine.Start();
                Assert.True(pair.Pump(() => second.Engine.Phase == Phase.Prep), "start did not mirror");
                pair.PumpFor(700);
                Assert.InRange(pair.Gap(), -0.1, 0.1);

                // ---- skip into the first work block
                main.Engine.Advance();
                Assert.True(pair.Pump(() => second.Engine.Phase == Phase.Active && second.Engine.Index == 0), "skip did not mirror");
                pair.PumpFor(600);
                Assert.InRange(pair.Gap(), -0.1, 0.1);

                // ---- it remembered the route that worked, to try first next time, and reports the link test
                Assert.Equal("tcp:127.0.0.1", secondSettings.LinkLastRoute);
                LinkStatus linkStatus = second.Link.GetStatus();
                Assert.Equal("follow", linkStatus.Role);
                Assert.Equal(FollowerStatus.Connected, linkStatus.FollowerState);
                Assert.Equal("network", linkStatus.Via);
                Assert.True(linkStatus.SessionRunning);
                LinkStatus mainStatus = main.Link.GetStatus();
                Assert.True(mainStatus.LeaderRunning);
                Assert.Equal(new[] { Environment.MachineName + " (network)" }, mainStatus.Screens.ToArray());

                // ---- the second screen's status line reports how well in step it is (shown with the key hints)
                string badge = second.Link.BadgeText(true, out bool warning);
                Assert.StartsWith("FOLLOWING " + Environment.MachineName.ToUpperInvariant() + "  -  IN STEP WITHIN ", badge);
                Assert.False(warning);
                Assert.InRange(second.Link.DriftPeakMs, 0, 100);
                Assert.Equal("FOLLOWING " + Environment.MachineName.ToUpperInvariant(), second.Link.BadgeText(false, out _));
                Assert.Equal("LINKED  -  1 SECOND SCREEN", main.Link.BadgeText(false, out _));

                // ---- and the words on a second screen say who has the controls
                second.Display.IsSecondScreen = second.Link.IsFollowing;
                Assert.Contains("the main PC has the controls", second.Display.BuildFrame(showHints: true).Hint);

                // ---- pause: both stop at the same point
                main.Engine.TogglePause();
                Assert.True(pair.Pump(() => !second.Engine.PhaseClock.IsRunning), "pause did not mirror");
                pair.PumpFor(400);
                Assert.InRange(pair.Gap(), -0.06, 0.06);
                double pausedAt = second.Engine.PhaseClock.Elapsed.TotalSeconds;
                pair.PumpFor(300);
                Assert.Equal(pausedAt, second.Engine.PhaseClock.Elapsed.TotalSeconds, 3);

                // ---- resume
                main.Engine.TogglePause();
                Assert.True(pair.Pump(() => second.Engine.PhaseClock.IsRunning), "resume did not mirror");

                // ---- skip twice (rest, then round 2 work), and back once
                main.Engine.Advance();
                main.Engine.Advance();
                Assert.True(pair.Pump(() => second.Engine.Index == 2), "second skip did not mirror");
                pair.PumpFor(3000); // more than 2.5 s in, "back" restarts the interval
                main.Engine.GoBack();
                Assert.True(pair.Pump(() => second.Engine.PhaseClock.Elapsed.TotalSeconds < 1.0 && second.Engine.Index == 2), "back did not mirror");
                pair.PumpFor(500);
                Assert.InRange(pair.Gap(), -0.1, 0.1);

                // ---- the main PC's program changes mid-session: the second screen gets the new one
                mainSettings.WorkLabel = "NEW LABEL";
                Assert.True(pair.Pump(() => second.Settings.WorkLabel == "NEW LABEL"), "the changed settings never arrived");
                Assert.Equal(2, second.Engine.Index); // and it stays where the session is

                // ---- reset: back to the idle screen
                main.Engine.Reset();
                Assert.True(pair.Pump(() => second.Engine.Phase == Phase.Idle), "reset did not mirror");

                // ---- stop following: the second screen goes back to its own settings
                second.Link.Stop(true);
                Assert.False(second.Link.IsFollowing);
                Assert.False(second.ShowingLeaderSettings);
                Assert.Same(secondSettings, second.Settings);
                Assert.Equal("OWN WORK", second.Settings.WorkLabel);
            }
        }

        [Fact]
        public void SecondScreen_JoiningMidSession_StartsAtTheCurrentPoint()
        {
            TimerSettings mainSettings = new TimerSettings
            {
                ProgramScript = CircuitScript, UseBuiltProgram = true, PrepSeconds = 0, SoundEnabled = false, LinkRole = "lead", LinkCode = "4821"
            };
            TimerSettings secondSettings = new TimerSettings
            {
                SoundEnabled = false, LinkRole = "follow", LinkCode = "4821", LinkAddress = "127.0.0.1"
            };

            using (LinkedPair pair = new LinkedPair(mainSettings, secondSettings))
            {
                AppSession main = pair.Main, second = pair.Second;
                main.Link.Start();

                // The session is already in its second block, 1.5 s in, before the second screen is switched on.
                main.Engine.Start();
                main.Engine.Advance();
                pair.PumpFor(1500);
                Assert.Equal(1, main.Engine.Index);

                second.Link.Start();
                Assert.True(pair.Pump(() => second.Engine.Phase == Phase.Active && second.Engine.Index == 1),
                    "the second screen did not pick up the running session");
                pair.PumpFor(500);
                Assert.InRange(pair.Gap(), -0.1, 0.1);
                Assert.True(second.Engine.PhaseClock.Elapsed.TotalSeconds > 1.5);
            }
        }

        [Fact]
        public void SettingsEditedOnASecondScreen_AreKeptAsItsOwn_AndTheMainPcsSessionStaysOnScreen()
        {
            TimerSettings mainSettings = new TimerSettings
            {
                ProgramScript = CircuitScript, UseBuiltProgram = true, PrepSeconds = 0, SoundEnabled = false, LinkRole = "lead", LinkCode = "4821"
            };
            TimerSettings secondSettings = new TimerSettings
            {
                SoundEnabled = false, WorkLabel = "OWN WORK", LinkRole = "follow", LinkCode = "4821", LinkAddress = "127.0.0.1"
            };

            using (LinkedPair pair = new LinkedPair(mainSettings, secondSettings))
            {
                AppSession second = pair.Second;
                pair.Main.Link.Start();
                second.Link.Start();
                Assert.True(pair.Pump(() => second.Link.HasLeaderProgram), "the program never arrived");

                // Not written to the real settings file: keep the test away from the teacher's AppData.
                // ReplaceSettings would save, so check only the in-memory rule it guards.
                Assert.True(second.ShowingLeaderSettings);
                Assert.Contains("Link Test", second.Settings.ProgramScript);
                Assert.Equal("OWN WORK", second.OwnSettings.WorkLabel);
                Assert.NotSame(second.Settings, second.OwnSettings);
            }
        }

        [Fact]
        public void SecondScreen_KeepsItsOwnDisplayLayout_MarginMotionAndControlBar()
        {
            TimerSettings mainSettings = new TimerSettings
            {
                ProgramScript = CircuitScript, UseBuiltProgram = true, PrepSeconds = 0, SoundEnabled = false, LinkRole = "lead", LinkCode = "4821",
                DisplayLayout = "bignumber", SafeMargin = 0, ReduceMotion = "off", ControlBar = true, Theme = "dark"
            };
            TimerSettings secondSettings = new TimerSettings
            {
                SoundEnabled = false, LinkRole = "follow", LinkCode = "4821", LinkAddress = "127.0.0.1",
                DisplayLayout = "stations", SafeMargin = 5, ReduceMotion = "on", ControlBar = false, Theme = "light"
            };

            using (LinkedPair pair = new LinkedPair(mainSettings, secondSettings))
            {
                AppSession second = pair.Second;
                pair.Main.Link.Start();
                second.Link.Start();
                Assert.True(pair.Pump(() => second.Link.HasLeaderProgram), "the program never arrived");

                // The Display reads these from OwnSettings; the main PC's never arrive.
                Assert.Equal("stations", second.OwnSettings.DisplayLayout);
                Assert.Equal(5, second.OwnSettings.SafeMargin);
                Assert.Equal("on", second.OwnSettings.ReduceMotion);
                Assert.False(second.OwnSettings.ControlBar);
                Assert.Equal("light", second.OwnSettings.Theme);
                Assert.Equal("standard", second.Settings.DisplayLayout);   // what arrived: defaults, not the main PC's
            }
        }
    }
}
