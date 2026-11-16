using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GymClock.Link;

namespace GymClock
{
    /// <summary>
    /// Linked displays (version 4, docs/plans/linked-displays.md): this PC as the main PC
    /// (publishes its program and where the session is) or as a second screen (follows a main PC).
    ///
    /// A second screen runs the same timing engine as always, so it keeps counting by itself if
    /// the link drops. The main PC's STATE messages only correct it: small drifts are eased out,
    /// real differences (pause, skip, reset, joining mid-session) snap straight to the main PC.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>This PC's own settings while <see cref="_settings"/> holds the main PC's (second screen only).</summary>
        private TimerSettings _ownSettings;

        private LinkLeader _linkLeader;
        private LinkFollower _linkFollower;

        /// <summary>The link's network ports. Only changed by tests, so they don't collide with a running app.</summary>
        public static int LinkDataPort = LinkProtocol.DataPort;
        public static int LinkDiscoveryPort = LinkProtocol.DiscoveryPort;

        // ---- main PC: what was last sent
        private string _sentProgramKey;
        private long _lastProgramCheckMs = -100000;
        private long _lastStateSentMs = -100000;
        private int _sentPhase = -1;
        private int _sentIndex;
        private bool _sentPaused;
        private long _sentPhaseElapsedMs;
        private long _sentAtMs;
        private volatile bool _linkResendAll;

        // ---- second screen: what was last received
        private string _receivedProgramKey;
        private bool _hasLeaderProgram;
        private bool _snapOnNextState;

        /// <summary>The settings that belong to this PC (and are saved to its settings file).</summary>
        private TimerSettings OwnSettings
        {
            get { return _ownSettings ?? _settings; }
        }

        /// <summary>True while this PC is a second screen: the main PC has the controls.</summary>
        private bool IsFollowing
        {
            get { return _linkFollower != null; }
        }

