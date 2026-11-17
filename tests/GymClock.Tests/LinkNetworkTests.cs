using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using GymClock.Link;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>
    /// Linked displays over a real connection, both ends on this PC (loopback): the network classes
    /// by themselves, then two clock windows linked together. One at a time - they share test ports.
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

        // ---------------------------------------------------------------- two clock windows

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

        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static T Field<T>(MainForm form, string name)
        {
            // Session state now lives in the engine; MainForm reads it through properties of the same names.
            FieldInfo field = typeof(MainForm).GetField(name, Private);
            if (field != null) return (T)field.GetValue(form);
            return (T)typeof(MainForm).GetProperty(name, Private).GetValue(form);
        }

        private static void Call(MainForm form, string name, params object[] args)
        {
            typeof(MainForm).GetMethod(name, Private).Invoke(form, args);
        }

        private static double PhaseSeconds(MainForm form)
        {
            return Field<RunClock>(form, "_phaseClock").Elapsed.TotalSeconds;
        }

        /// <summary>Keeps both windows' message loops and tickers turning until the condition holds.</summary>
        private static bool Pump(MainForm main, MainForm second, Func<bool> condition, int timeoutMs = 8000)
        {
            Stopwatch waited = Stopwatch.StartNew();
            while (waited.ElapsedMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                Call(main, "RunTick");
                Call(main, "LinkTick");
                Call(second, "RunTick");
                if (condition()) return true;
                Thread.Sleep(15);
            }
            return condition();
        }

        private static void PumpFor(MainForm main, MainForm second, int ms)
        {
            Stopwatch waited = Stopwatch.StartNew();
            Pump(main, second, () => waited.ElapsedMilliseconds >= ms, ms + 1000);
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
        public void TwoClocks_SecondScreenMirrors_Start_Pause_Skip_Back_Reset_AndProgramChanges()
        {
            RunSta(() =>
            {
                int oldData = MainForm.LinkDataPort, oldDiscovery = MainForm.LinkDiscoveryPort;
                MainForm.LinkDataPort = DataPort;
                MainForm.LinkDiscoveryPort = DiscoveryPort;
                MainForm.LinkBluetoothEnabled = false; // tests never touch a real radio (or a paired PC)

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

                MainForm main = new MainForm(mainSettings);
                MainForm second = new MainForm(secondSettings);
                try
                {
                    IntPtr h1 = main.Handle, h2 = second.Handle; // windows exist (not shown), so link events can reach them
                    Call(main, "StartLink");
                    Call(second, "StartLink");

                    // ---- the second screen takes on the main PC's program and settings
                    Assert.True(Pump(main, second, () => Field<bool>(second, "_hasLeaderProgram")), "the program never arrived");
                    TimerSettings shown = Field<TimerSettings>(second, "_settings");
                    Assert.Equal("MAIN WORK", shown.WorkLabel);
                    Assert.Equal(5, shown.PrepSeconds);
                    Assert.Contains("Link Test", shown.ProgramScript);
                    Assert.Equal(ExecutionMode.Circuit, Field<WorkoutProgram>(second, "_program").Mode);
                    Assert.Equal(Phase.Idle, Field<Phase>(second, "_phase"));

                    // ...while its own settings are kept aside, untouched, for when it stops following
                    TimerSettings own = Field<TimerSettings>(second, "_ownSettings");
                    Assert.Same(secondSettings, own);
                    Assert.Equal("OWN WORK", own.WorkLabel);

                    // ---- start: prep countdown on both
                    Call(main, "StartSession");
                    Assert.True(Pump(main, second, () => Field<Phase>(second, "_phase") == Phase.Prep), "start did not mirror");
                    PumpFor(main, second, 700);
                    Assert.InRange(PhaseSeconds(second) - PhaseSeconds(main), -0.1, 0.1);

                    // ---- skip into the first work block
                    Call(main, "Advance");
                    Assert.True(Pump(main, second, () => Field<Phase>(second, "_phase") == Phase.Active && Field<int>(second, "_index") == 0), "skip did not mirror");
                    PumpFor(main, second, 600);
                    Assert.InRange(PhaseSeconds(second) - PhaseSeconds(main), -0.1, 0.1);

                    // ---- it remembered the route that worked, to try first next time, and reports the link test
                    Assert.Equal("tcp:127.0.0.1", secondSettings.LinkLastRoute);
                    LinkStatus linkStatus = (LinkStatus)typeof(MainForm).GetMethod("GetLinkStatus", Private).Invoke(second, null);
                    Assert.Equal("follow", linkStatus.Role);
                    Assert.Equal(FollowerStatus.Connected, linkStatus.FollowerState);
                    Assert.Equal("network", linkStatus.Via);
                    Assert.True(linkStatus.SessionRunning);
                    LinkStatus mainStatus = (LinkStatus)typeof(MainForm).GetMethod("GetLinkStatus", Private).Invoke(main, null);
                    Assert.True(mainStatus.LeaderRunning);
                    Assert.Equal(new[] { Environment.MachineName + " (network)" }, mainStatus.Screens.ToArray());

                    // ---- the second screen's status line reports how well in step it is (shown with the key hints)
                    object[] badgeArgs = { true, null };
                    string badge = (string)typeof(MainForm).GetMethod("LinkBadgeText", Private).Invoke(second, badgeArgs);
                    Assert.StartsWith("FOLLOWING " + Environment.MachineName.ToUpperInvariant() + "  -  IN STEP WITHIN ", badge);
                    Assert.InRange(Field<double>(second, "_linkDriftPeakMs"), 0, 100);
                    Assert.Equal("FOLLOWING " + Environment.MachineName.ToUpperInvariant(),
                        (string)typeof(MainForm).GetMethod("LinkBadgeText", Private).Invoke(second, new object[] { false, null }));

                    // ---- pause: both stop at the same point
                    Call(main, "TogglePause");
                    Assert.True(Pump(main, second, () => !Field<RunClock>(second, "_phaseClock").IsRunning), "pause did not mirror");
                    PumpFor(main, second, 400);
                    Assert.InRange(PhaseSeconds(second) - PhaseSeconds(main), -0.06, 0.06);
                    double pausedAt = PhaseSeconds(second);
                    PumpFor(main, second, 300);
                    Assert.Equal(pausedAt, PhaseSeconds(second), 3);

                    // ---- resume
                    Call(main, "TogglePause");
                    Assert.True(Pump(main, second, () => Field<RunClock>(second, "_phaseClock").IsRunning), "resume did not mirror");

                    // ---- skip twice (rest, then round 2 work), and back once
                    Call(main, "Advance");
                    Call(main, "Advance");
                    Assert.True(Pump(main, second, () => Field<int>(second, "_index") == 2), "second skip did not mirror");
                    PumpFor(main, second, 3000); // more than 2.5 s in, "back" restarts the interval
                    Call(main, "GoBack");
                    Assert.True(Pump(main, second, () => PhaseSeconds(second) < 1.0 && Field<int>(second, "_index") == 2), "back did not mirror");
                    PumpFor(main, second, 500);
                    Assert.InRange(PhaseSeconds(second) - PhaseSeconds(main), -0.1, 0.1);

                    // ---- the main PC's program changes mid-session: the second screen gets the new one
                    mainSettings.WorkLabel = "NEW LABEL";
                    Assert.True(Pump(main, second, () => Field<TimerSettings>(second, "_settings").WorkLabel == "NEW LABEL"), "the changed settings never arrived");
                    Assert.Equal(2, Field<int>(second, "_index")); // and it stays where the session is

                    // ---- reset: back to the idle screen
                    Call(main, "ResetSession");
                    Assert.True(Pump(main, second, () => Field<Phase>(second, "_phase") == Phase.Idle), "reset did not mirror");

                    // ---- stop following: the second screen goes back to its own settings
                    Call(second, "StopLink", true);
                    Assert.Same(secondSettings, Field<TimerSettings>(second, "_settings"));
                    Assert.Equal("OWN WORK", Field<TimerSettings>(second, "_settings").WorkLabel);
                    Assert.Null(Field<TimerSettings>(second, "_ownSettings"));
                }
                finally
                {
                    Call(main, "StopLink", false);
                    Call(second, "StopLink", false);
                    main.Dispose();
                    second.Dispose();
                    MainForm.LinkDataPort = oldData;
                    MainForm.LinkDiscoveryPort = oldDiscovery;
                    MainForm.LinkBluetoothEnabled = true;
                }
            });
        }

        [Fact]
        public void SecondScreen_JoiningMidSession_StartsAtTheCurrentPoint()
        {
            RunSta(() =>
            {
                int oldData = MainForm.LinkDataPort, oldDiscovery = MainForm.LinkDiscoveryPort;
                MainForm.LinkDataPort = DataPort;
                MainForm.LinkDiscoveryPort = DiscoveryPort;
                MainForm.LinkBluetoothEnabled = false; // tests never touch a real radio (or a paired PC)

                MainForm main = new MainForm(new TimerSettings
                {
                    ProgramScript = CircuitScript, UseBuiltProgram = true, PrepSeconds = 0, SoundEnabled = false, LinkRole = "lead", LinkCode = "4821"
                });
                MainForm second = new MainForm(new TimerSettings
                {
                    SoundEnabled = false, LinkRole = "follow", LinkCode = "4821", LinkAddress = "127.0.0.1"
                });
                try
                {
                    IntPtr h1 = main.Handle, h2 = second.Handle;
                    Call(main, "StartLink");

                    // The session is already in its second block, 1.5 s in, before the second screen is switched on.
                    Call(main, "StartSession");
                    Call(main, "Advance");
                    PumpFor(main, second, 1500);
                    Assert.Equal(1, Field<int>(main, "_index"));

                    Call(second, "StartLink");
                    Assert.True(Pump(main, second, () => Field<Phase>(second, "_phase") == Phase.Active && Field<int>(second, "_index") == 1),
                        "the second screen did not pick up the running session");
                    PumpFor(main, second, 500);
                    Assert.InRange(PhaseSeconds(second) - PhaseSeconds(main), -0.1, 0.1);
                    Assert.True(PhaseSeconds(second) > 1.5);
                }
                finally
                {
                    Call(main, "StopLink", false);
                    Call(second, "StopLink", false);
                    main.Dispose();
                    second.Dispose();
                    MainForm.LinkDataPort = oldData;
                    MainForm.LinkDiscoveryPort = oldDiscovery;
                    MainForm.LinkBluetoothEnabled = true;
                }
            });
        }
    }
}
