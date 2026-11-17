using System;
using System.Linq;
using System.Threading;
using GymClock.Link;

namespace GymClock
{
    /// <summary>
    /// Linked displays: this PC as the main PC (publishes its program and where the session is) or
    /// as a second screen (follows a main PC). See docs/plans/linked-displays.md.
    ///
    /// A second screen runs the same timing engine as always, so it keeps counting by itself if
    /// the link drops. The main PC's STATE messages only correct it: small drifts are eased out,
    /// real differences (pause, skip, reset, joining mid-session) snap straight to the main PC.
    ///
    /// Moved from MainForm.Link.cs (build guide task P1.7); the logic is unchanged.
    /// </summary>
    public sealed class LinkController : IDisposable
    {
        private readonly AppSession _session;

        private LinkLeader _linkLeader;
        private LinkFollower _linkFollower;

        /// <summary>The link's network ports. Only changed by tests, so they don't collide with a running app.</summary>
        public static int DataPort = LinkProtocol.DataPort;
        public static int DiscoveryPort = LinkProtocol.DiscoveryPort;

        /// <summary>Bluetooth is used automatically where the PC has it. Tests switch it off so they never touch a real radio.</summary>
        public static bool BluetoothEnabled = true;

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
        private FollowerStatus _lastFollowerStatus = FollowerStatus.Searching;

        /// <summary>Why the main PC's link could not start, for the setup window.</summary>
        private string _linkLeaderError = string.Empty;

        /// <summary>
        /// Second screen: the largest recent difference from the main PC's countdown, in milliseconds
        /// (before correction). Fades over a few seconds, so it shows how well in step the screens are now.
        /// </summary>
        private double _linkDriftPeakMs;

        public LinkController(AppSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));

