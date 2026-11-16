# Plan: Linked displays (two or more PCs in sync)

**Status:** being built as **Gym Interval Clock version 4** (on the `version-4` branch). Phases 0–4 are built. Chris ran the first two-PC tests on 16–17 Nov 2026: the network test and linked displays both worked, and the Bluetooth link worked once the PCs were paired and joined by a Personal Area Network (section 6.3). Timing has not been measured on two PCs yet.
**Goal:** run one session from a "leader" PC and show it, in sync, on one or more "follower" PCs, each driving its own screen or projector, with no video streaming and no internet needed.

---

## 0. How version 4 is organised

Linked displays is the headline feature of **version 4.0**. The work is kept apart from the released 3.x app so that fixes can still go out while it is being built.

**Branches**

| Branch | Holds | Version in `GymClock.csproj` |
|---|---|---|
| `main` | the released 3.x app; 3.1.x fixes only | `3.1.x` |
| `version-4` | all linked-display work (phases 0–4) | `4.0.0-preview.N` |

- Do all version 4 work on `version-4`: `git checkout version-4`. Commit and push there, not to `main`.
- A fix to the released app goes on `main` first, as before. Then bring it into version 4 with `git checkout version-4` and `git merge main`.
- GitHub Actions builds and tests both branches on every push.

**Version numbers**

