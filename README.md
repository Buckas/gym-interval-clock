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
- A built-in **program builder**: Quick Setup turns a pattern (Tabata, pyramid,
  ladder, EMOM, ...) and a few numbers into a full program in seconds, and the
  Program Builder gives full control - stations with their own timing, repeat
  groups, rest/move/water-break/instruction blocks anywhere you like, or the
  same program typed as plain text. See [Building a program](#building-a-program).
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
   volume, wording, clock format, and the **Program** tab for Quick Setup / the
   Program Builder - see [Building a program](#building-a-program).
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
recoverycolour=#4A90D9     # default colours for the block types a program can use
countdowncolour=#6C3FC5
waterbreakcolour=#17A2B8
instructioncolour=#546E7A
customcolour=#B23A78

# Everything below this line only matters for a *simple* session - once a real
# program is built (see "program=" and Building a program, below) the program's
# own stations and timing take over completely.
stations=Burpees,Squats,Push-ups,Rower,Box jumps
showstationstable=true      # table of stations down the left quarter of the screen
showstationnameasdescription=false # show the station's name instead of WORK/REST/MOVE
highlightcurrentstation=true # highlight the one in progress (off for a rotating circuit)
usemovetime=false           # extra phase between stations, e.g. for walking round to the next one
moveseconds=8
movemessage=Move clockwise to the next station
movecolour=#1E9AA6

# The current program, written with the script grammar - see Building a
# program, below. Blank (the default) means no program has been built yet, so
# the clock runs the plain work/rest/rounds/stations settings above instead.
program=

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

## Building a program

The **Program** tab of the settings dialog (**S**) is where a session stops being
just "work/rest/rounds" and becomes a real, reusable **program**: any mix of
timed blocks, optionally split across stations that each run their own timing.
Two ways in, both producing the same thing:

- **Quick Setup** - a name, an execution mode, a pattern and a few numbers, with
  a live preview. **Create Program** applies it immediately; **Open In Builder**
  hands the result to the Program Builder for further shaping first.
- **Program Builder** - full control: stations with their own timing, repeat
  groups, and any block anywhere, or the same program typed as plain text in
  its Script window.

A program is entirely optional. A settings file with no program built yet
(`program=` blank, the default) runs exactly as it always has, off the plain
`work`/`rest`/`rounds`/`plan`/`stations`/move-time fields on the **Timing &
Wording** tab - nothing changes for anyone who never opens the Program tab.

### Execution modes

- **Shared Timing** - one countdown for the whole room; every station (if any
  are listed) follows it together. This is what every simple session and every
  Quick Setup pattern produces by default.
- **Sequential Stations** - each station runs its own timeline, one after
  another, with an optional shared block (e.g. a Rest or a Move) between each
  pair. Adding a station in the Program Builder switches a program to this mode
  automatically.
- **Parallel Independent Stations** - every station runs its own pattern at
  once, completely independently (they can finish at different times). Its own
  multi-station dashboard replaces the single countdown while it is running; a
  station that finishes early sits on a muted "done" tile while the others
  keep going.

### Quick Setup patterns

| Pattern | Shape |
|---|---|
| Standard Interval | Even work/recovery, repeated |
| Tabata | The classic fixed 20s work / 10s recovery |
| Pyramid / Reverse Pyramid | Work climbs then falls back down (or the reverse) in even steps |
| Ladder Up / Ladder Down | Work climbs, or falls, one step at a time with no way back down (or up) |
| Wave | Alternates between two work lengths rather than settling on a peak |
| Ascending / Descending Work | A straight ramp up or down, no symmetry |
| EMOM / E2MOM | Every 1 (or 2) minutes on the minute, for a target total time |
| Custom Sequence | A single block, ready to be reshaped in the Builder |

Every pattern hands back ordinary, fully-editable blocks - never a locked
template - so "Open In Builder" after Quick Setup is always an option, not just
for Custom Sequence.

### The Program Builder

A single screen, numbered to match its own instructions ("1. Pick a station...
2. Add blocks... 3. Edit its details..."), with three panes:

- **Program Outline** (left) - the program root, then either the Shared
  Timeline, or one entry per station plus a Between Stations entry once there
  are two or more. `+Station` / `Duplicate` / `Delete` / `Up` / `Down` manage
  the list; `+Rest` / `+Move` add a block that plays between every pair of
  stations.
- **Selected Timeline** (middle) - the blocks and repeat groups for whichever
  outline entry is selected. `+Work` / `+Recovery` / `+Rest` / `+Move` add a
  block; `+Repeat Group` wraps new blocks so they repeat together (e.g. 3x
  Work/Recovery); `+Pattern...` inserts an entire generated pattern at this
  point, same generators as Quick Setup. Double-click a repeat group to drill
  into its own blocks, and use the `< Back` row to return. `Duplicate` /
  `Delete` / `Up` / `Down` manage whatever is selected.
- **Properties** (right) - edit whatever is selected in either pane: a block's
  Type, Seconds, Label (overrides the default word), Announcement (a smaller
  line shown alongside it), Colour and Sound; a repeat group's Count; a
  station's Name, Colour, Work/Rest instructions, whether it shows in the
  on-screen panel at all (independent of the panel's own show/hide setting),
  and where its timing comes from (the program default, linked to another
  station, or its own custom timeline); or the program's own Name and
  execution Mode.

Block types, with their default word and colour (a block's own Label/Colour
override these): **Work** (green), **Recovery** (blue), **Rest** (amber),
**Move** (teal), **Prepare** (blue), **Countdown** (purple), **Water Break**
(cyan), **Instruction** (slate), **Custom** (magenta, always needs its own
Label).

### The Script window

Click **Script...** on the Builder to open the same program as plain text, in
its own window - handy for a quick edit, or for anyone who would rather type
it than click through the panes:

```
PROGRAM "Battle Ropes Circuit"
MODE SEQUENTIAL

BETWEEN
    MOVE 15
END BETWEEN

STATION "Battle Ropes"
    REPEAT 3
        WORK 50
        RECOVERY 10
    END
END STATION

STATION "Rower"
    WORK 45
END STATION
```

Commands: `PROGRAM "name"`, `MODE SHARED` / `SEQUENTIAL` / `PARALLEL` (only
needed to force a mode the program wouldn't otherwise imply - e.g. `SHARED`
with an informational station list, since a bare `STATION` line otherwise
means Sequential), `STATION "name"` / `END STATION` (`PANEL HIDE` inside one
keeps that station off the on-screen panel), `BETWEEN` / `END BETWEEN` for the
block(s) that play between every pair of stations (written once, not once per
gap), `REPEAT n` / `END` for a repeat group (one level deep), and a block per
line - `WORK` `RECOVERY` `REST` `MOVE` `PREPARE` `COUNTDOWN` `WATER`
`INSTRUCTION` `CUSTOM`, each followed by a duration (`20`, `20s`, `1m`, `1m
30s`) and an optional quoted label. An unrecognised or malformed line is
skipped rather than failing the whole program, so a typo degrades gracefully.
**Apply** loads the text into the Builder; closing the Script window without
clicking Apply discards whatever was typed rather than silently applying it.

### Import, export, and reuse

**Import Program...** / **Export Program...** on the Program tab save or load a
program as its own `.program.txt` file, using the exact same script grammar -
so a program built once can be reused on another computer, kept as a backup, or
handed to another teacher. In the settings file itself, the current program is
stored in `program=`, with line breaks written as `\n`.

### The simple-session checkboxes

The **On the main screen** and **Move between stations** groups on the Program
tab only affect a *simple* session - one with no program built yet:

- **Show a stations table on screen** / **Show the station name as the
  description instead of WORK/REST/MOVE** / **Highlight the active station** -
  the same rotating-station display this app has always had for a plain
  `stations=` list, turning one station name per round on the wall.
- **Move between stations** - an optional extra phase after the rest (or
  straight after work, if there is none), with its own length, message and
  colour, skipped after the final round.

Once a real program is built, its own stations and execution mode take over
completely: **Sequential Stations** highlights whichever station is actually
running, and **Shared Timing** never highlights one, since every station plays
at once. Either way, `Ctrl`+`1`–`9` jumps the clock straight to a given
station's work phase, for a teacher correcting a mistake mid-session.

See [`settings-examples/`](settings-examples) for a plain legacy example and a
ready-made `.program.txt` circuit to import, side by side.

## Repository layout

```
GymClock.sln                       Visual Studio 2022 solution
src/GymClock/
  Program.cs                       Entry point, licence gate, high-DPI setup
  MainForm.cs                      Timing engine + all custom painting
  SettingsForm(.Designer).cs       Settings dialog: timing/wording + the Program tab
  TimerSettings.cs                 Settings model, presets, text-file persistence
  WorkoutProgram.cs                Program model: Block, Timeline, RepeatGroup, StationDef
  ProgramScript.cs                 Reads/writes the plain-text program script grammar
  PatternGenerator.cs              Builds a Timeline from a pattern (Tabata, Pyramid, ...)
  QuickSetupForm(.Designer).cs     Name + mode + pattern + numbers -> a program
  ProgramBuilderForm(.Designer).cs Full editor: outline / timeline / properties / script
  PatternPickerForm.cs             "+Pattern..." dialog inside the Program Builder
  Beeper.cs                        Generates PCM WAV cues in memory
  IntervalPlan.cs                  Legacy variable-plan structure (blocks of rounds)
  DiagnosticReport.cs              Support report the user can save and send
  Licensing.cs                     Expiry date, signed key verification
  LicenceDialog.cs                 Activation prompt, doubles as the About box
  GymClock.ico
  Properties/PublishProfiles/      win-x64 single-file publish profile
settings-examples/                 Drop-in gymclock.settings.txt examples
distribution/READ-ME-FIRST.txt     Bundled with the exe for recipients
.github/workflows/build.yml        Builds, bundles exe + licence, publishes tags
```

No NuGet packages - the whole thing is plain C# and WinForms, so there is nothing
to merge-conflict on beyond the usual `.Designer.cs` layout code.

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