            // Link events arrive on background threads; hand them to the thread that owns the session.
            SynchronizationContext context = SynchronizationContext.Current;
            if (context != null) Post = action => context.Post(delegate { action(); }, null);
            else Post = action => action();
        }

        /// <summary>
        /// Runs an action on the session's own thread. Set from the thread the controller was made
        /// on; tests replace it with a queue they pump themselves.
        /// </summary>
        public Action<Action> Post { get; set; }

        /// <summary>True while this PC is a second screen: the main PC has the controls.</summary>
        public bool IsFollowing
        {
            get { return _linkFollower != null; }
        }

        /// <summary>Second screen: true once the main PC's program has arrived.</summary>
        public bool HasLeaderProgram
        {
            get { return _hasLeaderProgram; }
        }

        public double DriftPeakMs
        {
            get { return _linkDriftPeakMs; }
        }

        /// <summary>A short message for the screen ("SECOND SCREEN LINKED: GYM-PC2") and how many seconds to show it.</summary>
        public event Action<string, double> ToastRequested;

        /// <summary>The link's state changed in a way the screen shows (role, status, screens joining or leaving).</summary>
        public event Action Changed;

        private TimerSettings Settings { get { return _session.Settings; } }
        private TimerSettings OwnSettings { get { return _session.OwnSettings; } }
        private SessionEngine Engine { get { return _session.Engine; } }

        private void ShowToast(string message, double seconds)
        {
            Action<string, double> handler = ToastRequested;
            if (handler != null) handler(message, seconds);
        }

        private void OnChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        private void OnSession(Action action)
        {
            Post(action);
        }

        // ------------------------------------------------------------------ start / stop

        /// <summary>Starts (or restarts) the link from this PC's settings.</summary>
        public void Start()
        {
            Stop(true);
            TimerSettings own = OwnSettings;

            if (own.IsLinkLeader)
            {
                if (string.IsNullOrWhiteSpace(own.LinkCode))
                {
                    own.LinkCode = LinkProtocol.NewCode();
                    own.Save();
                }

                LinkLeader leader = new LinkLeader(Environment.MachineName, own.LinkCode, DataPort, DiscoveryPort);
                leader.FollowerJoined += name =>
                {
                    _linkResendAll = true; // the next tick sends the program and state to everyone
                    OnSession(() => { if (_linkLeader == leader) ShowToast("SECOND SCREEN LINKED: " + name.ToUpperInvariant(), 4); });
                };
                leader.FollowersChanged += () => OnSession(OnChanged);

                // Bluetooth needs no switch: if this PC has it, second screens may use it. If it can't start,
                // the network link runs without it (the Link displays window shows which are ready).
                if (BluetoothEnabled && BluetoothLink.HasRadio()) leader.AddListener(new BluetoothLinkListener());

                if (!leader.Start())
                {
                    _linkLeaderError = leader.StartError ?? string.Empty;
                    ShowToast("LINK NOT STARTED: " + leader.StartError, 8);
                    leader.Dispose();
                    return;
                }
                _linkLeaderError = string.Empty;
                _linkLeader = leader;
            }
            else if (own.IsLinkFollower)
            {
                // The second screen works the route out by itself: what worked last time, a typed address,
                // the main PC's announcement on the network, then Bluetooth to a paired computer.
                LinkFollower follower = new LinkFollower(Environment.MachineName, own.LinkCode, new LinkRoutePlan
                {
                    LastGoodRoute = own.LinkLastRoute,
                    TypedAddress = own.LinkAddress,
                    UseNetwork = true,
                    UseBluetooth = BluetoothEnabled,
                    BluetoothAddress = own.LinkBluetoothAddress
                }, DataPort, DiscoveryPort);
                _lastFollowerStatus = FollowerStatus.Searching;
                follower.ProgramReceived += (settingsText, script) => OnSession(() => { if (_linkFollower == follower) OnLeaderProgram(settingsText, script); });
                follower.StateReceived += message => OnSession(() => { if (_linkFollower == follower) OnLeaderState(message); });
                follower.StatusChanged += () => OnSession(() => { if (_linkFollower == follower) OnFollowerStatusChanged(); });
                _linkFollower = follower;
                follower.Start();
            }

            OnChanged();
        }

        /// <summary>Stops the link. A second screen goes back to its own program and settings.</summary>
        public void Stop(bool restoreOwnProgram)
        {
            if (_linkLeader != null) { _linkLeader.Dispose(); _linkLeader = null; }
            if (_linkFollower != null) { _linkFollower.Dispose(); _linkFollower = null; }

            _sentProgramKey = null;
            _sentPhase = -1;
            _lastProgramCheckMs = -100000;
            _lastStateSentMs = -100000;
            _receivedProgramKey = null;
            _hasLeaderProgram = false;
            _linkDriftPeakMs = 0;
            _linkLeaderError = string.Empty;

            _session.RestoreOwnSettings(restoreOwnProgram);
        }

        /// <summary>Changes this PC's link settings, saves them and restarts the link with them.</summary>
        public void ApplyChange(Action<TimerSettings> change)
        {
            TimerSettings own = OwnSettings;
            string before = own.LinkKey();
            change(own);
            if (own.LinkKey() == before) return;

            // A different main PC or code: what worked last time no longer applies.
            if (!own.IsLinkFollower) own.LinkLastRoute = string.Empty;
            own.Save();
            Start();
        }

        /// <summary>The link right now, for the setup window.</summary>
        public LinkStatus GetStatus()
        {
            TimerSettings own = OwnSettings;
            LinkStatus status = new LinkStatus { Role = own.IsLinkLeader ? "lead" : own.IsLinkFollower ? "follow" : "off", Code = own.LinkCode ?? string.Empty };

            LinkLeader leader = _linkLeader;
            if (leader != null)
            {
                status.LeaderRunning = true;
                status.Screens.AddRange(leader.FollowerDescriptions);
                status.BluetoothListening = leader.ActiveTransports.Contains("Bluetooth");
            }
            status.LeaderError = _linkLeaderError;

            LinkFollower follower = _linkFollower;
            if (follower != null)
            {
                status.FollowerState = follower.Status;
                status.Activity = follower.Activity ?? string.Empty;
                status.LeaderName = follower.LeaderName ?? string.Empty;
                status.Via = follower.ConnectedVia ?? string.Empty;
                status.ClockMatched = follower.TryLeaderNow(out _);
                status.Quality = follower.Quality;
                status.FailedRounds = follower.FailedRounds;
                status.SessionRunning = Engine.InTimedPhase && !Engine.IsPaused;
                status.DriftPeakMs = _linkDriftPeakMs;
            }
            return status;
        }

        // ------------------------------------------------------------------ main PC: publish

        /// <summary>
        /// Called on every tick. Sends the program when it changes (and to a screen that has just
        /// joined), and the session state about 4 times a second - or at once when something
        /// changes (start, pause, skip, back, reset), so second screens react without delay.
        /// </summary>
        public void Tick()
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
                string settingsText = Settings.ToText(false);
                string script = Settings.ProgramScript ?? string.Empty;
                string key = settingsText + "\u0001" + script;
                if (key != _sentProgramKey)
                {
                    _sentProgramKey = key;
                    leader.Broadcast(new LinkMessage { Type = LinkMessage.Program, Name = Environment.MachineName, Settings = settingsText, Script = script });
                    _lastStateSentMs = -100000; // follow it with the state straight away
                }
            }

            bool paused = Engine.IsPaused;
            long phaseElapsedMs = (long)Engine.PhaseClock.Elapsed.TotalMilliseconds;
            long expectedMs = _sentPhaseElapsedMs + (Engine.PhaseClock.IsRunning && !_sentPaused ? now - _sentAtMs : 0);
            bool changed = (int)Engine.Phase != _sentPhase
                || Engine.Index != _sentIndex
                || paused != _sentPaused
                || Math.Abs(phaseElapsedMs - expectedMs) > 100; // e.g. "back" restarted the interval

            if (!changed && now - _lastStateSentMs < 250) return;

            _sentPhase = (int)Engine.Phase;
            _sentIndex = Engine.Index;
            _sentPaused = paused;
            _sentPhaseElapsedMs = phaseElapsedMs;
            _sentAtMs = now;
            _lastStateSentMs = now;

            leader.Broadcast(new LinkMessage
            {
                Type = LinkMessage.State,
                Phase = (int)Engine.Phase,
                Index = Engine.Index,
                PhaseElapsedMs = phaseElapsedMs,
                SessionElapsedMs = (long)Engine.SessionClock.Elapsed.TotalMilliseconds,
                Paused = paused,
                LeaderTimeMs = now
            });
        }

        // ------------------------------------------------------------------ second screen: follow

        private void OnFollowerStatusChanged()
        {
            LinkFollower follower = _linkFollower;
            if (follower == null) return;

            // This also fires when only the activity text changes ("Trying Bluetooth..."): announce a status once.
            FollowerStatus now = follower.Status;
            if (now != _lastFollowerStatus)
            {
                _lastFollowerStatus = now;
                switch (now)
                {
                    case FollowerStatus.Connected:
                        _snapOnNextState = true;
                        ShowToast("LINKED TO THE MAIN PC" + (follower.LeaderName.Length > 0 ? ": " + follower.LeaderName.ToUpperInvariant() : string.Empty), 4);

                        // Remember the route that worked, so next time it is tried first.
                        TimerSettings own = OwnSettings;
                        if (follower.ConnectedRoute.Length > 0 && own.LinkLastRoute != follower.ConnectedRoute)
                        {
                            own.LinkLastRoute = follower.ConnectedRoute;
                            own.Save();
                        }
                        break;
                    case FollowerStatus.WrongCode:
                        ShowToast("LINK CODE NOT ACCEPTED  -  PRESS L TO CHECK IT", 8);
                        break;
                }
            }
            OnChanged();
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
            leaderSettings.CopyLinkFrom(own);

            _hasLeaderProgram = true;
            _snapOnNextState = true;

            _session.ShowLeaderSettings(leaderSettings);
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

            bool parallel = Engine.Program.Mode == ExecutionMode.Parallel;
            bool sameStep = leaderPhase == Engine.Phase && (leaderPhase != Phase.Active || parallel || leaderIndex == Engine.Index);
            bool localPaused = Engine.IsPaused;
            bool snap = _snapOnNextState;
            _snapOnNextState = false;

            if (!snap && sameStep && (!leaderTimed || leaderPaused == localPaused))
            {
                if (!leaderTimed) return;

                double local = Engine.PhaseClock.Elapsed.TotalSeconds;
                _linkDriftPeakMs = Math.Max(Math.Abs(leaderElapsed - local) * 1000.0, _linkDriftPeakMs * 0.97);
                if (leaderPaused)
                {
                    if (Math.Abs(leaderElapsed - local) > 0.05) Engine.PhaseClock.Set(TimeSpan.FromSeconds(leaderElapsed), false);
                }
                else
                {
                    double corrected = ClockSync.Correct(local, leaderElapsed);
                    if (corrected != local) Engine.PhaseClock.Set(TimeSpan.FromSeconds(corrected), true);
                }

                if (Math.Abs(Engine.SessionClock.Elapsed.TotalSeconds - leaderSession) > 0.3)
                {
                    Engine.SessionClock.Set(TimeSpan.FromSeconds(leaderSession), !leaderPaused);
                }
                return;
            }

            // A different step. When both are running and a fraction of a second apart on the
            // timeline, one of us has simply crossed into the next block a moment before the
            // other - this screen's own engine gets there by itself (with its own cue), so leave it.
            if (!snap && !leaderPaused && !localPaused && Engine.InTimedPhase)
            {
                double? gap = TimelineGap(leaderPhase, leaderIndex, leaderElapsed);
                if (gap.HasValue && Math.Abs(gap.Value) < 0.3) return;
            }

            Engine.SnapTo(leaderPhase, leaderIndex, leaderElapsed, leaderSession, leaderPaused);
        }

        /// <summary>Seconds from this screen's position to the main PC's along the program, or null when they can't be compared.</summary>
        private double? TimelineGap(Phase leaderPhase, int leaderIndex, double leaderElapsed)
        {
            double? theirs = TimelinePosition(leaderPhase, leaderIndex, leaderElapsed);
            double? ours = TimelinePosition(Engine.Phase, Engine.Index, Engine.PhaseClock.Elapsed.TotalSeconds);
            if (!theirs.HasValue || !ours.HasValue) return null;

            double gap = theirs.Value - ours.Value;
            if (Engine.Program.Continuous && Engine.Program.Mode != ExecutionMode.Parallel && leaderPhase == Phase.Active && Engine.Phase == Phase.Active)
            {
                // A looping program: the last block and the first are neighbours.
                double cycle = SequenceOffset(Engine.Sequence.Count);
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
            bool parallel = Engine.Program.Mode == ExecutionMode.Parallel;
            switch (phase)
            {
                case Phase.Prep:
                    return elapsed - Math.Max(1, Settings.PrepSeconds);
                case Phase.Active:
                    return parallel ? elapsed : SequenceOffset(index) + elapsed;
                case Phase.Finished:
                    if (parallel || Engine.Program.Continuous) return null;
                    return SequenceOffset(Engine.Sequence.Count);
                default:
                    return null;
            }
        }

        /// <summary>Seconds of program before the block at <paramref name="index"/>.</summary>
        private double SequenceOffset(int index)
        {
            double total = 0;
            for (int i = 0; i < index && i < Engine.Sequence.Count; i++) total += Math.Max(1, Engine.Sequence[i].Block.Seconds);
            return total;
        }

        // ------------------------------------------------------------------ display

        /// <summary>Second screen, Parallel programs: is this station one of the ones this screen shows?</summary>
        public bool ShowsStation(int stationIndex, int stationCount)
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

        /// <summary>
        /// The small link status line at the bottom of the screen, or empty when the link is off.
        /// <paramref name="isWarning"/> is true when it should stand out (looking, reconnecting, wrong code).
        /// </summary>
        public string BadgeText(bool detail, out bool isWarning)
        {
            isWarning = false;

            LinkLeader leader = _linkLeader;
            if (leader != null)
            {
                int count = leader.FollowerNames.Count;
                if (count > 0) return "LINKED  -  " + count + (count == 1 ? " SECOND SCREEN" : " SECOND SCREENS");

                // The address (only needed when a second screen can't find this PC) is in the Link displays window.
                bool bluetooth = leader.ActiveTransports.Contains("Bluetooth");
                return "LINK ON" + (bluetooth ? " (NETWORK + BLUETOOTH)" : string.Empty) + "  -  CODE " + OwnSettings.LinkCode + "  -  NO SECOND SCREEN YET  (L = SETUP)";
            }

            LinkFollower follower = _linkFollower;
            if (follower != null)
            {
                switch (follower.Status)
                {
                    case FollowerStatus.Connected:
                    {
                        string following = "FOLLOWING " + (follower.LeaderName.Length > 0 ? follower.LeaderName.ToUpperInvariant() : "THE MAIN PC");
                        if (!detail || !follower.TryLeaderNow(out _)) return following;

                        // How far apart the two screens can be: the drift seen, plus half the ping
                        // (the uncertainty in matching the two PCs' clocks).
                        long ping = follower.RoundTripMs;
                        if (!Engine.InTimedPhase) return following + "  -  PING " + ping + " MS"; // nothing counting down to compare yet
                        long within = (long)Math.Ceiling(_linkDriftPeakMs + (ping / 2.0));
                        return following + "  -  IN STEP WITHIN " + within + " MS  (PING " + ping + ")";
                    }
                    case FollowerStatus.Reconnecting:
                        isWarning = true;
                        return "RECONNECTING TO THE MAIN PC...";
                    case FollowerStatus.WrongCode:
                        isWarning = true;
                        return "LINK CODE NOT ACCEPTED  -  PRESS L";
                    default:
                    {
                        // What it is trying right now ("TRYING BLUETOOTH (GYM-PC)..."), so the screen is never just stuck.
                        isWarning = true;
                        string activity = (follower.Activity ?? string.Empty).ToUpperInvariant();
                        return (activity.Length > 0 ? activity : "LOOKING FOR THE MAIN PC...") + "  (L = SETUP AND HELP)";
                    }
                }
            }

            return string.Empty;
        }

        public void Dispose()
        {
            Stop(false);
        }
    }
}
