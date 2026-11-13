# Plan: Linked displays (two or more PCs in sync)

**Status:** proposal, not started
**Goal:** run one session from a "leader" PC and show it, in sync, on one or more "follower" PCs, each driving its own screen or projector, with no video streaming and no internet needed.

---

## 1. What the teacher gets

- **One set of controls.** Start, pause, skip and reset happen on the leader. Every linked screen follows within a fraction of a second.
- **A screen anywhere in the gym.** Each display needs only a laptop or mini-PC next to it, so there are no long HDMI runs and no Miracast lag or drop-outs.
- **Different views per screen.** For example:
  - **Mirror:** the same picture as the leader.
  - **Stations 5–8 only:** in a big gym, the far end sees just its own stations' tiles, larger.
  - **Big clock only:** the rotation countdown ("ROTATE IN 3:10") or the current phase, as large as possible.
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
  - pass a **display role** into `OnPaint`: Mirror, Stations X–Y, or Big clock;
  - `PaintParallelDashboard` takes a station range;
  - add a small corner badge: "LINKED - Leader: Gym PC 1" / "RECONNECTING…".
- **Sound:** off on followers by default, with an option to turn it on.
- **Settings window:** a "Link displays" section with:
  - Off / Lead this session / Follow another PC,
  - the link code,
  - the display role, and
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
| **2. Display roles** | Station range and "big clock" role, per-follower settings | Everest Session 1 on 2 screens: stations 1–2 / 3–4 |
| **3. Resilience** | Reconnect, keep counting while offline, mid-session join (follower starts at the current point) | Unplug Wi-Fi for 30 s: the follower keeps time, then resyncs without a visible jump |
| **4. Bluetooth (optional)** | RFCOMM transport behind the same message interface, for venues with no usable network | Two paired laptops sync with Wi-Fi off |

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

## 9. Questions to settle before starting

1. Is a **phone hotspot or travel router** in the gym acceptable, or must it run on school Wi-Fi?
2. Should a follower ever be able to **pause** the session, e.g. a second teacher? (v1: no.)
3. Which **display roles** are most useful in your gym: mirror, a range of stations, or a big rotation clock?
4. Do you want **follower sound** at all (e.g. a speaker at the far end)?

## 10. Rough effort

- Phase 0: ½ day
- Phase 1: 3–4 days
- Phase 2: 1–2 days
- Phase 3: 1–2 days
- Phase 4 (Bluetooth): 2–3 days, if needed

These are development estimates, plus gym testing time.