- While in progress, the version is `4.0.0-preview.N`. The window title shows "Gym Interval Clock Version 4.0 Preview N", so a test PC is easy to recognise.
- `<AssemblyVersion>` and `<FileVersion>` are `3.99.N.0` for preview N, which is just below 4.0.0. The update check compares the assembly version, so a test PC running a preview is still offered the real 4.0.0 when it is released.
- Bump N in all three (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`) each time a build goes to the gym for testing.

**Test builds for the gym (pre-releases)**

1. Set `<Version>` to the next `4.0.0-preview.N`, and `<AssemblyVersion>` and `<FileVersion>` to `3.99.N.0`. Commit on `version-4`.
2. Tag it: `git tag -a v4.0.0-preview.N -m "Gym Interval Clock v4.0.0-preview.N"`.
3. Push: `git push`, then `git push origin v4.0.0-preview.N`.
4. GitHub Actions publishes a **pre-release** with `GymClock.zip` attached. Any tag with a `-` in it is a pre-release, so it is never shown as the latest version.

For quick local tests, a single-file exe can be published to `%USERPROFILE%\Downloads\GymClock-v4-test-N` instead (see the publish command in the memory notes).

**Releasing 4.0.0**

1. Make sure all tests pass on `version-4` and the gym testing in section 7 is done.
2. Merge into `main`: `git checkout main`, `git merge version-4`.
3. On `main`, set `<Version>4.0.0</Version>` and `<AssemblyVersion>` and `<FileVersion>` to `4.0.0.0`. Commit, tag `v4.0.0` ("Gym Interval Clock v4.0.0") and push the commit and the tag.
4. The release workflow publishes it as the latest release.
5. After that, `main` carries 4.x and the `version-4` branch can be deleted.
6. Update `version.txt` on the licence site to `4.0.0`. Every PC on 3.x, and every test PC on a preview, is then told the new version is available.

**Scope of version 4.0**

- Phases 0–3 in section 6.
- Phase 4 (Bluetooth) is optional and can follow in a later 4.x.
- The decisions in section 9 are fixed for 4.0.

---

## 1. What the teacher gets

- **One set of controls.** Start, pause, skip and reset happen on the leader. Every linked screen follows within a fraction of a second.
- **A screen anywhere in the gym.** Each display needs only a laptop or mini-PC next to it, so there are no long HDMI runs and no Miracast lag or drop-outs.
- **The right view on each screen** (see section 9):
  - **Mirror:** for one-clock programs (Shared, Circuit, Sequential), the follower shows the same picture as the leader.
  - **Chosen stations:** for Parallel programs, each follower shows the stations it is set to, e.g. stations 5–8 at the far end of a big gym, with larger tiles.
- **Keeps going if the link drops.** A follower carries on counting by itself, because the timing is predictable, and shows a small "reconnecting" badge until the link returns.

## 2. Approach: share the *state*, not the picture

Every display can already work out the whole screen from just three things:

1. **the program script** (same parser on every PC),
2. **where the run is** (phase, current step, elapsed time), and
3. **whether it's paused.**

Parallel mode already works this way: every tile is calculated from one clock (`ParallelClock.Locate`, `ParallelRotationPlan.Locate`). The leader therefore only needs to send:

- the **program** once, again whenever it changes, and
- a small **state heartbeat** about 4 times a second.

Each follower draws at full resolution itself. That uses tiny bandwidth, gives sharp text, and Wi-Fi jitter doesn't show on screen.

```
 Leader PC (teacher)                         Follower PC(s)
 ┌───────────────────┐   PROGRAM (on change)  ┌────────────────────┐
 │ MainForm + timer  │ ─────────────────────▶ │ MainForm (follow)  │
 │ LinkLeader        │   STATE x4/sec         │ LinkFollower       │
 │  - discovery      │ ─────────────────────▶ │  - ClockSync       │
 │  - pairing code   │ ◀───────────────────── │  - role / stations │
 └───────────────────┘   PONG (clock sync)    └────────────────────┘
```

## 3. Protocol (v1)

**Transport**
- **Discovery:** UDP broadcast on the local network (port 47215).
- **Data:** one TCP connection per follower (port 47216).
- **Fallback:** if broadcast is blocked, the teacher types the leader's address, which the leader shows on screen.

**Format:** one JSON message per line (newline-delimited). Every message carries `v` (protocol version) and `seq`.

| Message | Direction | Contents |
|---|---|---|
| `ANNOUNCE` | leader → broadcast, every 2 s | leader name, address, TCP port, whether it's accepting followers |
| `HELLO` | follower → leader | follower name, protocol version, proof of the pairing code |
| `PROGRAM` | leader → follower | full script text and its hash. Sent on connect and whenever the program changes. |
| `STATE` | leader → follower, ~250 ms, and immediately on any change | `phase`, `index`, `phaseElapsedMs`, `sessionElapsedMs`, `paused`, `programHash`, `leaderTimeMs` |
| `PING` / `PONG` | both ways | timestamps for measuring round-trip time and clock offset |
| `BYE` | either | clean disconnect |

**Clock sync (`ClockSync`)**
- The follower estimates the one-way delay as half the measured round-trip time (on a local network this is normally under 20 ms).
- Displayed elapsed time = `phaseElapsedMs` from the last `STATE` + time since it arrived + one-way delay.
- **Small drift (under 250 ms)** is eased out gradually, so countdowns never jump backwards.
- **Larger differences** (after a pause, skip or reconnect) snap straight to the leader's value.

**Pairing and security**
- The leader shows a **4-digit link code** when linking is switched on. Followers must enter it before they get anything.
- Messages are signed with a key derived from the code (HMAC-SHA256), so another PC on the network can't take over a screen.
- Followers have **no remote control** in v1: they only receive.
- No internet traffic. Everything stays on the local network.

## 4. Code changes

### New: `src/GymClock/Link/`
| File | Purpose |
|---|---|
| `LinkMessage.cs` | Message types plus JSON read/write (System.Text.Json) |
| `LinkLeader.cs` | Discovery announcements, TCP listener, sends to all followers, pairing check |
| `LinkFollower.cs` | Finds the leader or connects by address, reconnects, raises `ProgramReceived` / `StateReceived` |
| `ClockSync.cs` | Round-trip/offset estimate and the smoothing logic. Pure code, unit-tested. |
| `LinkSettings.cs` | Role (Off / Lead / Follow), link code, display role, station range. Stored in `settings.txt`. |

### Changes to existing code
- **`MainForm`:** move the run state into one `RunState` object that can be copied:
  - today it lives in `_phase`, `_index`, `_phaseClock`, `_sessionClock`, `IsPaused`, the prep countdown and the cue bookkeeping;
  - **as leader**, every change publishes a `STATE` message;
  - **as follower**, `RunState` is set from received state, and only the display keys (F6, F11, Q) stay active;
  - drawing reads `RunState` instead of the separate fields.
- **`MainForm` drawing:**
  - pass a **display role** into `OnPaint`: Mirror for one-clock programs, or Stations X–Y for Parallel (see section 9);
  - `PaintParallelDashboard` takes a station range;
  - add a small corner badge: "LINKED - Leader: Gym PC 1" / "RECONNECTING…".
- **Sound:** each follower has its own sound on/off setting, set on the follower (default off).
- **Settings window:** a "Link displays" section with:
  - Off / Lead this session / Follow another PC,
  - the link code,
  - the station range shown for Parallel programs, the follower sound setting, and
  - "Connect by address".
- **`ProgramLibrary` / startup:** a follower doesn't load `current.program.txt`. It waits for `PROGRAM` and shows "Waiting for leader…".

## 5. School network realities (test first)

School Wi-Fi often has **client isolation**, which means devices can't reach each other directly. Options, in order:

1. **Same wired network / same Wi-Fi network**, if allowed. Test in Phase 0.
2. **A dedicated travel router or phone hotspot** in the gym. The PCs join it, and no internet is needed.
3. **A network cable** between two laptops, or a small switch.
4. **An IT exception** for ports 47215/47216 between the gym PCs.

The first time the app runs as leader, Windows asks to allow it through the firewall. On managed laptops, IT may need to approve this or push the rule out.

## 6. Delivery phases

| Phase | Scope | Done when |
|---|---|---|
| **0. Spike** (½ day) | Small console test: broadcast and TCP heartbeat between two school PCs, on Wi-Fi and on a hotspot | We know which network setup works in the gym |
| **1. Mirror MVP** | `RunState` refactor, `LinkLeader`/`LinkFollower`, `PROGRAM` + `STATE`, `ClockSync`, pairing code, badge, settings | Two PCs mirror any of the 4 modes, including pause/skip/reset, within 100 ms |
| **2. Display roles** | Automatic mirror for one-clock programs; chosen station range for Parallel; per-follower settings | Everest Session 1 on 2 screens: stations 1–2 / 3–4 |
| **3. Resilience** | Reconnect, keep counting while offline, mid-session join (follower starts at the current point) | Unplug Wi-Fi for 30 s: the follower keeps time, then resyncs without a visible jump |
| **4. Bluetooth (optional)** | RFCOMM transport behind the same message interface, for venues with no usable network | Two paired laptops sync with Wi-Fi off |

**Progress (17 Nov 2026)**

| Phase | State |
|---|---|
| 0. Spike | Done. Tool built (`src/GymClock.LinkTest`); Chris ran it on two PCs and it worked. The RESULT lines were not recorded (6.1). |
| 1. Mirror | Built and **worked on two PCs** (Chris, 16–17 Nov 2026). Automated tests also link two clock windows on one PC and check start, pause, skip, back, reset and a program change mirror within 100 ms. |
| 2. Display roles | Built. One-clock programs mirror; for Parallel a second screen can show a chosen station range. |
| 3. Resilience | Built. A second screen keeps counting alone, reconnects by itself, and can join mid-session. The "unplug Wi-Fi for 30 s" check on real PCs is still to do. |
| 4. Bluetooth | Built and **worked on two PCs** (Chris, 16–17 Nov 2026), after pairing them and joining the second PC to the main PC's Personal Area Network. Whether the PAN step is needed is not known (6.3). |

### 6.1 Phase 0: running the network test

The test tool is `src/GymClock.LinkTest` (a console app, separate from the main app and not part of a release). It uses the ports and message style from section 3: announcements on UDP 47215, and a TCP connection on 47216 carrying 4 heartbeats a second plus ping/pong timing.

Build the exe to copy to both PCs:

```powershell
dotnet publish src/GymClock.LinkTest/GymClock.LinkTest.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=embedded --output "$env:USERPROFILE\Downloads\GymClock-LinkTest"
```

Run it once on each network to be checked (school Wi-Fi, then a phone hotspot):

1. Copy `GymClock.LinkTest.exe` to both PCs and connect both to the same network.
2. On the main PC, run it and choose **1**. If Windows asks about the firewall, choose Allow. It shows this PC's address.
3. On the second PC, run it and choose **2** (find automatically). If Windows asks about the firewall, choose Allow.
4. If it can't find the main PC, type the main PC's address when asked (or run again and choose **3**).
5. Watch the "shared clock" line on both screens for a few seconds: the countdown should change at the same moment on both.
6. After 30 seconds the second PC prints a RESULT line and saves `LinkTest-report-<PC>-<date>.txt` next to the exe (or on the Desktop).

What the result means:

| Result | Meaning |
|---|---|
| **GOOD** | The network works, including finding the main PC automatically. |
| **GOOD, BY ADDRESS** | Timing is fine, but broadcast is blocked: followers will need "Connect by address". |
| **USABLE** | It works, but slower or less steady than the 100 ms target. |
| **POOR / NOT WORKING** | Use a hotspot or cable, or ask IT about UDP 47215 and TCP 47216 (section 5). |

Results. Chris reported on 17 Nov 2026 that the test worked on two PCs; which network it was on, and the RESULT lines, were not recorded. Fill these in next time it is run:

| Network | Find | Connect | Average ping | Result |
|---|---|---|---|---|
| School Wi-Fi | | | | |
| Phone hotspot | | | | |

### 6.2 Trying linked displays in the app

Use a version 4 preview build on both PCs (section 0), on the same network.

1. **Main PC:** press **L**, choose "This is the MAIN PC" and press OK. The bottom of the screen shows `LINK ON - CODE 1234 - NO SECOND SCREEN YET`. If Windows asks about the firewall, choose Allow.
2. **Second PC:** press **L**, choose "This is a SECOND SCREEN", type the code from the main PC and press OK. Leave the address blank to find the main PC automatically. If Windows asks about the firewall, choose Allow.
3. The second PC shows `FOLLOWING <main PC>` and the main PC shows `LINKED - 1 SECOND SCREEN`. The second screen now shows the main PC's program.
4. Run the session from the main PC as normal. Start, pause, skip, back and reset all show on the second screen.

If the second PC stays on `LOOKING FOR THE MAIN PC...`: press **L** on the main PC to see its address, then press **L** on the second PC and type it in "Main PC's address". If that also fails, the network is blocking PC-to-PC traffic (section 5).

On a second screen:

- The session keys do nothing (the main PC has the controls). **L** (link settings), **M** (sound on this screen), **F11**, **F6**, **F3** and **Q** still work.
- Sound is off unless "Play the sounds on this screen too" is ticked (or press **M**).
- "Show stations 3 to 4" applies to Parallel programs only. Other programs always mirror the main PC.
- Choosing "Not linked" returns it to its own program and settings, which are never changed by following.

### 6.3 Linking over Bluetooth (Phase 4)

For a venue with no network the two PCs can share. Bluetooth reaches about 10 m and walls reduce it, so keep the two PCs in the same room.

The same steps are in the app: press **L**, then "How to set up Bluetooth between two PCs...". That window also has buttons that open the two Windows screens used below.

**In Windows**

1. **Turn Bluetooth on (both PCs).** Open Settings > Bluetooth & devices and switch Bluetooth on. Leave this Settings page open on both PCs while pairing: a PC can only be found while it is open.
2. **Pair the two PCs (once only).** On the second PC choose Add device > Bluetooth, then pick the main PC's name. A number appears on both screens. Check they match, then choose Connect on one PC and Yes on the other.
   - If the main PC is not in the list: on the second PC, scroll down in Bluetooth & devices > Devices, set "Bluetooth devices discovery" to Advanced, and try Add device again.
3. **Join the Personal Area Network (second PC).** Right-click the Bluetooth icon near the clock and choose "Join a Personal Area Network". Click the main PC, then choose Connect using > Direct connection. Do this each time the PCs are switched on.

**In Gym Interval Clock**

4. **Main PC:** press **L**, choose "This is the MAIN PC" and tick "Also accept second screens over Bluetooth". The bottom line shows `LINK ON (NETWORK + BLUETOOTH) - CODE 1234`.
5. **Second PC:** press **L**, choose "This is a SECOND SCREEN", type the code, tick "Connect by Bluetooth instead of the network" and choose the main PC from the list of paired devices. If exactly one paired device is a computer, it is already selected.
6. The second PC shows `CONNECTING TO THE MAIN PC BY BLUETOOTH...` (this can take several seconds), then `FOLLOWING <main PC>`.

The main PC keeps accepting network second screens at the same time. If Bluetooth can't start on the main PC, the network link still runs and a message says why.

**If it will not connect:** check Bluetooth is on at both PCs, that each PC lists the other as Paired, and that the main PC's screen says `(NETWORK + BLUETOOTH)`.

**What the two-PC test showed, and what it didn't (17 Nov 2026)**

- With the PCs paired and the second PC joined to the Personal Area Network, the link worked in the app.
- The PAN was joined before the app was tried, so **it is not known whether step 3 is needed.** The app's own Bluetooth connection (RFCOMM) should need only pairing. Step 3 is in the instructions because it is the setup known to work.
- A joined PAN is also an ordinary network between the two PCs, so the link may have run over it as a network link instead of the app's Bluetooth connection. Chris could not recall whether "Connect by Bluetooth" was ticked.
- To settle both: pair the PCs, skip step 3, tick "Connect by Bluetooth" on the second PC and see whether it links. If it does, step 3 can be dropped from the instructions and the help window.

### 6.4 How it was built (differences from sections 3 and 4)

- **A second screen runs the normal timing engine itself.** It is not a passive display fed a `RunState` object. Each `STATE` message corrects it: a drift under 0.25 s is eased out (a quarter of the gap per message), anything larger snaps to the main PC's position. This is why it keeps counting when the link drops, and why joining mid-session works: both came with the design instead of needing Phase 3 work.
- **No `RunState` refactor of `MainForm`.** The two session clocks became `RunClock` (a stopwatch that can be set to a position). The main PC's state is read from the existing fields each tick and sent when something changes, or every 250 ms.
- **`PROGRAM` carries the settings as well as the script** (`TimerSettings.ToText(false)` plus `ProgramScript`), so colours, labels, prep time and the simple (non-program) session all mirror. Link settings are never sent or accepted.
- **Signing:** every line is `HEX(HMAC-SHA256) + space + JSON`, with the key derived from the link code. Announcements are unsigned but carry a tag only a PC with the same code can produce, so a second screen ignores other main PCs on the network.
- **The link window is opened with L** from the clock screen, or with the "Link displays..." button in the settings window. Changes made from the settings window take effect when that window is closed with OK.
- **How well in step:** while the key hints are showing (press **H**), a second screen's status line reads `FOLLOWING <main PC> - IN STEP WITHIN 15 MS (PING 8)`. The figure is the largest recent difference from the main PC's countdown plus half the ping, which is the uncertainty in matching the two PCs' clocks.
- **Transports:** the main PC and second-screen code only see a stream (`ILinkConnection`, `ILinkListener` in `LinkTransport.cs`). TCP and Bluetooth are two implementations, so everything above the connection (link code, signing, clock matching, mirroring) is shared.
- **Bluetooth** (`BluetoothLink.cs`) uses Windows' own Bluetooth sockets (RFCOMM through `ws2_32`) and paired-device list (`BluetoothApis`) directly, with no extra library. The main PC advertises the service id `6f0c7a52-3b1e-4d7a-9a57-47796d436c6b`; a second screen connects to the paired main PC by that id. There is no searching over Bluetooth: the teacher picks the paired main PC.
- Link code is in `src/GymClock/Link/` and `src/GymClock/MainForm.Link.cs`. Tests are `LinkCoreTests`, `LinkNetworkTests` and `LinkBluetoothTests`.

**Still to do before 4.0.0**

- Repeat the two-PC tests on each network the gym will use (school Wi-Fi, phone hotspot) and record the RESULT lines in 6.1. Try real programs in all four modes.
- Record the real screen-to-screen difference: on the second PC press **H** during a session and note the `IN STEP WITHIN ... MS` figure, on each network and over Bluetooth. The target is 100 ms.
- Sound on a second screen has not been listened to on real hardware.
- **Bluetooth:** find out whether the Personal Area Network step is needed (see the end of 6.3). Still unchecked: how long connecting takes, whether the screens stay within 100 ms, what happens when one PC goes out of range and comes back, and whether school-managed PCs allow it.

## 7. Testing

- **Unit tests:**
  - `ClockSync` maths: offset, smoothing, snapping;
  - message round-trips and the signature check;
  - `RunState` copy and apply.
- **Integration test:** leader and follower in one test process over loopback (127.0.0.1). Drive the leader through start / pause / skip / finish, and assert the follower's state matches at each step.
- **Gym checklist:**
  - all 4 modes, and an Everest sample per mode;
  - pause/resume, skip, back/restart, reset;
  - a follower joining mid-session;
  - Wi-Fi drop and recovery;
  - 30-minute soak test (drift under 100 ms).

## 8. Risks

| Risk | Mitigation |
|---|---|
| Network blocks PC-to-PC traffic | Phase 0 spike first; hotspot/router fallback; connect by address |
| Firewall prompt on managed laptops | Document the ports; IT rule; clear in-app message if the leader can't listen |
| Leader and follower on different app versions | `v` in every message; the follower shows "Update needed" instead of guessing |
| Program edited on the leader mid-session | New `PROGRAM` + hash; followers swap at the next `STATE` with the new hash |
| Sounds out of step between rooms | Sound off on followers by default |

## 9. Decisions (answered by Chris, 13 Nov 2026)

1. **Network:** use the school Wi-Fi first, with a phone hotspot as the fallback when the school network blocks the link. The link screen should say which network it is using and suggest the hotspot if the leader can't be found.
2. **Control:** followers are **display only**. A follower can never start, pause, skip or change the session; all control stays on the main (leader) PC.
3. **Display role:** this depends on the program running on the leader.
   - If the leader shows **one clock** (Shared, Circuit, Sequential), the follower mirrors it automatically.
   - If the leader shows **several timers** (Parallel), the follower chooses which stations to show, e.g. the leader shows stations 1-2 and the follower shows 3-4. Until a range is chosen it shows all stations.
   - The follower decides this when a program loads, so a follower left running needs no changes between sessions.
4. **Sound:** each follower has its own **sound on/off** setting, set on the follower PC (default off). When it is on, the follower plays the same cues as the leader, at the leader's times.

## 10. Rough effort

- Phase 0: ½ day
- Phase 1: 3–4 days
- Phase 2: 1–2 days
- Phase 3: 1–2 days
- Phase 4 (Bluetooth): 2–3 days, if needed

These are development estimates, plus gym testing time.
