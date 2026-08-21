# Gym Interval Clock

A full-screen C# Windows Forms interval training timer, designed to be projected onto a
gym wall. Shows the time of day in the corner and a huge countdown for a work/rest
interval pair — 45/15, 40/20, 20/10 — that can be changed at any time, including
mid-session.

Everything on screen is custom painted and sized as a fraction of the window, so it
fills the wall properly at whatever resolution the projector happens to be running.

```
45s WORK / 15s CHANGE OVER x 10                        3:07:22 PM
Session 04:12

                            BURPEES
                             WORK

                              38

                        ROUND 3 OF 10
                      Next: CHANGE OVER for 15s

 ██████████████████████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░
 SPACE start/pause · R reset · S settings · 1=45/15 x10 · 2=40/20 x10 ...
```

- **Whole-screen colour** signals the phase, so it reads from the far end of the gym
  even when nobody can make out the digits: blue = get ready, green = work,
  amber = rest, grey = paused, purple = finished.
- The background **pulses over the last three seconds** of every interval, with a beep
  on 3‑2‑1 and a distinct double beep when work starts.
- A built-in **session builder** for circuit work: name each station, give it its
  own work/rest instructions, and show the whole list as a table down the left
  third of the screen. See [Stations / session builder](#stations--session-builder).
- Timing uses `Stopwatch`, not accumulated timer ticks, so it does not drift over a
  long session.

> **Not open source.** Free to use and pass on up to and including **30 April 2027**.
> From **1 May 2027** a licence key is required. See [Licensing](#licensing).

## Getting it running

**Option 1 — download the built bundle.** Grab `GymClock.zip` from the
[latest release](../../releases/latest), or from the artifacts of any
[build run](../../actions). It contains `GymClock.exe`, the licence, and a read-me for
whoever you hand it to. The exe is self-contained: no .NET runtime needed, one file,
copy it to the gym laptop and make a desktop shortcut.

**Option 2 — clone and build.**

```powershell
git clone https://github.com/<your-account>/gym-interval-clock.git
cd gym-interval-clock
start GymClock.sln          # or open it from Visual Studio 2022
```

Press **F5**. Requires Visual Studio 2022 with the **.NET desktop development**
workload (or just the .NET 8 SDK if you prefer the command line):

```powershell
dotnet run --project src/GymClock
```

To produce the single-file exe yourself, right-click the project in Visual Studio and
choose **Publish** (the `win-x64` profile is already committed), or:

```powershell
dotnet publish src/GymClock/GymClock.csproj -p:PublishProfile=win-x64
```

The result appears in `src/GymClock/bin/publish/win-x64/GymClock.exe`.

## Controls

| Key | Action |
|---|---|
| `Space` / `Enter` / left click | Start, pause, resume |
| `R` | Reset back to the start |
| `S` or `F2` | Open settings — works mid-session |
| `→` / `Page Down` / `N` | Skip to the next interval |
| `←` / `Page Up` / `P` | Restart this interval, or step back one |
| `1`–`9` | Apply quick preset 1–9 instantly |
| `Ctrl`+`1`–`9` | Jump straight to station 1–9's work phase |
| `M` | Mute / unmute the cues |
| `T` | Toggle always-on-top |
| `F11` or `F` | Toggle full screen |
| `F6` | Move the clock to the next monitor (i.e. the projector) |
| `F3` | Licence and version information |
| `Esc` | Leave full screen |
| `H` / `F1` | Re-show the hint bar |
| `Q` | Quit |

Arrow keys and Page Up/Down are both mapped, so most presentation clickers will step
the intervals without any configuration.

## Changing the times

Three ways, all of which take effect immediately — even halfway through an interval.
If the new interval is already shorter than the time elapsed, the clock rolls straight
on to the next one.

1. **Press `1`–`9`** for a preset. Out of the box: `1` = 45/15, `2` = 40/20,
   `3` = 30/30, `4` = 20/10 (Tabata), `5` = 60/15.
2. **Press `S`** for the settings dialog: prep countdown, work, rest, rounds, cue
   volume, wording, station list, clock format.
3. **Edit the settings file** in Notepad. The path is shown at the bottom of the
   settings dialog; by default it is `%AppData%\GymClock\settings.txt`.

```ini
prep=10                    # "get ready" countdown, 0 to skip
work=45
rest=15
rounds=10                  # 0 = keep looping until you stop it
plan=                      # optional variable structure, see below
restafterfinalround=false
sound=true
volume=0.85
alwaysontop=true
showclock=true
clockformat=h:mm:ss tt     # any .NET date format string
worklabel=WORK
restlabel=REST
workcolour=#0F8A46         # #RRGGBB or a .NET colour name
restcolour=#D9822B
prepcolour=#1E63D0
pausedcolour=#333B47
donecolour=#5B4B9E
idlecolour=#1A1E25
stations=Burpees,Squats,Push-ups,Rower,Box jumps
usestationwording=false     # show each station's own instruction instead of WORK/REST
showstationstable=true      # table of stations down the left third of the screen
highlightcurrentstation=true # highlight the one in progress (off for a rotating circuit)
usemovetime=false           # extra phase between stations, e.g. for walking round to the next one
moveseconds=8
movemessage=Move clockwise to the next station
movecolour=#1E9AA6
presets=45/15x10,40/20x10,30/30x10,20/10x8,60/15x8
```

### Variable sessions

A session does not have to be evenly paced. Set `plan` to a series of blocks, each
`work/rest x repeats`, joined with `+`:

```ini
plan=45/15x2+50/10x4
```

That is two rounds of 45 on / 15 off, then four of 50 on / 10 off — six rounds, six
minutes with a 15-second lead-in. Commas work as separators too, and a bare `60/30`
means one round of it.

```ini
plan=30/30x3+45/15x4+50/10x4+60/30       # build, hold, finisher
```

When `plan` is set it overrides `work`, `rest` and the round count. `rounds=0` still
means continuous, and in that case the whole plan cycles rather than the last round
repeating. Leave `plan` blank for an evenly paced session built from `work`/`rest`/`rounds`
as before, which is what every existing settings file does.

The settings dialog (**S**) has a plan box with a live preview showing the resolved
rounds and total running time, so a typo is obvious before you are standing in front of a
class. While a variable plan is running, the display shows which block you are in and
warns you on the last rest before the pace changes.

Pressing a preset key (`1`–`9`) clears the plan and returns to an even session.

A file named `gymclock.settings.txt` placed **next to the .exe** is used in preference
to the one in `%AppData%`, which is handy for a USB-stick copy or several shortcuts
with different configurations. See [`settings-examples/`](settings-examples) for
ready-made circuit, Tabata and continuous configurations.

## Stations / session builder

The **Stations** tab of the settings dialog (**S**, second tab) is a small session
builder: one row per station, with optional instructions for what to do during the
work phase and during the rest phase, and an optional accent colour.

```
Station / exercise    During WORK          During REST        Colour
Exercise bike          80% resistance      no resistance      #2AA7A0
Skip rope               full pace          walk
Rower                  max effort          light pull          SkyBlue
```

Use **Add** to append a blank row, **Duplicate** to copy the selected station
(handy for reusing the same bike or a similar exercise elsewhere in the program),
**Remove** to delete the selected row, and **Up** / **Down** to reorder the list -
or just use the grid's own blank last row and the Delete key. A station can appear
more than once in the list; that is completely normal for reusing equipment. What
is not allowed is the *same* station running straight into itself with nothing in
between, including where the list wraps from the last station back to the first -
so **Ride bike, Skip rope, Ride bike** is fine, but **Ride bike, Ride bike** is not.
Two labels under the grid update live as you type:

- A **back-to-back warning** naming any station that repeats with nothing
  between the two - a **Duplicate** lands right next to its original for this
  exact reason, as a prompt to move it with Up/Down to where it actually belongs.
- A **session estimate** - station count, round count, and the total running
  time the current Timing settings and move time add up to.

**Import...** / **Export...** save or load just the station list as its own
`.stations.txt` file (one station per line, `Name|work|rest|colour`), so a circuit
built once can be reused on another computer or in another settings file, or
handed to another teacher. Importing offers to add to the current list or replace
it outright.

On the right, checkboxes control how it plays out on the projector:

- **Show a stations table on screen** - the grid above, always visible down the
  left third of the screen, rather than just a single station name scrolling
  through the header.
- **Replace the WORK/REST word with each station's own instruction, when it has
  one** - the huge central word becomes "80% RESISTANCE" instead of "WORK" for a
  station that has one set, and falls back to the plain WORK/REST wording
  otherwise (including for any station left with no instructions).
- **Highlight the station in progress** - turn this **on** for a linear session
  where the whole class moves through the stations together, one per round, so
  the table can point at the one they should be on. Turn it **off** for a
  rotating circuit, where every station is already staffed by a different group
  every round - highlighting just one there would be actively misleading.

Below that, **Move between stations** adds an optional extra phase after the rest
(or straight after work, if there is none) for the physical move to the next
station, with its own length and an editable instruction - e.g. 8 seconds of
"MOVE CLOCKWISE TO THE NEXT STATION" in its own colour, shown as the big central
word and as the banner line above it. It is skipped after the final round, and
`Ctrl`+`1`–`9` on the clock jumps straight to a given station's work phase if a
class needs correcting onto the right one mid-session.

A **Save** button next to OK writes straight to `settings.txt` without closing the
dialog, so a session can be built up and checked on disk before moving on; OK
still saves and applies it as before, and Cancel discards the changes.

In the settings file itself, a plain list still works exactly as it always has:

```
stations=Burpees,Squats,Push-ups,Rower,Box jumps
```

Add instructions and a colour with `Name|during work|during rest|colour`, stations
joined by `;` (trailing fields can be left blank or omitted):

```
stations=Exercise bike|80% resistance|no resistance|#2AA7A0;Skip rope|full pace|walk
usestationwording=true
showstationstable=true
highlightcurrentstation=true
usemovetime=true
moveseconds=8
movemessage=Move clockwise to the next station
```

See [`settings-examples/station-builder.txt`](settings-examples/station-builder.txt)
for a complete example, and drop a `*.stations.txt` file exported from the dialog
next to it to reuse just the station list on its own.

## Repository layout

```
GymClock.sln                       Visual Studio 2022 solution
src/GymClock/
  Program.cs                       Entry point, licence gate, high-DPI setup
  MainForm.cs                      Timing engine + all custom painting
  SettingsForm.cs                  Settings dialog, built in code
  TimerSettings.cs                 Settings model, presets, text-file persistence
  Beeper.cs                        Generates PCM WAV cues in memory
  IntervalPlan.cs                  Variable session structure (blocks of rounds)
  DiagnosticReport.cs              Support report the user can save and send
  Licensing.cs                     Expiry date, signed key verification
  LicenceDialog.cs                 Activation prompt, doubles as the About box
  GymClock.ico
  Properties/PublishProfiles/      win-x64 single-file publish profile
settings-examples/                 Drop-in gymclock.settings.txt examples
distribution/READ-ME-FIRST.txt     Bundled with the exe for recipients
.github/workflows/build.yml        Builds, bundles exe + licence, publishes tags
```

No NuGet packages and no designer (`.Designer.cs`) files — every form is built in
code, so there is nothing to merge-conflict on and the whole thing is readable
top to bottom.

## Cutting a release

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The workflow builds it and attaches `GymClock.exe` to a new GitHub release.

## Targeting .NET Framework 4.8 instead

No longer supported. `Licensing.cs` uses `ECDsa.ImportSubjectPublicKeyInfo`, which
needs .NET 6 or later. Everything else in the app is still C# 7.3 with no package
dependencies, so if you ever wanted a 4.8 build you would need to swap the licence
verification for `RSACryptoServiceProvider.ImportCspBlob`.

## Licensing

**This is not open source software.** Copyright is jointly retained by Chris Bucknell
and James Vella — see [LICENSE](LICENSE) for the full terms.

| Period | Behaviour |
|---|---|
| Until 31 March 2027 | Runs normally. Says nothing about licensing on screen. |
| 1 – 30 April 2027 | Runs normally, with an amber `UNLICENSED — n days left` warning in the corner. |
| From 1 May 2027 | Will not start. Shows the activation dialog until a valid key is entered. |

Both dates are compile-time constants at the top of `Licensing.cs`:

```csharp
public static readonly DateTime WarningStartsOn = new DateTime(2027, 4, 1);
public static readonly DateTime FreeUseEndsOn   = new DateTime(2027, 4, 30);
```

Enforcement details:
- Licence keys are **ECDSA P-256 signed**. Only the *public* key is compiled into the
  app, so decompiling the exe does not let anyone forge a key.
- A key can be perpetual or carry its own expiry, and can optionally be locked to one
  computer via a machine fingerprint derived from the Windows `MachineGuid`.
- The most recent date the app has seen is recorded in both the registry and AppData.
  Winding the clock back to before the cutoff is detected and refused.

Be realistic about what this achieves: it stops one key being passed around a
staffroom and it stops the obvious clock-rollback trick. It will not stop someone
who is willing to decompile the exe and patch the check out. Treat it as a deterrent.

### One-time setup before you release

The key generator lives in a **separate private repository** so that the signing tool
is never published alongside the app. Clone it, then:

```powershell
dotnet run -- newkeys
```

That writes `keys/gymclock-private.pem` and prints your public key. Paste it into the
`PublicKeyBase64` constant in `src/GymClock/Licensing.cs` here, then rebuild.

> **Back up `keys/gymclock-private.pem` somewhere safe and offline.** It is gitignored
> and never ships with the app. If you lose it you can never issue another key without
> invalidating every key already out there. If it leaks, anyone can mint their own.

Also set `ContactDetails` in `Licensing.cs` to a real address — it is shown to anyone
who needs a key.

The CI workflow refuses to build a tagged release while the placeholder key is still
in place, so a build that can never be licensed cannot reach a release by accident.

### Issuing a key

From the private key-generator repository:

```powershell
# Perpetual, any computer
dotnet run -- issue --name "Casey Grammar PE Department"

# Expires at the end of 2028, written to a file you can email
dotnet run -- issue --name "J. Smith, Example College" `
    --expires 2028-12-31 --out licences/jsmith.licence

# Locked to one computer (ask them for the ID shown on the F3 screen)
dotnet run -- issue --name "Gym laptop" --machine K7PQ-3MRT-92XZ
```

The recipient either pastes the key into the app (**F3 → paste → Activate**) or drops
the file next to `GymClock.exe` renamed to `gymclock.licence`.

Keys and the `licences/` folder are gitignored in both repositories, but keep your own
record of what you issued to whom — the tool does not maintain a database.

The licence string format is shared between the two repos. If you ever change it in
one, change it in the other, or every existing key stops validating.