        private void OnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) action(); });
            }
            catch { /* closing */ }
        }

        // ------------------------------------------------------------------ start / stop

        /// <summary>Starts (or restarts) the link from this PC's settings. Called once the window exists.</summary>
        private void StartLink()
        {
            StopLink(true);
            TimerSettings own = OwnSettings;

            if (own.IsLinkLeader)
            {
                if (string.IsNullOrWhiteSpace(own.LinkCode))
                {
                    own.LinkCode = LinkProtocol.NewCode();
                    own.Save();
                }

                LinkLeader leader = new LinkLeader(Environment.MachineName, own.LinkCode, LinkDataPort, LinkDiscoveryPort);
                leader.FollowerJoined += name =>
                {
                    _linkResendAll = true; // the next tick sends the program and state to everyone
                    OnUi(() => { if (_linkLeader == leader) ShowToast("SECOND SCREEN LINKED: " + name.ToUpperInvariant(), 4); });
                };
                leader.FollowersChanged += () => OnUi(Invalidate);

                if (!leader.Start())
                {
                    ShowToast("LINK NOT STARTED: " + leader.StartError, 8);
                    leader.Dispose();
                    return;
                }
                _linkLeader = leader;
            }
            else if (own.IsLinkFollower)
            {
                LinkFollower follower = new LinkFollower(Environment.MachineName, own.LinkCode, own.LinkAddress, LinkDataPort, LinkDiscoveryPort);
                follower.ProgramReceived += (settingsText, script) => OnUi(() => { if (_linkFollower == follower) OnLeaderProgram(settingsText, script); });
                follower.StateReceived += message => OnUi(() => { if (_linkFollower == follower) OnLeaderState(message); });
                follower.StatusChanged += () => OnUi(() => { if (_linkFollower == follower) OnFollowerStatusChanged(); });
                _linkFollower = follower;
                follower.Start();
            }

            ApplyAudioSettings();
            Invalidate();
        }

        /// <summary>Stops the link. A second screen goes back to its own program and settings.</summary>
        private void StopLink(bool restoreOwnProgram)
        {
            if (_linkLeader != null) { _linkLeader.Dispose(); _linkLeader = null; }
            if (_linkFollower != null) { _linkFollower.Dispose(); _linkFollower = null; }

            _sentProgramKey = null;
            _sentPhase = -1;
            _lastProgramCheckMs = -100000;
            _lastStateSentMs = -100000;
            _receivedProgramKey = null;
            _hasLeaderProgram = false;

            if (_ownSettings != null)
            {
                _settings = _ownSettings;
                _ownSettings = null;
                if (restoreOwnProgram)
                {
                    ApplySettingsLive();
                    ResetSession();
                }
            }
        }

        /// <summary>The Link Displays window (L). Works on both the main PC and a second screen.</summary>
        private void ShowLinkSettings()
        {
            using (LinkSettingsForm dialog = new LinkSettingsForm(OwnSettings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                TimerSettings own = OwnSettings;
                own.LinkRole = dialog.Role;
                own.LinkCode = dialog.Code;
                own.LinkAddress = dialog.Address;
                own.LinkSound = dialog.FollowerSound;
                own.LinkStationsFrom = dialog.StationsFrom;
                own.LinkStationsTo = dialog.StationsTo;
                own.Save();

                StartLink();
                ShowHint(6);
            }
        }

        // ------------------------------------------------------------------ main PC: publish

        /// <summary>
        /// Called on every tick. Sends the program when it changes (and to a screen that has just
        /// joined), and the session state about 4 times a second - or at once when something
        /// changes (start, pause, skip, back, reset), so second screens react without delay.
        /// </summary>
        private void LinkTick()
        {
            LinkLeader leader = _linkLeader;
            if (leader == null) return;

            if (_linkResendAll)
            {
                _linkResendAll = false;
                _sentProgramKey = null;
                _lastProgramCheckMs = -100000;
                _lastStateSentMs = -100000;
            }
            if (leader.FollowerNames.Count == 0) return;

            long now = leader.NowMs;

            if (now - _lastProgramCheckMs >= 1000)
            {
                _lastProgramCheckMs = now;
                string settingsText = _settings.ToText(false);
                string script = _settings.ProgramScript ?? string.Empty;
                string key = settingsText + "\u0001" + script;
                if (key != _sentProgramKey)
                {
                    _sentProgramKey = key;
                    leader.Broadcast(new LinkMessage { Type = LinkMessage.Program, Name = Environment.MachineName, Settings = settingsText, Script = script });
                    _lastStateSentMs = -100000; // follow it with the state straight away
                }
            }

            bool paused = IsPaused;
            long phaseElapsedMs = (long)_phaseClock.Elapsed.TotalMilliseconds;
            long expectedMs = _sentPhaseElapsedMs + (_phaseClock.IsRunning && !_sentPaused ? now - _sentAtMs : 0);
            bool changed = (int)_phase != _sentPhase
                || _index != _sentIndex
                || paused != _sentPaused
                || Math.Abs(phaseElapsedMs - expectedMs) > 100; // e.g. "back" restarted the interval

            if (!changed && now - _lastStateSentMs < 250) return;

            _sentPhase = (int)_phase;
            _sentIndex = _index;
            _sentPaused = paused;
            _sentPhaseElapsedMs = phaseElapsedMs;
            _sentAtMs = now;
            _lastStateSentMs = now;

            leader.Broadcast(new LinkMessage
            {
                Type = LinkMessage.State,
                Phase = (int)_phase,
                Index = _index,
                PhaseElapsedMs = phaseElapsedMs,
                SessionElapsedMs = (long)_sessionClock.Elapsed.TotalMilliseconds,
                Paused = paused,
                LeaderTimeMs = now
            });
        }

        // ------------------------------------------------------------------ second screen: follow

        private void OnFollowerStatusChanged()
        {
            LinkFollower follower = _linkFollower;
            if (follower == null) return;

            switch (follower.Status)
            {
                case FollowerStatus.Connected:
                    _snapOnNextState = true;
                    ShowToast("LINKED TO THE MAIN PC" + (follower.LeaderName.Length > 0 ? ": " + follower.LeaderName.ToUpperInvariant() : string.Empty), 4);
                    break;
                case FollowerStatus.WrongCode:
                    ShowToast("LINK CODE NOT ACCEPTED  -  PRESS L TO CHECK IT", 8);
                    break;
            }
            Invalidate();
        }

        /// <summary>The main PC's settings and program arrived: show exactly what it shows.</summary>
        private void OnLeaderProgram(string settingsText, string script)
        {
            string key = settingsText + "\u0001" + script;
            if (key == _receivedProgramKey) return;
            _receivedProgramKey = key;

            TimerSettings own = OwnSettings;
            TimerSettings leaderSettings = TimerSettings.FromText(settingsText, script);

            // What stays this PC's own: its sound, its window behaviour and its link settings.
            leaderSettings.SoundEnabled = own.LinkSound;
            leaderSettings.Volume = own.Volume;
            leaderSettings.AlwaysOnTop = own.AlwaysOnTop;
            leaderSettings.LinkRole = own.LinkRole;
            leaderSettings.LinkCode = own.LinkCode;
            leaderSettings.LinkAddress = own.LinkAddress;
            leaderSettings.LinkSound = own.LinkSound;
            leaderSettings.LinkStationsFrom = own.LinkStationsFrom;
            leaderSettings.LinkStationsTo = own.LinkStationsTo;

            if (_ownSettings == null) _ownSettings = _settings;
            _settings = leaderSettings;
            _hasLeaderProgram = true;
            _snapOnNextState = true;

            ApplySettingsLive();
        }

        /// <summary>Where the main PC's session is. Corrects this screen's clock, or jumps to match.</summary>
        private void OnLeaderState(LinkMessage message)
        {
            LinkFollower follower = _linkFollower;
            if (follower == null || !_hasLeaderProgram) return;
            if (!follower.TryLeaderNow(out long leaderNow)) return; // clocks not matched yet (takes under a second)

            int phaseNumber = message.Phase ?? 0;
            if (phaseNumber < (int)Phase.Idle || phaseNumber > (int)Phase.Finished) return;
            Phase leaderPhase = (Phase)phaseNumber;
            int leaderIndex = message.Index ?? -1;
            bool leaderPaused = message.Paused ?? false;
            bool leaderTimed = leaderPhase == Phase.Prep || leaderPhase == Phase.Active;

            // The message is a moment old by the time it gets here: add the travel time.
            double age = 0;
            if (leaderTimed && !leaderPaused)
            {
                age = (leaderNow - (message.LeaderTimeMs ?? leaderNow)) / 1000.0;
                if (age < 0) age = 0;
                if (age > 5) age = 5;
            }
            double leaderElapsed = ((message.PhaseElapsedMs ?? 0) / 1000.0) + age;
            double leaderSession = ((message.SessionElapsedMs ?? 0) / 1000.0) + age;

            bool parallel = _program.Mode == ExecutionMode.Parallel;
            bool sameStep = leaderPhase == _phase && (leaderPhase != Phase.Active || parallel || leaderIndex == _index);
            bool localPaused = IsPaused;
            bool snap = _snapOnNextState;
            _snapOnNextState = false;

            if (!snap && sameStep && (!leaderTimed || leaderPaused == localPaused))
            {
                if (!leaderTimed) return;

                double local = _phaseClock.Elapsed.TotalSeconds;
                if (leaderPaused)
                {
                    if (Math.Abs(leaderElapsed - local) > 0.05) _phaseClock.Set(TimeSpan.FromSeconds(leaderElapsed), false);
                }
                else
                {
                    double corrected = ClockSync.Correct(local, leaderElapsed);
                    if (corrected != local) _phaseClock.Set(TimeSpan.FromSeconds(corrected), true);
                }

                if (Math.Abs(_sessionClock.Elapsed.TotalSeconds - leaderSession) > 0.3)
                {
                    _sessionClock.Set(TimeSpan.FromSeconds(leaderSession), !leaderPaused);
                }
                return;
            }

            // A different step. When both are running and a fraction of a second apart on the
            // timeline, one of us has simply crossed into the next block a moment before the
            // other - this screen's own engine gets there by itself (with its own cue), so leave it.
            if (!snap && !leaderPaused && !localPaused && InTimedPhase)
            {
                double? gap = TimelineGap(leaderPhase, leaderIndex, leaderElapsed);
                if (gap.HasValue && Math.Abs(gap.Value) < 0.3) return;
            }

            SnapToLeader(leaderPhase, leaderIndex, leaderElapsed, leaderSession, leaderPaused);
        }

        /// <summary>Seconds from this screen's position to the main PC's along the program, or null when they can't be compared.</summary>
        private double? TimelineGap(Phase leaderPhase, int leaderIndex, double leaderElapsed)
        {
            double? theirs = TimelinePosition(leaderPhase, leaderIndex, leaderElapsed);
            double? ours = TimelinePosition(_phase, _index, _phaseClock.Elapsed.TotalSeconds);
            if (!theirs.HasValue || !ours.HasValue) return null;

            double gap = theirs.Value - ours.Value;
            if (_program.Continuous && _program.Mode != ExecutionMode.Parallel && leaderPhase == Phase.Active && _phase == Phase.Active)
            {
                // A looping program: the last block and the first are neighbours.
                double cycle = SequenceOffset(_sequence.Count);
                if (cycle > 0)
                {
                    gap = ((gap % cycle) + cycle) % cycle;
                    if (gap > cycle / 2) gap -= cycle;
                }
            }
            return gap;
        }

        private double? TimelinePosition(Phase phase, int index, double elapsed)
        {
            bool parallel = _program.Mode == ExecutionMode.Parallel;
            switch (phase)
            {
                case Phase.Prep:
                    return elapsed - Math.Max(1, _settings.PrepSeconds);
                case Phase.Active:
                    return parallel ? elapsed : SequenceOffset(index) + elapsed;
                case Phase.Finished:
                    if (parallel || _program.Continuous) return null;
                    return SequenceOffset(_sequence.Count);
                default:
                    return null;
            }
        }

        /// <summary>Seconds of program before the block at <paramref name="index"/>.</summary>
        private double SequenceOffset(int index)
        {
            double total = 0;
            for (int i = 0; i < index && i < _sequence.Count; i++) total += Math.Max(1, _sequence[i].Block.Seconds);
            return total;
        }

        private void SnapToLeader(Phase leaderPhase, int leaderIndex, double leaderElapsed, double leaderSession, bool leaderPaused)
        {
            bool parallel = _program.Mode == ExecutionMode.Parallel;
            bool stepChanged = leaderPhase != _phase || (leaderPhase == Phase.Active && !parallel && leaderIndex != _index);

            switch (leaderPhase)
            {
                case Phase.Idle:
                    if (_phase != Phase.Idle) ResetSession();
                    return;

                case Phase.Finished:
                    _phase = Phase.Finished;
                    _phaseClock.Stop();
                    _sessionClock.Set(TimeSpan.FromSeconds(leaderSession), false);
                    if (stepChanged)
                    {
                        Beeper.CueFinish();
                        ShowHint(20);
                    }
                    Invalidate();
                    return;

                case Phase.Prep:
                    _phase = Phase.Prep;
                    _index = -1;
                    _phaseLength = Math.Max(1, _settings.PrepSeconds);
                    break;

                default:
                    _phase = Phase.Active;
                    _index = Math.Max(0, Math.Min(leaderIndex, _sequence.Count - 1));
                    if (!parallel) _phaseLength = Math.Max(1, CurrentBlock().Block.Seconds);
                    break;
            }

            _phaseClock.Set(TimeSpan.FromSeconds(leaderElapsed), !leaderPaused);
            _sessionClock.Set(TimeSpan.FromSeconds(leaderSession), !leaderPaused);

            if (stepChanged)
            {
                _lastCueSecond = -1;
                _lastRotationKey = -1;
                if (!leaderPaused)
                {
                    if (_phase == Phase.Prep) Beeper.CuePrepStart();
                    else if (parallel) Beeper.CueWorkStart();
                    else PlayCueFor(CurrentBlock().Block.Type);
                }
            }
            Invalidate();
        }

        /// <summary>A control key or click on a second screen: say why nothing happened.</summary>
        private void FollowerControlBlocked()
        {
            ShowToast("THE MAIN PC CONTROLS THIS SCREEN  -  PRESS L FOR LINK SETTINGS", 3);
        }

        // ------------------------------------------------------------------ display

        /// <summary>Second screen, Parallel programs: is this station one of the ones this screen shows?</summary>
        private bool LinkShowsStation(int stationIndex, int stationCount)
        {
            if (!IsFollowing) return true;
            return StationInRange(stationIndex, stationCount, OwnSettings.LinkStationsFrom, OwnSettings.LinkStationsTo);
        }

        /// <summary>
        /// Is the station (0-based) inside "show stations FROM to TO" (1-based, 0 = not set)?
        /// A range that doesn't fit the program at all shows everything rather than a blank screen.
        /// </summary>
        public static bool StationInRange(int stationIndex, int stationCount, int from, int to)
        {
            if (from <= 0 && to <= 0) return true;
            if (from <= 0) from = 1;
            if (to <= 0) to = stationCount;
            if (from > to) { int swap = from; from = to; to = swap; }
            if (from > stationCount) return true;
            int number = stationIndex + 1;
            return number >= from && number <= to;
        }

        /// <summary>The small link status line at the bottom of the screen, or empty when the link is off.</summary>
        private string LinkBadgeText(out Color colour)
        {
            Color quiet = Color.FromArgb(130, 255, 255, 255);
            Color warning = Color.FromArgb(235, 255, 200, 90);
            colour = quiet;

            LinkLeader leader = _linkLeader;
            if (leader != null)
            {
                int count = leader.FollowerNames.Count;
                if (count > 0) return "LINKED  -  " + count + (count == 1 ? " SECOND SCREEN" : " SECOND SCREENS");

                // The address (only needed when a second screen can't find this PC) is in the Link displays window.
                return "LINK ON  -  CODE " + OwnSettings.LinkCode + "  -  NO SECOND SCREEN YET";
            }

            LinkFollower follower = _linkFollower;
            if (follower != null)
            {
                switch (follower.Status)
                {
                    case FollowerStatus.Connected:
                        return "FOLLOWING " + (follower.LeaderName.Length > 0 ? follower.LeaderName.ToUpperInvariant() : "THE MAIN PC");
                    case FollowerStatus.Reconnecting:
                        colour = warning;
                        return "RECONNECTING TO THE MAIN PC...";
                    case FollowerStatus.WrongCode:
                        colour = warning;
                        return "LINK CODE NOT ACCEPTED  -  PRESS L";
                    case FollowerStatus.Connecting:
                        colour = warning;
                        return "CONNECTING TO THE MAIN PC...";
                    default:
                        colour = warning;
                        return "LOOKING FOR THE MAIN PC...  (L = LINK SETTINGS)";
                }
            }

            return string.Empty;
        }
    }
}
