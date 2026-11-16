using System;
using System.Threading;
using GymClock.Link;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>Linked displays: the parts with no network - clock matching, the wire format and the settings text.</summary>
    public class LinkCoreTests
    {
        // ---------------------------------------------------------------- ClockSync

        [Fact]
        public void ClockSync_WorksOutTheOffsetFromAPing()
        {
            // Local clock reads 1000 when the ping leaves and 1040 when it returns (40 ms round trip).
            // The main PC's clock read 5020 in the middle, so it is 4000 ms ahead of ours.
            ClockSync sync = new ClockSync();
            Assert.False(sync.HasOffset);

            sync.AddSample(1000, 5020, 1040);

            Assert.True(sync.HasOffset);
            Assert.Equal(4000, sync.OffsetMs);
            Assert.Equal(40, sync.RoundTripMs);
            Assert.Equal(6000, sync.ToLeaderTime(2000));
        }

        [Fact]
        public void ClockSync_TrustsTheQuickestRecentPing()
        {
            ClockSync sync = new ClockSync();
            sync.AddSample(1000, 5150, 1300);   // slow, lopsided ping: offset looks like 4000
            sync.AddSample(2000, 6010, 2020);   // quick ping: offset 4000 exactly
            sync.AddSample(3000, 7400, 3500);   // very slow: would give 4150
            Assert.Equal(20, sync.RoundTripMs);
            Assert.Equal(4000, sync.OffsetMs);

            sync.Reset();
            Assert.False(sync.HasOffset);
        }

        [Theory]
        [InlineData(0.005, SyncAction.None)]
        [InlineData(-0.01, SyncAction.None)]
        [InlineData(0.08, SyncAction.Ease)]
        [InlineData(-0.2, SyncAction.Ease)]
        [InlineData(0.4, SyncAction.Snap)]
        [InlineData(-3.0, SyncAction.Snap)]
        public void ClockSync_SmallDriftsEase_BigOnesSnap(double drift, SyncAction expected)
        {
            Assert.Equal(expected, ClockSync.Classify(drift));
        }

        [Fact]
        public void ClockSync_Correct_ClosesPartOfASmallGap_AndAllOfABigOne()
        {
            Assert.Equal(10.0, ClockSync.Correct(10.0, 10.01), 6);          // close enough: untouched
            Assert.Equal(10.025, ClockSync.Correct(10.0, 10.1), 6);         // eased: a quarter of the gap
            Assert.Equal(9.975, ClockSync.Correct(10.0, 9.9), 6);
            Assert.Equal(42.0, ClockSync.Correct(10.0, 42.0), 6);           // pause/skip/reconnect: straight there
        }

        // ---------------------------------------------------------------- RunClock

        [Fact]
        public void RunClock_CanBePutAtAPosition()
        {
            RunClock clock = new RunClock();
            Assert.False(clock.IsRunning);

            clock.Set(TimeSpan.FromSeconds(30), false);
            Assert.False(clock.IsRunning);
            Assert.Equal(30, clock.Elapsed.TotalSeconds, 3);

            clock.Set(TimeSpan.FromSeconds(12), true);
            Assert.True(clock.IsRunning);
            Thread.Sleep(60);
            Assert.InRange(clock.Elapsed.TotalSeconds, 12.04, 12.6);

            clock.Stop();
            double stopped = clock.Elapsed.TotalSeconds;
            Thread.Sleep(30);
            Assert.Equal(stopped, clock.Elapsed.TotalSeconds, 6);

            clock.Restart();
            Assert.InRange(clock.Elapsed.TotalSeconds, 0, 0.5);
            clock.Reset();
            Assert.Equal(0, clock.Elapsed.TotalSeconds, 6);
        }

        // ---------------------------------------------------------------- wire format + link code

        [Fact]
        public void Message_RoundTrips_WithTheSameCode()
        {
            byte[] key = LinkProtocol.KeyFromCode("4821");
            string line = LinkProtocol.Encode(new LinkMessage
            {
                Type = LinkMessage.State, Seq = 7, Phase = 2, Index = 3, PhaseElapsedMs = 12345,
                SessionElapsedMs = 99999, Paused = true, LeaderTimeMs = 555
            }, key);

            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("settings", line); // unused fields are left out
            Assert.True(LinkProtocol.TryDecode(line, key, out LinkMessage m));
            Assert.Equal(LinkMessage.State, m.Type);
            Assert.Equal(7, m.Seq);
            Assert.Equal(2, m.Phase);
            Assert.Equal(3, m.Index);
            Assert.Equal(12345, m.PhaseElapsedMs);
            Assert.True(m.Paused);
            Assert.Equal(555, m.LeaderTimeMs);
        }

        [Fact]
        public void Message_IsRefused_WithADifferentCode_OrIfChanged()
        {
            byte[] key = LinkProtocol.KeyFromCode("4821");
            string line = LinkProtocol.Encode(new LinkMessage { Type = LinkMessage.Program, Script = "PROGRAM \"A\"\nWORK 30s" }, key);

            Assert.False(LinkProtocol.TryDecode(line, LinkProtocol.KeyFromCode("4822"), out _));
            Assert.False(LinkProtocol.TryDecode(line.Replace("WORK 30s", "WORK 99s"), key, out _));
            Assert.False(LinkProtocol.TryDecode("not a message", key, out _));
            Assert.False(LinkProtocol.TryDecode(string.Empty, key, out _));
            Assert.False(LinkProtocol.TryDecode(LinkProtocol.DeniedLine, key, out _));
            Assert.True(LinkProtocol.TryDecode(line, key, out LinkMessage ok));
            Assert.Equal("PROGRAM \"A\"\nWORK 30s", ok.Script); // a multi-line script survives on one line
        }

        [Fact]
        public void Announcement_IsOnlyAcceptedFromAMainPcWithOurCode()
        {
            byte[] ours = LinkProtocol.KeyFromCode("4821");
            byte[] theirs = LinkProtocol.KeyFromCode("1111");
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(LinkProtocol.EncodeAnnounce("GYM-PC-1", ours));

            Assert.True(LinkProtocol.TryDecodeAnnounce(bytes, ours, out LinkMessage announce));
            Assert.Equal("GYM-PC-1", announce.Name);
            Assert.Equal(LinkProtocol.DataPort, announce.Port);
            Assert.False(LinkProtocol.TryDecodeAnnounce(bytes, theirs, out _)); // the gym next door's main PC
            Assert.False(LinkProtocol.TryDecodeAnnounce(new byte[] { 1, 2, 3 }, ours, out _));
        }

        [Fact]
        public void NewCode_IsFourDigits()
        {
            for (int i = 0; i < 20; i++)
            {
                string code = LinkProtocol.NewCode();
                Assert.Equal(4, code.Length);
                Assert.True(int.TryParse(code, out int n) && n >= 1000);
            }
        }

        // ---------------------------------------------------------------- which stations a second screen shows

        [Theory]
        [InlineData(0, 0, new[] { 1, 2, 3, 4 })]   // not set: everything
        [InlineData(3, 4, new[] { 3, 4 })]         // "show stations 3 to 4"
        [InlineData(1, 2, new[] { 1, 2 })]
        [InlineData(3, 0, new[] { 3, 4 })]         // from 3 onwards
        [InlineData(0, 2, new[] { 1, 2 })]         // up to 2
        [InlineData(4, 3, new[] { 3, 4 })]         // typed the wrong way round
        [InlineData(3, 9, new[] { 3, 4 })]         // runs past the end
        [InlineData(7, 8, new[] { 1, 2, 3, 4 })]   // doesn't fit this program: show all, never a blank screen
        public void StationRange_ForASecondScreen(int from, int to, int[] expected)
        {
            var shown = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 4; i++) if (MainForm.StationInRange(i, 4, from, to)) shown.Add(i + 1);
            Assert.Equal(expected, shown.ToArray());
        }

        // ---------------------------------------------------------------- settings sent to a second screen

        [Fact]
        public void SettingsText_CarriesTheDisplaySetup_ButNeverTheLinkSettings()
        {
            TimerSettings main = new TimerSettings
            {
                PrepSeconds = 7, WorkSeconds = 50, RestSeconds = 20, Rounds = 6, WorkLabel = "GO", WorkColour = "#112233",
                ShowStationsTable = false, UseBuiltProgram = true,
                LinkRole = "lead", LinkCode = "4821", LinkAddress = "10.0.0.9", LinkSound = true, LinkStationsFrom = 2, LinkStationsTo = 3
            };
            main.Stations.Add(new Station("Bike", "fast", "easy"));

            string sent = main.ToText(false);
            Assert.DoesNotContain("4821", sent);
            Assert.DoesNotContain("linkrole", sent);

            TimerSettings second = TimerSettings.FromText(sent, "PROGRAM \"X\"\nWORK 30s");
            Assert.Equal(7, second.PrepSeconds);
            Assert.Equal(50, second.WorkSeconds);
            Assert.Equal(20, second.RestSeconds);
            Assert.Equal(6, second.Rounds);
            Assert.Equal("GO", second.WorkLabel);
            Assert.Equal("#112233", second.WorkColour);
            Assert.False(second.ShowStationsTable);
            Assert.True(second.UseBuiltProgram);
            Assert.Single(second.Stations);
            Assert.Equal("PROGRAM \"X\"\nWORK 30s", second.ProgramScript);

            // A second screen's link settings are its own.
            Assert.Equal("off", second.LinkRole);
            Assert.Equal(string.Empty, second.LinkCode);
            Assert.False(second.LinkSound);

            // This PC's own file does keep them, and Clone (used by the settings window) carries them.
            Assert.Contains("linkcode=4821", main.ToText(true));
            TimerSettings clone = main.Clone();
            Assert.True(clone.IsLinkLeader);
            Assert.Equal("4821", clone.LinkCode);
            Assert.Equal("10.0.0.9", clone.LinkAddress);
            Assert.True(clone.LinkSound);
            Assert.Equal(2, clone.LinkStationsFrom);
            Assert.Equal(3, clone.LinkStationsTo);
        }
    }
}
