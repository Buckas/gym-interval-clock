# Version 5 build guide

**Read this first.** This is the step-by-step guide for building version 5 (the modern interface). It is written so that a coding assistant can follow it one task at a time without having to make design decisions.

- **The plan** ([version-5-modern-ui.md](version-5-modern-ui.md)) says what version 5 is and why.
- **The mockups** ([v5-mockups/](v5-mockups/)) show what each screen looks like.
- **This guide** says exactly what to do, in order.

Where the three disagree, this guide wins, then the mockups, then the plan. Section 8 lists where this guide deliberately differs from the plan.

**Status (17 Nov 2026):** Phase 0 done: go with WPF on .NET 10. Phase 1 tasks P1.1 to P1.8 done; **next: the Phase 1 gate** (Chris runs a normal lesson and a two-PC link test), then P2.1. Approved by Chris: the nine mockup screens. The branch `version-5` was made from `version-4`, not `main`, because Chris asked to start before 4.0.0 was released; when 4.0.0 ships, merge `main` into `version-5`.

---

## 1. Rules for whoever does the work

Follow these on every task.

1. **One task at a time, in order.** Each task in section 6 has an ID (for example `P1.3`). Do not start a task until the one before it meets its "Done when".
2. **Build and test after every task.** Run the two commands in 2.2. All tests must pass before committing. Never delete, skip or weaken a test to make it pass.
3. **One commit per task.** Message format: `v5 P1.3: extract SessionEngine`. Push after each commit.
4. **Move code, do not rewrite it.** Most of Phase 1 is moving existing methods into new classes. Copy the method body unchanged, then make only the substitutions the task lists. Do not "improve" logic while moving it.
5. **Do not change behaviour.** Timing, modes, program scripts, the AI assistant's answers, the link protocol, licensing and file formats must work exactly as before. If a task seems to need a behaviour change, stop (rule 8).
6. **The mockups are the design.** Use the exact colours and sizes from section 3 and from the mockup source files. Do not invent a different look.
7. **Parity is the test.** The checklist in the plan's section 6 lists every key and action. A phase is not done while any row it covers is broken.
8. **Stop and ask Chris when:**
   - a "Done when" cannot be met after two honest attempts;
   - a test fails and the cause is not understood;
   - a task would change behaviour, a file format or the link protocol;
   - something in section 9 (open decisions) blocks the task;
   - a gate is reached (marked **GATE** in section 6).
9. **Record what happened.** After each phase, fill in its row in section 10.

## 2. Project facts

### 2.1 Layout today

```
GymClock.sln
src/GymClock/            the app (Windows Forms, .NET 8)
  MainForm.cs            the clock: engine + drawing + keys (2,218 lines)
  MainForm.Link.cs       linked displays inside MainForm (600 lines)
  AI/  Link/  Forms/     assistant logic, link code, some windows
src/GymClock.LinkTest/   separate console tool; do not touch
tests/GymClock.Tests/    xUnit, 274 tests
data/exercises.json      embedded in the app as GymClock.exercises.json
docs/plans/              the plans, this guide, the mockups
```

### 2.2 Commands

Run from the repository root.

```powershell
dotnet build GymClock.sln -v q -nologo
dotnet test tests/GymClock.Tests -v q -nologo
```

Publish a test build (N = the preview number):

```powershell
dotnet publish src/GymClock/GymClock.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded --output "$env:USERPROFILE\Downloads\GymClock-v5-preview-N"
```

### 2.3 Conventions

- C# with `Nullable` and `ImplicitUsings` disabled. Explicit types, no `var` in new code where the type is not obvious. Match the style of the file being edited.
- Comments explain why, in plain English. Public classes get a `<summary>`.
- Australian spelling in user-facing text: colour, licence (noun), programme is not used ("program").
- User-facing text is plain. No internal names on screen.
- Namespaces stay `GymClock`, `GymClock.AI`, `GymClock.Link`. New WPF code uses `GymClock.App.*`.

### 2.4 Pitfalls already met in this project

- **Radio buttons fire events while being set.** Setting several `Checked` values in a row fired a "mode changed" handler with nothing ticked, which silently changed a program's mode. In any toolkit: when code sets a selection, suppress the change handler with a flag.
- **Always-on-top.** The clock is normally topmost. Any window opened from it must also be topmost, or it opens behind the clock. Any external window (Windows Settings) needs topmost dropped first.
- **`&` in labels.** In Windows Forms a single `&` is a shortcut marker and vanishes. In WPF `_` is the marker for `Label`/`Button` content. Text such as "Bluetooth & devices" must display correctly.
- **Text on a second screen.** A second screen must never show "Press SPACE to start". Check `IsFollowing` wherever a hint mentions a control.
- **Editing files with scripts.** Backslash sequences such as `\b`, `\v` and `\p` in script strings have corrupted files twice. Prefer the editor's own edit tool over a script. After any scripted edit, check the result.
- **Line endings.** The repository has mixed LF and CRLF. Keep each file's existing line endings.
- **Tests and real hardware.** Tests must never touch a real Bluetooth radio. Follow the existing patterns: `LinkController.BluetoothEnabled = false`, test ports 47415/47416.
- **Tests must never write to the real `%AppData%\GymClock`.** This went wrong once: the app saves its settings as part of normal work (for example when a second screen connects, to remember the route), so the link tests overwrote the settings and current program of the PC they ran on. Now every file the app keeps is found through `AppFolders.Data` (`src/GymClock.Core/AppFolders.cs`), and `tests/GymClock.Tests/TestIsolation.cs` sends it to a temporary folder before any test runs. Two rules follow. In app code, never build a path from `Environment.SpecialFolder.ApplicationData`; use `AppFolders.Data`. In the tests, never remove `TestIsolation.cs` or call `AppFolders.UseFolderForTesting(null)`.
- **The GitHub build has its own file paths.** `.github/workflows/build.yml` reads `src/GymClock.Core/Licensing.cs` by path and publishes `src/GymClock/GymClock.csproj`. When a file it names moves, update the workflow in the same commit. Moving `Licensing.cs` in P1.1 without doing so failed five builds in a row.
- **The GitHub build runs the tests** (since 17 Nov 2026). After each push, check the run passed; a failure emails Chris.
- **Windows tests need an STA thread.** Any test that creates a window runs its body on a thread with `SetApartmentState(ApartmentState.STA)`. Copy `RunSta` from `ProgramEditorModeTests`.

## 3. Design reference

### 3.1 Mockups

| # | Screen | Image | Source (exact sizes and colours) |
|---|---|---|---|
| 1 | Display, Standard layout | `v5-mockups/01-display-standard.png` | `v5-mockups/source/Main.dc.html` |
| 2 | Display, Parallel tiles + control bar | `02-display-parallel.png` | `DisplayParallel.dc.html` |
| 3 | One screen: Control as a side panel | `03-display-side-panel.png` | `DisplayPanel.dc.html` |
| 4 | Control, Session | `04-control-session.png` | `Session.dc.html` |
| 5 | Control, Programs | `05-control-programs.png` | `Programs.dc.html` |
| 6 | Control, Builder | `06-control-builder.png` | `Builder.dc.html` |
| 7 | Control, Assistant | `07-control-assistant.png` | `Assistant.dc.html` |
| 8 | Control, Link displays | `08-control-link.png` | `Link.dc.html` |
| 9 | Control, Settings | `09-control-settings.png` | `Settings.dc.html` |

The source files are HTML with every size and colour written inline. Open one in a browser to see it, or read the `style="..."` values to get exact numbers. They are a picture of the target, not code to reuse.

Not drawn: the Exercises and About pages, the light theme, and the Big number and Stations layouts at full size. Section 7 specifies them in words, using the same tokens.

### 3.2 Colour tokens (dark theme)

Put these in `Themes/Tokens.Dark.xaml` with exactly these keys.

| Key | Value | Used for |
|---|---|---|
| `Surface.App` | `#12151A` | Control window background; Display idle and Parallel background |
| `Surface.Rail` | `#0D1014` | Navigation rail; segmented control track |
| `Surface.Card` | `#1B2027` | Cards |
| `Surface.Raised` | `#232A33` | Buttons and rows on a card |
| `Surface.Selected` | `#1C2733` | Selected nav item, selected card |
| `Surface.Input` | `#12151A` | Text boxes inside a card |
| `Border.Card` | `#2A313B` | Card outline |
| `Border.Control` | `#3A4450` | Button and input outline |
| `Border.Dashed` | `#5B6775` | "Add" placeholders |
| `Text.Primary` | `#F1F3F6` | Main text |
| `Text.Secondary` | `#C9D1DB` | Supporting text on cards |
| `Text.Muted` | `#A7B0BC` | Labels, hints, unselected nav |
| `Accent` | `#5AA2FF` | Primary button, selection outline, focus |
| `Accent.On` | `#0B1220` | Text on an accent button |
| `Accent.Soft` / `Accent.SoftText` | `#1E2F4D` / `#A9CBFF` | Blue tag |
| `Success` | `#4CC38A` | Tick icons, progress on Session page |
| `Success.Soft` / `Success.SoftText` | `#1D3A2B` / `#7FE0AE` | Green tag ("RUNNING NOW") |
| `Warning.Soft` / `Warning.SoftText` | `#3A2A14` / `#F3C98B` | Amber tag |
| `Danger` | `#F27F77` | Errors (text on dark) |
| `Bubble.User` | `#24456F` | The teacher's chat bubble |

Light theme (`Tokens.Light.xaml`), same keys: `Surface.App #F5F6F8`, `Surface.Rail #E9EBEF`, `Surface.Card #FFFFFF`, `Surface.Raised #F0F2F5`, `Surface.Selected #E3EEFF`, `Surface.Input #FFFFFF`, `Border.Card #DDE1E7`, `Border.Control #C3C9D2`, `Border.Dashed #9AA3AF`, `Text.Primary #14181E`, `Text.Secondary #3A4350`, `Text.Muted #5B6573`, `Accent #1463D6`, `Accent.On #FFFFFF`, `Accent.Soft #E3EEFF`, `Accent.SoftText #0F4AA3`, `Success #1E8E5A`, `Success.Soft #DDF3E8`, `Success.SoftText #17683F`, `Warning.Soft #FBEBD3`, `Warning.SoftText #7A4A0B`, `Danger #B3261E`, `Bubble.User #D6E6FF`.

High contrast (`Tokens.HighContrast.xaml`): map every key to the matching `SystemColors` brush (`WindowBrush`, `WindowTextBrush`, `HighlightBrush`, `HighlightTextBrush`, `ControlBrush`, `GrayTextBrush`).

**Phase colours** on the Display come from `TimerSettings` as they do today (`WorkBg`, `RestBg`, and so on). Do not change the defaults in `TimerSettings`. The darker shade used for a progress-bar track and for the station panel is the phase colour blended 26% towards black: reuse the existing `Blend(colour, Color.Black, 0.26)`.

### 3.3 Type, spacing, shape

| Token | Value |
|---|---|
| Control font | `Segoe UI Variable Text, Segoe UI` |
| Display font | `Segoe UI Variable Display, Segoe UI` |
| `Text.Caption` | 12, SemiBold, upper case, letter spacing 0.06 em (section labels such as "NOW RUNNING") |
| `Text.Small` | 13 |
| `Text.Body` | 14 |
| `Text.BodyLarge` | 15 |
| `Text.Subtitle` | 16 Bold (card headings), 19–22 Bold (card titles) |
| `Text.Title` | 28 Bold (page title) |
| Spacing | multiples of 4: 4, 8, 12, 16, 20, 24, 28, 32 |
| Page padding | 28 top and bottom, 32 left and right |
| Gap between cards | 16 to 20 |
| Radius | 8 controls · 10–12 large buttons · 14–16 cards · 999 pills and tags · 20 Display tiles |
| Control height | 44 minimum; 40 for inputs inside the Builder inspector; 36 inside a segmented control; 56–64 for the Session transport buttons |
| Navigation rail | 232 wide, items 44 high, 12 padding, icon 18 + 12 gap |
| Focus | a 2-pixel `Accent` outline, 2 pixels outside the control, on keyboard focus only |

### 3.4 Display proportions (Standard layout)

Everything on the Display is sized as a fraction of the window height `h`, as `MainForm.OnPaint` does today. From mockup 1 (720 high):

| Element | Size | Position |
|---|---|---|
| Station panel | width 23.4% of the window width | left edge, full height, padding 0.033 h |
| Station card | radius 0.019 h; name 0.030 h Bold; notes 0.021 h | stacked, gap 0.017 h; the current station is white with dark text |
| Program name / session time | 0.033 h Bold / 0.022 h | top left of the main area |
| Time of day | 0.050 h Bold | top right |
| Station or description line | 0.042 h Bold, letter spacing 0.06 em | centred |
| Phase word (WORK) | 0.086 h ExtraBold | centred |
| Countdown | 0.367 h ExtraBold | centred |
| Tip (work or rest note) | 0.039 h SemiBold | centred, under the countdown |
| Round line | 0.033 h Bold | centred |
| Next-up line | 0.025 h | centred |
| Progress bar | height 0.0195 h, fully rounded | full width of the main area |
| Footer (licensee, link status, copyright) | 0.018 h SemiBold | three columns at the bottom |
| Main area padding | 0.033 h top, 0.056 h sides, 0.025 h bottom | |

Every text item is fitted to its box: if it is too wide at the size above, shrink it until it fits (binary search, as `FitFont` does today). Nothing is ever cut off.

Parallel layout (mockup 2): rotation header 0.042 h ExtraBold; tiles in a grid chosen by the existing `PaintParallelDashboard` column rule; tile radius 0.028 h, padding 0.028 h; tile name 0.036 h ExtraBold top left; phase word 0.047 h; note 0.026 h; countdown 0.183 h (0.153 h when it has a colon); bar 0.017 h.

## 4. Target structure

```
src/GymClock.Core/                 new, no windows
  Session/  SessionEngine.cs  ICuePlayer.cs  DisplayModel.cs  DisplayFrame.cs
            LinkController.cs  AppSession.cs
  (moved, unchanged)  WorkoutProgram.cs  ProgramScript.cs  ProgramLibrary.cs  TimerSettings.cs
            IntervalPlan.cs  PatternGenerator.cs  Licensing.cs  OnlineServices.cs
            UsageLog.cs  UsageAssessment.cs  AppVersion.cs  AI/*  Link/* (not the two forms)
src/GymClock/                      the app; keeps its name and folder
  App/        WpfHost.cs  ObservableObject.cs  RelayCommand.cs  IDialogService.cs
  Themes/     Tokens.xaml  Tokens.Dark.xaml  Tokens.Light.xaml  Tokens.HighContrast.xaml
              Controls.xaml  Icons.cs
  Display/    DisplayWindow.xaml  ClockSurface.cs  ControlBar.xaml
  Control/    ControlWindow.xaml  Pages/*  Dialogs/*
  ViewModels/ one per page
  Beeper.cs  BeeperCuePlayer.cs  AppIconProvider.cs  DiagnosticReport.cs
  (old Windows Forms windows, deleted phase by phase)
tests/GymClock.Tests/              references both projects
Directory.Build.props              version numbers, shared by every project
```

## 5. Contracts

Create these exactly. Names matter: later tasks and tests refer to them.

### 5.1 `ICuePlayer` and `Cue` (`GymClock.Core/Session/ICuePlayer.cs`)

```csharp
namespace GymClock
{
    public enum Cue { WorkStart, RestStart, MoveStart, PrepStart, Countdown, Pause, Finish }

    /// <summary>Plays a sound cue. The engine asks; the app decides how (and whether) it sounds.</summary>
    public interface ICuePlayer
    {
        void Play(Cue cue);
    }
}
```

In the app: `BeeperCuePlayer : ICuePlayer`, a `switch` that calls the matching `Beeper.CueXxx()`. In tests: a `RecordingCuePlayer` that adds each cue to a list.

### 5.2 `SessionEngine` (`GymClock.Core/Session/SessionEngine.cs`)

```csharp
namespace GymClock
{
    /// <summary>The timing engine: which phase and block the session is in, and the two clocks.
    /// No drawing, no windows, no sound of its own.</summary>
    public sealed class SessionEngine
    {
        public SessionEngine(TimerSettings settings, ICuePlayer cues);

        // ---- state (set only by the engine)
        public TimerSettings Settings { get; }
        public WorkoutProgram Program { get; }
        public IReadOnlyList<RuntimeBlock> Sequence { get; }
        public IReadOnlyList<List<ResolvedBlock>> ParallelStations { get; }
        public ParallelRotationPlan ParallelPlan { get; }      // null unless Parallel
        public int[] LegacyRound { get; }
        public Phase Phase { get; }
        public int Index { get; }
        public double PhaseLength { get; }
        public RunClock PhaseClock { get; }
        public RunClock SessionClock { get; }
        public bool InTimedPhase { get; }
        public bool IsPaused { get; }
        public bool IsParallelActive { get; }
        public bool UsingLegacyStationRotation { get; }

        // ---- commands
        public void ApplySettings(TimerSettings settings);   // RebuildProgram + the clamping in ApplySettingsLive
        public void Start();                                  // StartSession
        public void TogglePause();
        public void Advance();
        public void GoBack();
        public void Reset();                                  // ResetSession
        public void JumpToStation(int stationIndex);
        public void Tick();                                   // RunTick, without the redraw

        // ---- queries used by drawing and the link
        public RuntimeBlock CurrentBlock();
        public RuntimeBlock BlockAt(int index);
        public int NextIndex();
        public double RemainingSeconds();
        public List<StationPosition> ComputeParallelPositions();
        public Station LegacyStationAt(int index);
        public Station LegacyCurrentStation();

        // ---- for a second screen (was MainForm.SnapToLeader)
        public void SnapTo(Phase phase, int index, double phaseElapsedSeconds, double sessionElapsedSeconds, bool paused);

        /// <summary>Raised wherever MainForm used to call Invalidate().</summary>
        public event Action Changed;
        /// <summary>Raised wherever MainForm used to call ShowHint(seconds).</summary>
        public event Action<double> HintRequested;
    }
}
```

`Phase` moves from `MainForm.cs` to its own file in Core, unchanged.

**Where each piece comes from** (`MainForm.cs` on `version-4`):

| `MainForm` member | Becomes |
|---|---|
| fields `_phase`, `_phaseLength`, `_lastCueSecond`, `_phaseClock`, `_sessionClock`, `_program`, `_sequence`, `_index`, `_parallelStations`, `_legacyRound`, `_parallelPlan`, `_lastRotationKey` | private fields of `SessionEngine`, same names |
| `InTimedPhase`, `IsPaused`, `UsingLegacyStationRotation`, `IsParallelActive` | properties, same names |
| `RebuildProgram`, `CurrentBlock`, `BlockAt`, `NextIndex`, `LegacyStationAt`, `LegacyCurrentStation`, `BeginPrep`, `BeginBlock`, `ComputeParallelPositions`, `PlayParallelRotationCues`, `AllParallelStationsFinished`, `PlayCueFor`, `RemainingSeconds`, `Advance`, `GoBack`, `TogglePause`, `JumpToStation` | methods, same names, bodies unchanged except the substitutions below |
| `ResetSession`, `StartSession`, `FinishSession`, `RunTick` | `Reset`, `Start`, private `Finish`, `Tick` |
| the phase clamping inside `ApplySettingsLive` (the `if (_phase == Phase.Prep) … else if …` block) | the second half of `ApplySettings` |
| `MainForm.Link.cs` `SnapToLeader` | `SnapTo` (the cue it plays goes through `ICuePlayer`) |

**Substitutions while moving** (the only edits allowed):

| In the old code | Write instead |
|---|---|
| `Invalidate();` | `Changed?.Invoke();` |
| `ShowHint(n);` | `HintRequested?.Invoke(n);` |
| `Beeper.CueWorkStart();` (and the other six) | `_cues.Play(Cue.WorkStart);` (matching name) |
| `_settings` | `Settings` |
| `ParallelRotationText()` and other text methods | leave in `MainForm`; they move in P1.4 |

### 5.3 `DisplayModel` and `DisplayFrame`

`DisplayModel` is where every word and colour on the clock is decided. It takes the engine and reads the settings from it.

```csharp
public sealed class DisplayModel
{
    public DisplayModel(SessionEngine engine);
    public DisplayFrame BuildFrame(DateTime now, bool showHints, bool isSecondScreen, bool hasLeaderProgram);
    // plus every method listed below, public, with its current name
}
```

Methods to move from `MainForm.cs` into `DisplayModel`, bodies unchanged: `ParallelRotationText`, `ApplyPulse`, `TileNoteFor`, `BackgroundColour`, `CurrentTimeText`, `ConfigSummaryText`, `SharedTimelineSummary`, `SessionElapsedText`, `ActiveStation`, `ActiveStationIndexForDisplay`, `ShowStationNameAsBigLabel`, `SecondaryDescriptionText`, `PhaseLabelText`, `BigNumberText`, `RoundText`, `NextUpText`, `PhaseNameFor`, `HintText`, `FormatSeconds`, `FormatClock`, `Blend`, `ReadableInkFor`. Where they read an engine field (`_phase`, `_index`, `_program`…), read the matching engine property. `HintText` takes `isSecondScreen` and `hasLeaderProgram` as parameters in place of `IsFollowing` and `_hasLeaderProgram`.

`DisplayFrame` is a plain object holding one frame's worth of answers:

```csharp
public sealed class DisplayFrame
{
    public Color Background;            // System.Drawing.Color, already pulsed
    public bool IsParallel;
    public string ProgramName;          // ConfigSummaryText
    public string SessionElapsed;
    public string TimeOfDay;            // empty when the clock is hidden
    public string Secondary;            // SecondaryDescriptionText
    public string PhaseLabel;
    public string BigNumber;
    public string Tip;                  // work or rest note for the active station, may be empty
    public string RoundLine;
    public string NextUp;
    public double ProgressFraction;     // 0..1, remaining / length
    public bool ShowProgress;           // InTimedPhase
    public string RotationHeader;       // Parallel only
    public List<TileFrame> Tiles;       // Parallel only
    public List<StationRow> Stations;   // the station list; empty when not shown
    public string Hint;                 // empty when hints are hidden
    public string Licensee;
    public string Copyright;
    public string LicenceWarning;       // empty when licensed
    public string UpdateNotice;         // empty when none
}

public sealed class TileFrame
{
    public int StationIndex; public string Name; public string PhaseLabel; public string Note;
    public string Countdown; public double ProgressFraction; public Color Background; public bool Finished;
}

public sealed class StationRow
{
    public int Number; public string Name; public string WorkNote; public string RestNote;
    public Color Colour; public bool IsCurrent;
}
```

`System.Drawing.Color` is part of .NET itself and needs no Windows Forms reference, so Core keeps using it. The WPF side converts with one helper: `Color.FromArgb(c.A, c.R, c.G, c.B)`.

### 5.4 `LinkController` (`GymClock.Core/Session/LinkController.cs`)

Everything in `MainForm.Link.cs` except drawing and the two window calls.

```csharp
public sealed class LinkController : IDisposable
{
    public LinkController(AppSession session);
    public static int DataPort; public static int DiscoveryPort; public static bool BluetoothEnabled;  // were MainForm.Link* statics
    public bool IsFollowing { get; }
    public bool HasLeaderProgram { get; }
    public double DriftPeakMs { get; }
    public void Start();                                   // StartLink
    public void Stop(bool restoreOwnProgram);              // StopLink
    public void Tick();                                    // LinkTick
    public void ApplyChange(Action<TimerSettings> change); // ApplyLinkChange
    public LinkStatus GetStatus();                         // GetLinkStatus
    public string BadgeText(bool detail, out bool isWarning);   // LinkBadgeText, colour replaced by a flag
    public event Action<string, double> ToastRequested;    // wherever ShowToast was called
    public event Action Changed;
}
```

Link events arrive on background threads. `LinkController` hands each one to its `Post` property (an `Action<Action>`), replacing `MainForm.OnUi`. By default `Post` posts to the `SynchronizationContext` captured in the constructor, so create the `AppSession` on the UI thread. Tests set `Post` to add to a queue and run the queue themselves (see `LinkedPair` in `LinkNetworkTests`).

### 5.5 `AppSession` (`GymClock.Core/Session/AppSession.cs`)

The one object both surfaces share.

```csharp
public sealed class AppSession : IDisposable
{
    public AppSession(TimerSettings settings, ICuePlayer cues);
    public TimerSettings Settings { get; }          // what is showing (the main PC's, when following)
    public TimerSettings OwnSettings { get; }       // this PC's own, the one that is saved
    public SessionEngine Engine { get; }
    public DisplayModel Display { get; }
    public LinkController Link { get; }
    public void ReplaceSettings(TimerSettings settings, bool reset);   // after the editor's OK
    public void ShowLeaderSettings(TimerSettings leader);              // second screen: was OnLeaderProgram
    public void RestoreOwnSettings();
    public void Tick();                             // Engine.Tick(); Link.Tick();
    public void SaveOwnSettings();
}
```

### 5.6 MVVM helpers (`src/GymClock/App/`)

```csharp
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    protected void Raise([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object> _run; private readonly Func<object, bool> _can;
    public RelayCommand(Action<object> run, Func<object, bool> can = null) { _run = run; _can = can; }
    public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    public bool CanExecute(object p) { return _can == null || _can(p); }
    public void Execute(object p) { _run(p); }
}
```

No MVVM package is added.

### 5.7 Text fitting on the Display (reference code)

`ClockSurface` draws with `DrawingContext`. Use this to fit text to a box, the WPF equivalent of `FitFont`:

```csharp
private FormattedText Fit(string text, Typeface face, double maxSize, Size box, Brush brush)
{
    double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
    FormattedText Make(double size)
    {
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, dpi);
    }
    FormattedText best = Make(maxSize);
    if (best.WidthIncludingTrailingWhitespace <= box.Width && best.Height <= box.Height) return best;
    double lo = 7, hi = maxSize;
    while (hi - lo > 0.5)
    {
        double mid = (lo + hi) / 2;
        FormattedText t = Make(mid);
        if (t.WidthIncludingTrailingWhitespace <= box.Width && t.Height <= box.Height) { lo = mid; best = t; } else hi = mid;
    }
    return best;
}
```

Cache the result per (text, box size): the countdown changes once a second, so most frames reuse the last fit. Segoe UI's digits are all the same width, so the countdown does not shift as it changes.

## 6. Tasks

### Phase 0: spike (3 days)

Proves the technology before anything is rewritten. Nothing from this phase ships except the retarget.

| ID | Do | Done when |
|---|---|---|
| P0.1 | After 4.0.0 is released: `git checkout main`, `git pull`, `git checkout -b version-5`, push. Create `Directory.Build.props` at the root holding `<Version>5.0.0-preview.1</Version>`, `<AssemblyVersion>4.99.1.0</AssemblyVersion>`, `<FileVersion>4.99.1.0</FileVersion>`, and the `Product`, `Copyright`, `Authors` values now in `GymClock.csproj`; delete those from `GymClock.csproj`. Add `version-5` to both branch lists in `.github/workflows/build.yml`. | Build and tests pass. The window title says "Version 5.0 Preview 1". `AppVersionTests` pass. |
| P0.2 | Retarget: in `GymClock.csproj` and `GymClock.Tests.csproj` change `net8.0-windows` to `net10.0-windows`; in `GymClock.LinkTest.csproj` change `net8.0` to `net10.0`; in `build.yml` change `dotnet-version` to `10.0.x`. | Build has no errors. All 274 tests pass. The GitHub build passes. |
| P0.3 | Publish (2.2). Run the exe. Open the AI assistant and wait for the local AI status. Start a linked pair on one PC if possible. | Recorded in section 10: exe size, time from double-click to the clock appearing, whether the local AI became ready, whether Bluetooth and the link started. |
| P0.4 | Create `spike/WpfSpike` (a WPF app, `net10.0-windows`, not added to the solution). One full-screen window with a `ClockSurface : FrameworkElement` that redraws every 40 ms: a green background, "WORK", a countdown at 0.367 h using 5.7, and eight coloured tiles. Print the average `OnRender` time once a second to the debug output. Run it at 3840×2160 if a 4K screen is available, otherwise at the largest screen. | Recorded: average render time. Target under 8 ms. |
| P0.5 | In the spike, set `ThemeMode="System"` on the application (WPF's built-in Fluent theme) and add a Button, TextBox, CheckBox, ComboBox and Slider. Then try restyling the Button's corner radius and colours from a resource dictionary. | Recorded: does it build without warnings being errors, does it follow Windows light/dark, can a Button be restyled to match section 3. This decides P2.3. |
| P0.6 | Publish the spike as a single self-contained file. | Recorded: its size and start time. |
| **GATE** | Fill in section 10 for Phase 0 and report to Chris. Delete `spike/`. | Chris says go, or says use the fallback in the plan's 5.1. |

### Phase 1: split the core (6 days)

No visible change to the app. Every task ends with all tests passing.

| ID | Do | Done when |
|---|---|---|
| P1.1 | Create `src/GymClock.Core/GymClock.Core.csproj`: `Microsoft.NET.Sdk`, `net10.0-windows`, `RootNamespace` `GymClock`, `Nullable` and `ImplicitUsings` disabled, `UseWindowsForms` **true for now** (see note). Move into it, with `git mv`, every source file that does not use Windows Forms: all of `AI/` (24 files), `Link/BluetoothLink.cs`, `ClockSync.cs`, `LinkFollower.cs`, `LinkHelp.cs`, `LinkLeader.cs`, `LinkProtocol.cs`, `LinkStatus.cs`, `LinkTransport.cs`, `RunClock.cs`, and `IntervalPlan.cs`, `Licensing.cs`, `OnlineServices.cs`, `PatternGenerator.cs`, `ProgramLibrary.cs`, `ProgramScript.cs`, `TimerSettings.cs`, `UsageAssessment.cs`, `UsageLog.cs`, `WorkoutProgram.cs`. Also move `AppVersion.cs` (remove its `Application.ProductVersion` fallback and its `using System.Windows.Forms`) and `ColourUITypeEditor.cs`. Move the two `PackageReference`s and the `exercises.json` `EmbeddedResource` (same `LogicalName`) to the Core project. Delete the empty `SettingsForm.cs`. Add a `ProjectReference` from the app and from the tests to Core. Add Core to the solution under `src`. A few moved files mention `MainForm` in comments only (`LinkFollower.cs`, `LinkLeader.cs`, `RunClock.cs`, `WorkoutProgram.cs`); leave the comments for now. | Build and all tests pass. The app runs; the exercise library loads; the title shows the right version. |
| P1.2 | Add `ICuePlayer` and `Cue` (5.1) to Core. Add `BeeperCuePlayer` to the app. | Build passes. |
| P1.3 | Create `SessionEngine` (5.2). Move the fields and methods in the 5.2 table, applying only the listed substitutions. In `MainForm`: add `private readonly SessionEngine _engine;` created in the constructor with `new BeeperCuePlayer()`; subscribe `Changed` to `Invalidate` and `HintRequested` to `ShowHint`; replace every use of a moved member with the engine's (`_phase` → `_engine.Phase`, `Advance()` → `_engine.Advance()`, and so on). `Ticker_Tick` calls `_engine.Tick(); LinkTick(); Invalidate();`. In `MainForm.Link.cs`, `SnapToLeader` becomes a call to `_engine.SnapTo(...)`, and the places that set the clocks directly use `_engine.PhaseClock.Set(...)`. | Build and all tests pass (the link tests reach `MainForm` fields by reflection: update the field names they read to go through `_engine`, changing nothing else). Run a Circuit, a Sequential and a Parallel program by hand: start, pause, skip, back, reset behave as before. |
| P1.4 | Add `SessionEngineTests`: one test per row. (a) Circuit, 2 stations, `REPEAT 2 { WORK 20s REST 10s }`, 15 s move, prep 5: `Start` gives `Prep`; advancing through gives the block order W R W R M W R W R then `Finished`. (b) `TogglePause` stops both clocks and plays `Cue.Pause`. (c) `GoBack` after 3 s restarts the interval; within 2.5 s goes to the previous block. (d) `JumpToStation(1)` in Circuit lands on the first block of station 2. (e) Parallel: `Advance` does nothing and raises `HintRequested`; the session finishes when every station is finished. (f) Each block start plays the right cue (use `RecordingCuePlayer`). (g) `SnapTo` puts the engine at the given phase, index and elapsed time. Use `RunClock.Set` to move time; do not sleep. | The new tests pass. |
| P1.5 | Create `DisplayModel` (5.3) and move the listed methods. `MainForm.OnPaint` and its helpers call `_display.Xxx()`. | Build and all tests pass. The clock looks identical in every mode. |
| P1.6 | Add `DisplayFrame`, `TileFrame`, `StationRow` and `DisplayModel.BuildFrame`. Fill each field from the method that `OnPaint` uses for it today. Change `OnPaint`, `PaintParallelDashboard`, `DrawStationTile` and `DrawStationsTable` to read from one frame built at the top of `OnPaint`. Add `DisplayModelTests`: for a Circuit program at work second 11 of 45, round 2, station 1, assert `PhaseLabel`, `BigNumber` ("34"), `RoundLine`, `NextUp`, `ProgressFraction` (34/45), the four `Stations` rows and which is current; for Parallel, assert four `Tiles` with the right labels and countdowns; for a second screen at idle, assert the hint does not contain "SPACE". | Tests pass. The clock looks identical. |
| P1.7 | Create `AppSession` (5.5) and `LinkController` (5.4). Move the link code from `MainForm.Link.cs`. `MainForm` keeps only: creating the `AppSession`, drawing the badge from `Link.BadgeText`, showing toasts from `ToastRequested`, and opening the link window. `MainForm.LinkDataPort` and friends become `LinkController.DataPort` and friends. | Build and all tests pass (update the statics the tests set). |
| P1.8 | Rewrite `LinkNetworkTests`' two window tests to use two `AppSession`s and no windows: drive the main one with `Engine.Start()`, `Advance()` and so on; pump both with a loop that runs queued posts on a test `SynchronizationContext` and calls `Tick()`; assert on public `Engine` and `Link` properties. Remove every `GetField`/`GetMethod` on `MainForm` from the tests. (`ProgramEditorModeTests` may keep its reflection until Phase 5.) | `grep -rn "typeof(MainForm).Get" tests/` finds nothing. All tests pass. |
| **GATE** | Publish a preview. Chris runs a two-PC link test and a normal lesson. Decide whether to ship this as a 4.x release (plan decision 8). | Chris confirms no change in behaviour. |

Note on P1.1: `WorkoutProgram.cs` carries two `[Editor(typeof(ColourUITypeEditor), …)]` attributes for the old property grid, so Core needs Windows Forms until the property grid is deleted in P5.12. That task removes the attributes, deletes `ColourUITypeEditor.cs` and sets `UseWindowsForms` to false in Core.

### Phase 2: design system and shell (5 days)

| ID | Do | Done when |
|---|---|---|
| P2.1 | In `GymClock.csproj` add `<UseWPF>true</UseWPF>` beside `UseWindowsForms`. Add `App/WpfHost.cs` with `static void EnsureApplication()`: if `System.Windows.Application.Current` is null, create `new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }` and merge the theme dictionaries into its `Resources`. Call it at the start of `Program.Main`. Add a helper `ShowModeless(Window w)` that calls `ElementHost.EnableModelessKeyboardInterop(w)` then `w.Show()`. | Build passes. The app starts as before. |
| P2.2 | Add `Themes/Tokens.Dark.xaml`, `Tokens.Light.xaml`, `Tokens.HighContrast.xaml` (3.2) as `SolidColorBrush` resources, and `Tokens.xaml` (3.3) with font families, sizes, thicknesses and corner radii. Add `ThemeService`: picks Dark or Light from the Windows setting (registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, `AppsUseLightTheme`), switches to HighContrast when `SystemParameters.HighContrast` is true, honours the `theme` setting (`system`, `light`, `dark`) and swaps the merged dictionary live. Add `theme` to `TimerSettings` (field, `Clone`, parse, `ToText`), default `system`. | A test asserts each token file defines every key in 3.2. Changing the Windows theme changes the app. |
| P2.3 | The spike showed WPF's built-in Fluent theme works on .NET 10 and its controls can be restyled with `BasedOn`. So: `ThemeService` also sets `Application.ThemeMode` to `Dark` or `Light` (it is still marked experimental: add `<NoWarn>$(NoWarn);WPF0001</NoWarn>` to the app project with a comment). Standard controls (TextBox, ComboBox, CheckBox, Slider, ScrollBar, ToolTip) use the built-in look with our colours overridden where they clash; the app's own pieces below are styled from our tokens. Add `Themes/Controls.xaml` with styles (keys in brackets): Button `[Button.Primary]`, `[Button.Standard]`, `[Button.Subtle]`, `[Button.Danger]`; `TextBox`; `[NumberBox]` (a TextBox that accepts digits only, with up and down arrows); `ComboBox`; `CheckBox`; `[ToggleSwitch]` (a styled CheckBox); `Slider`; `ListBox` and `ListBoxItem`; `[Card]` (a Border); `[Tag.Success]`, `[Tag.Accent]`, `[Tag.Warning]`, `[Tag.Neutral]`; `[SegmentedControl]` (a ListBox of radio-like items); `[NavItem]`; `[InfoBar]`; `ToolTip`; `ScrollBar`. Sizes and colours from section 3 and the mockup sources. Every style has a keyboard focus visual (3.3) and uses `DynamicResource` for colours so themes switch live. Base them on the built-in Fluent theme only if P0.5 said that works. | Each style appears in the gallery (P2.6) and matches the mockups side by side. |
| P2.4 | Add `ObservableObject` and `RelayCommand` (5.6) and `Themes/Icons.cs`: constants for the glyphs used (play, pause, skip, back, reset, programs, builder, assistant, exercises, link, settings, about, sound, full screen, more, close, tick, drag handle) from Segoe Fluent Icons, with the Segoe MDL2 Assets fallback, and one `IconFontFamily` resource = `Segoe Fluent Icons, Segoe MDL2 Assets`. | Icons show on Windows 11. |
| P2.5 | Add `Control/ControlWindow.xaml`: a 232-wide navigation rail (title "Gym Interval Clock", eight `[NavItem]`s: Session, Programs, Builder, Assistant, Exercises, Link displays, Settings, About) and a page host (`ContentControl`). `ControlViewModel` has `CurrentPage` and one command per page. Minimum size 900×600; default 1280×800; remembers its size and position in settings (`controlbounds`). Add `ControlHost`: `Open(pageId)` shows the window on a monitor other than the clock's when there are two (compare with `Screen.FromControl(MainForm)`), otherwise as a side panel: a borderless topmost window docked to the right edge of the clock's screen at 40% width, with the header from mockup 3 ("Full window" and close buttons). Esc closes the panel. | The window opens with empty pages. With one monitor it docks right; with two it opens on the other. |
| P2.6 | Add a `GalleryPage` (reached with Ctrl+Shift+G from the Control window) showing every style from P2.3 in each state: normal, hover, pressed, focused, disabled. | Looks right at 100, 125, 150 and 200% Windows text size, in dark, light and high contrast. |
| P2.7 | In `MainForm.OnKeyDown` add `C`: `ControlHost.Open("session")`. | `C` opens the Control window. |
| **GATE** | Send Chris and James screenshots of the gallery in dark and light. | They approve the look. |

### Phase 3: the Display (6 days)

| ID | Do | Done when |
|---|---|---|
| P3.1 | Add `Display/ClockSurface.cs : FrameworkElement` with a `DisplayFrame Frame` property and `OnRender`. Port `MainForm.OnPaint`'s Standard (non-Parallel) branch: same order, same rectangles (fractions of height), sizes from 3.4, text through `Fit` (5.7). Station panel as rounded cards (3.4). Rounded progress bar. Footer in three columns. | A snapshot test (P3.8) of mockup 1's state matches the approved image. |
| P3.2 | Port `PaintParallelDashboard` and `DrawStationTile`: same grid rule, rounded tiles (3.4), the station-range filter (`LinkController` already knows which stations a second screen shows). | Snapshot tests for 2, 4 and 8 tiles. |
| P3.3 | Port the rest of `OnPaint`: licence warning, update notice, hint bar, link badge, toast (a rounded card, centred, same text). | Snapshot tests for idle with hints, paused, finished, and a toast. |
| P3.4 | Add `Display/DisplayWindow.xaml`: borderless, black background, hosts `ClockSurface`. A `DispatcherTimer` at 40 ms calls `AppSession.Tick()`, builds a frame and sets it. Port from `MainForm`: full screen (`SetFullScreen`), `MoveToNextScreen` (F6), always on top, the windowed size, `OnKeyDown` exactly as the table in the plan's 6.1 (including the second-screen blocking and its toast), left click = pause, and `S`/`F2`, `A`, `L`, `F3` opening the Control window at Builder, Assistant, Link displays, About (until those pages exist they open the old dialogs, modal, as now). | Every row of the plan's 6.1 works. |
| P3.5 | Make WPF the entry point: add `App.xaml` with `Startup` doing what `Program.Main` did (licence check, `ProgramLibrary.EnsureInstalled`, create `AppSession`, show `DisplayWindow`, then the work of `MainForm.OnShown`: start the link, AI warm-up, background checks, renewal offer). Call `System.Windows.Forms.Application.EnableVisualStyles()` and `SetHighDpiMode(PerMonitorV2)` first so the remaining old dialogs look right. Delete `Program.cs`, `MainForm.cs`, `MainForm.Link.cs`. | The app starts on the new Display. Old dialogs still open and work. All tests pass. |
| P3.6 | Add `Display/ControlBar.xaml` (mockup 2): shown for 4 s on mouse move or touch, never on a second screen, hidden when `controlbar=false`. Buttons: Back, Pause/Start (primary), Skip, Reset, Sound, Link, Controls, Full screen, bound to the same actions as the keys. In Parallel, Skip is disabled. | Each button does what its key does. |
| P3.7 | Add settings `displaylayout` (`standard`, `bignumber`, `stations`), `safemargin` (0–8, percent), `reducemotion` (`system`, `on`, `off`), `controlbar` (bool) to `TimerSettings`. Big number: no station panel, no round or next-up lines; phase word 0.11 h; countdown 0.55 h. Stations: station panel 50% wide with names at 0.045 h and notes at 0.030 h; countdown 0.28 h. Safe margin shrinks the drawing area on all four sides by that percentage of the smaller dimension. These four are this PC's own and are not sent to second screens: exclude them in `ToText(false)` the way link settings are. | Snapshot tests for both layouts and a 5% margin. A link test shows the second screen keeps its own layout. |
| P3.8 | Snapshot tests. Helper: build a `DisplayFrame` by hand, set it on a `ClockSurface` of 1280×720, `Measure`, `Arrange`, render with `RenderTargetBitmap` at 96 DPI, compare with `tests/GymClock.Tests/Snapshots/<name>.png`: pass if fewer than 0.5% of pixels differ by more than 24 in any channel. If the approved file is missing, save the new image as `<name>.new.png` and fail with a message to review and rename it. Chris approves the first set. | About 30 snapshots: each mode at idle, prep, work, rest, move, paused, finished; Parallel with 2, 4, 8 tiles; both extra layouts; second-screen idle; a toast. |
| P3.9 | Motion: when the background colour changes, cross-fade over 200 ms (interpolate in `OnRender` from frame time). In the last 3 s of a block, scale the countdown 1.00 → 1.04 → 1.00 once a second, replacing the brightness pulse. Skip both when reduce motion is on or `SystemParameters.ClientAreaAnimation` is false. | Visible on screen; off when reduce motion is on. Snapshot tests set reduce motion on. |
| **GATE** | Preview build. Chris runs a lesson and a two-PC link (a version 4 second screen with the version 5 main PC, and the reverse). | Section 6.1 of the plan signed off. |

### Phase 4: Session, Settings, Link, About (5 days)

For each page: a view model in `ViewModels/`, a page in `Control/Pages/`, tests for the view model. Section 7 has the per-page detail.

| ID | Do | Done when |
|---|---|---|
| P4.1 | `App/IDialogService.cs` with `Confirm(title, message, yesText, noText)`, `Info(title, message)`, `Prompt(title, label, initial)`, `PickOpenFile(filter)`, `PickSaveFile(filter, name)`; a WPF implementation showing in-window dialogs over the Control window (file pickers use the standard Windows dialogs); a fake for tests. `[InfoBar]` host at the top of the page area with `ShowInfo`, `ShowWarning`, `ShowError`. | Used by every later page; no `MessageBox.Show` in new code. |
| P4.2 | Session page (7.1). | Session rows of the plan's 6.2 pass from the page. |
| P4.3 | Settings page (7.2). Delete `GeneralSettingsForm`. | Settings rows of 6.2 pass. |
| P4.4 | Link displays page (7.3). Delete `LinkSetupForm` and `LinkHelpForm`. | Link rows of 6.2 pass; the existing link tests pass. |
| P4.5 | About page and the blocking licence window (7.4). Delete `LicenceDialog` and `LicenceRequestForm`. | Licence rows of 6.2 pass. |

### Phase 5: Programs and Builder (10 days)

| ID | Do | Done when |
|---|---|---|
| P5.1 | Programs page (7.5). | Run, edit, duplicate, export, delete, import work. |
| P5.2 | `BuilderViewModel` (7.6): wraps a working copy of a `WorkoutProgram`. Port the operations from `ProgramEditorForm` handlers one for one (the list is in 7.6). Tests for each operation. | View model tests pass. |
| P5.3 | Builder page: header, mode cards, outline (7.6). | Mode changes follow the ported rules; a test loads a Circuit program and checks the mode stays Circuit. |
| P5.4 | Timeline cards, repeat groups, add menu, patterns. | Each Everest sample can be rebuilt from scratch and its script matches the sample's. |
| P5.5 | Inspector (station, block, program) and colour swatches. | Every editable property in the old property grid has a field. |
| P5.6 | Script tab with two-way sync, ported from `RefreshScriptFromProgram` and `ScriptSyncTimer_Tick`. | Typing a valid script updates the Build tab; editing in Build updates the script. |
| P5.7 | Preview, Save, Discard, "Undo AI change", unsaved-changes prompt when leaving the page. | Saving updates the running session as the old OK did. |
| P5.8 | Drag to reorder (stations and blocks), with Alt+Up / Alt+Down as the keyboard way. | Works with mouse and keyboard. |
| P5.9 | "New program" flow, replacing Quick Setup (7.5). | A program made with each pattern matches the one Quick Setup made. |
| P5.10 | Simple session editor (7.5). | Simple session rows of 6.2 pass. |
| P5.11 | Optional: undo and redo (plan decision 6). Keep a stack of script snapshots (up to 50); Ctrl+Z, Ctrl+Y. | Skip unless Chris says yes. |
| P5.12 | Delete `ProgramEditorForm`, `QuickSetupForm`, `PatternPickerForm`, `ColourUITypeEditor`; remove the two `[Editor]` attributes from `WorkoutProgram.cs`; set `UseWindowsForms` to false in Core; delete `ProgramEditorModeTests` (its case is now a view model test). | Build and tests pass. |

### Phase 6: Assistant and Exercises (6 days)

| ID | Do | Done when |
|---|---|---|
| P6.1 | `ProgramAssistantForm` holds logic that is not interface: `TryHandleGuidedBuilderAsync`, `TryHandleStationTipsAsync`, `GenerateAsync`, the clarification stages, `TryAutoRepairAsync`, `SuggestStationNamesAsync`, `SuggestStationCueAsync`, `GetContextScriptForGeneration`, the setup state handling. **First**, while the old form still exists, move that logic into `AI/AssistantController.cs` in Core: methods `SendAsync(string userInput)`, `NewChat()`, `LoadProgram(script)`, `Validate()`, `Cancel()`; state `Messages`, `ProposedScript`, `ValidationState`, `Issues`, `IsBusy`, `SetupStatus`; events for changes. The form calls the controller. Tests: replay the chats in `GuidedProgramBuilderTests` and `StationTipsTests` through the controller with a fake AI service. | The old form behaves as before, driven by the controller. Tests pass. |
| P6.2 | Assistant page (7.7) bound to an `AssistantViewModel` over the controller. Delete `ProgramAssistantForm`. | Assistant rows of 6.2 pass. |
| P6.3 | Exercises page (7.8). Delete `ExercisesForm`. | Exercises rows of 6.2 pass. |

### Phase 7: finish (5 days)

| ID | Do | Done when |
|---|---|---|
| P7.1 | Remove `<UseWindowsForms>` from `GymClock.csproj`. Replace anything left (`Screen`, `AppIconProvider`, `DiagnosticReport` dialogs) with WPF or Win32 equivalents. | `grep -rn "System.Windows.Forms" src/` finds nothing. |
| P7.2 | Accessibility: every control has an `AutomationProperties.Name`; Tab order follows reading order; every action reachable by keyboard; text contrast at least 4.5:1 in dark and light; high contrast usable. | A keyboard-only walk through every page; Narrator reads each control. |
| P7.3 | "What's new" page shown once on first run of version 5 (setting `seenwhatsnew=5`): the two surfaces, the Control window, the unchanged keys. | Shows once. |
| P7.4 | Update `README.md` and `distribution/READ-ME-FIRST.txt`. | Reviewed by Chris. |
| P7.5 | Scaling and soak: 100/125/150/200% text size; 1024×768, 1280×720, 1920×1080, 3840×2160; a 40-minute session watching memory in Task Manager. | Recorded in section 10. |
| **GATE** | The plan's full section 6 checklist on two PCs. Then release as the plan's 9.1 says. | Chris signs off. |

## 7. Page specifications

Each page is a `UserControl` in `Control/Pages/` with a view model in `ViewModels/`. Layout follows the mockup; this section lists what each control is bound to.

### 7.1 Session (mockup 4)

View model `SessionViewModel(AppSession)`, refreshed from `Engine.Changed` and a 250 ms timer.

| Control | Bound to |
|---|---|
| "Display is on" pill | the Display's screen name and resolution |
| Now running: name, description | `Display.ConfigSummaryText()`, mode label + station count + total length |
| Phase tag, countdown, station line, round line, progress | the same `DisplayFrame` fields the clock uses; the tag's colour is the frame's background |
| Back, Pause/Start, Skip, Reset | `Engine.GoBack`, `TogglePause` (label "Start" when idle or finished, "Resume" when paused), `Advance`, `Reset` |
| Jump to station chips | one per station, `Engine.JumpToStation(i)`; hidden in Parallel and Shared |
| Up next | the next three blocks from `Engine.BlockAt(Index + 1…3)`: name and length |
| Sound toggle, volume | `OwnSettings.SoundEnabled`, `Volume`; saved at once |
| Move to next screen, Full screen | the Display window's actions |
| Link line | `Link.BadgeText(true, …)` |

On a second screen the four transport buttons and the chips are disabled, with the text "The main PC has the controls".

### 7.2 Settings (mockup 9)

`SettingsViewModel`. Every change is written to `OwnSettings`, saved, and applied with `AppSession.ReplaceSettings(settings, reset: false)`.

- **Display layout:** three cards (`displaylayout`), safe margin slider (`safemargin`), "Show the control bar when the mouse moves" (`controlbar`), "Reduce motion" (`reducemotion`: unticked = `system`).
- **Phase colours:** one button per colour in `TimerSettings` (Work, Rest, Recovery, Move, Get ready, Countdown, Water break, Instruction, Custom, Paused, Finished, Idle). Clicking opens a swatch dialog: 24 preset colours and a hex box. Writes the `#RRGGBB` string.
- **Appearance:** theme segmented control (`theme`).
- **Sound:** play cues, volume, get-ready countdown (`PrepSeconds`, 0–300).
- **Clock and stations:** show time of day (`ShowClock`), time format (`ClockFormat`: offer `h:mm tt`, `H:mm`, `h:mm:ss tt`, and "Other…" for a typed format), keep the Display on top (`AlwaysOnTop`), show the station list (`ShowStationsTable`), show the station name instead of WORK / REST (`ShowStationNameAsDescription`).

### 7.3 Link displays (mockup 8)

`LinkViewModel` is a direct port of `LinkSetupForm`'s logic. Keep its three states (choose, main PC, second screen), its 400 ms refresh, its texts word for word, the four-digit auto-apply, "More options", and the stuck-for-25-seconds tip. The right-hand column shows the six `LinkHelp` topics; choosing one replaces the status column with that topic's text and a "Back to status" button. The Bluetooth topic keeps its two buttons that open the Windows screens.

Keep `LinkHelp.cs` unchanged except: where it says "press L", keep it (L still opens this page).

### 7.4 About, and the licence window

`AboutViewModel` ports `LicenceDialog` and `LicenceRequestForm`.

- A header card: product name, `AppVersion.Full`, copyright.
- A status card: the text and colour `LicenceDialog.UpdateStatusText` produces for each `LicenceState`.
- Computer ID with a Copy button.
- "Enter a licence key": a text box, "Activate", "From a file…", "Check online".
- "Request a licence": the fields of `LicenceRequestForm`, in place.
- "Save report…", a link to the site, and the update notice when `UpdateInfo.IsNewerThanThis`.

When the app may not run (`!status.CanRun`) the same view is shown in a small stand-alone `LicenceWindow` before anything else, with "Exit" in place of "Close" and Esc disabled, as the blocking dialog does now.

### 7.5 Programs (mockup 5)

`ProgramsViewModel`.

- **Your programs:** the current program (tagged "RUNNING NOW" when it is the active source), the Simple session card, and every `*.program.txt` in `ProgramLibrary.RootFolder` that is not in the Samples folder. See open decision D1 before building this list.
- **Samples:** the files in `ProgramLibrary.SamplesFolder`.
- A card shows: mode in plain words (the four phrases used in the mode question), station count, total length (`BuildRuntimeSequence` or `GetParallelRotationPlan`), station names.
- **Run:** make it the current program (`UseBuiltProgram = true`, `ProgramScript` = the file), reset the session, close the panel if docked.
- **Edit:** open it in the Builder. **Copy and edit** on a sample: copy it into the programs folder first.
- **More:** Duplicate, Export… (save dialog, same filter as today), Delete (confirm).
- **Import…:** open dialog, same checks as `ProgramEditorForm`'s import.
- **Search:** filters cards by name or station name.
- **New program:** a three-step flow replacing Quick Setup: name and mode; pattern and numbers (the fields `QuickSetupForm` has, with its preview); stations. It builds the program with `PatternGenerator` exactly as Quick Setup does and opens it in the Builder. A second button, "Ask the assistant", opens the Assistant page with "Help me create a new program" sent.
- **Simple session → Change times:** work, rest, rounds (0 = continuous), work and rest wording, rest after the final round, variable plan, move time and message: the fields of the old editor's "Simple session" tab.

### 7.6 Builder (mockup 6)

`BuilderViewModel` holds a working copy of a `WorkoutProgram`, a `Selection` (program, shared timing, move, station *n*, block), `IsDirty`, and the script text.

Operations to port from `ProgramEditorForm` (find each handler by its button text in the designer file):

| Old control | View model method |
|---|---|
| Mode radio buttons | `SetMode(mode)`: port `ModeRadio_CheckedChanged` exactly, including copying the shared timeline into empty stations and adding the default move for Parallel. Setting the mode from a loaded program must not run this (pitfall 2.4). |
| +Station, Duplicate, Delete, Up, Dn (outline) | `AddStation`, `DuplicateStation`, `DeleteStation`, `MoveStation(±1)` |
| +Work, +Recov, +Rest, +Move, +Repeat Group, +Pattern…, Duplicate, Delete, Up, Down (timeline) | `AddBlock(type)`, `AddRepeatGroup`, `AddPattern(pattern)`, `DuplicateItem`, `DeleteItem`, `MoveItem(±1)` |
| Property grid, Program item | `Name`, `Mode`, `Rotations` |
| Property grid, station | `Name`, `Colour`, `WorkInstruction`, `RestInstruction`, `ShowInPanel`, `LinkedStationIndex` (shown as "Same timing as…" with a station list), `Source` (shown as "Own timing / Shared timing" where the mode allows) |
| Property grid, block | `Type`, `Seconds`, `Label`, `Colour` |
| Script box | `ScriptText` with the 600 ms debounce the old timer used; invalid script shows an InfoBar and leaves the model alone |
| Save, OK, Cancel | `Save` (writes settings and the program file, applies to the session), `Discard` |
| Undo AI Change | `UndoAiChange` |
| AI Assistant… | opens the Assistant page with this program loaded |

Timeline header text: "TIMING AT EVERY STATION" for Shared and Circuit; "TIMING AT <station name>" for Sequential and Parallel; "MOVE BETWEEN STATIONS" for the move. The right-hand total line shows one station's length and the whole program's.

Block card colours: the phase colour for the block type from settings. Text is white or dark by `ReadableInkFor`.

### 7.7 Assistant (mockup 7)

`AssistantViewModel` over `AssistantController` (P6.1).

- Chat: one bubble per message; the teacher's on the right in `Bubble.User`, the assistant's on the left in `Surface.Raised`. Assistant text is passed through the existing `FormatForChat`, then lines starting "- " or a number become list items.
- Suggestion chips: three fixed prompts as in the mockup; clicking one sends it.
- Message box: Enter sends, Shift+Enter is a new line. Disabled while busy, with a Cancel button and the progress text the old form showed.
- The privacy line and the "Local AI" status pill, with "Set up local AI", "Retry" and "View details" when it is not ready (the states in `LocalAiSetupUiStateEvaluator`).
- Proposed program card: name, length, one row per station with its notes, the row outlined and tagged "changed" when it differs from the loaded program; a tag "CHECKED, READY TO USE" or "NEEDS FIXING" with the issues listed.
- Buttons: "Open in the Builder" (was Apply to Editor), "Save as…", "Copy script", "Undo change", "New chat", "Load program".

### 7.8 Exercises (not drawn)

`ExercisesViewModel` over `ExerciseCatalog`. Same card style as Programs.

- Left: a search box, filter chips (All, Mine, Edited, Hidden) and the list: name, type tags, equipment, and a source tag (Library, Edited, Mine).
- Right: the selected exercise's fields: name, coaching cue, type checkboxes, equipment needed (alphabetical), "Done on the floor / a mat", and buttons New, Save, Hide, Revert to library, Delete (only for own exercises).
- Below: "Equipment in our gym": a wrap panel of checkboxes (alphabetical), "Don't filter", "Add equipment…", "Remove equipment…".
- Footer: library status line and "Check for library updates".

## 7.9 Notes from the work so far

Read these before starting Phase 2.

- **How P1.3 and P1.5 were done.** `MainForm` holds one `AppSession` (`_session`). So that the drawing code did not have to change, `MainForm` keeps a block of small forwarding members with the old names (`_settings`, `_engine`, `_display`, `_phase`, `_index`, `_phaseClock`, `IsPaused`, `Advance()` and so on) that read or call the session. They are temporary: Phase 3 deletes `MainForm` and all of them with it. New code must use `AppSession` directly.
- **How P1.7 was done.** The link logic moved unchanged from `MainForm.Link.cs` into `LinkController`. `MainForm.Link.cs` (75 lines) now only opens the Link displays window, words the badge and refuses the session keys on a second screen. `AppSession.ReplaceSettings(edited, reset)` is the one way a settings window hands back its result: it keeps the link settings, saves, and returns false on a second screen (where the edit is kept as this PC's own and the main PC's session stays on screen).
- **How P1.8 was done.** `LinkNetworkTests` has a `LinkedPair` helper: two `AppSession`s on test ports, each with a queue for `Post`, and `Pump`/`PumpFor` to run the queues and tick both. Use it for any new link test. No test reflects on `MainForm` any more.
- **`DisplayModel` differs slightly from section 5.3.** `IsSecondScreen` and `HasLeaderProgram` are properties the window sets, not parameters. The frame builder is `BuildFrame(bool showHints)`. `DisplayFrame` has `Summary` (not `ProgramName`), `WorkLine` and `RestLine` on a station row (already worded, "WORK: High cadence"), a `Swatch` on a tile, and four fields the window fills in itself: `UpdateNotice`, `LinkBadge`, `LinkBadgeIsWarning`, `Toast`. The code is the reference: `src/GymClock.Core/Session/DisplayFrame.cs`.
- **The old `OnPaint` was not rewired to use `DisplayFrame`** (P1.6 said to). It still calls the model's methods one at a time. It is deleted in Phase 3, so rewiring it would be wasted work. `BuildFrame` is covered by `DisplayModelTests` instead, and `ClockSurface` (P3.1) is its first real user.
- **The tip under the countdown, and the highlighted station.** Mockup 1 shows a station highlighted and its note ("High cadence") under the countdown. That is right for a **Sequential** program, where the whole class is at one station. In **Circuit** and **Shared** every station is in use by a different group, so today's app highlights none, and `DisplayFrame.Tip` is empty and no `StationRow` is current. Keep that behaviour. Draw the tip line and the highlight only when the frame provides them.
- **Helper scripts.** The moves in P1.3 and P1.5 were done with one-off scripts kept in the untracked `.claude/` folder (`extract_engine.py`, `extract_display.py`). They are not needed again.

## 8. Where this guide differs from the plan

| Plan says | This guide does | Why |
|---|---|---|
| A new project `GymClock.App` | The app stays in `src/GymClock` | Less churn; nothing else depends on the folder name |
| Core replaces `System.Drawing.Color` with strings and an `Rgb` struct | Core keeps `System.Drawing.Color` | It is part of .NET itself and needs no window toolkit |
| Core has no Windows Forms reference from the start | Core references it until P5.12 | `WorkoutProgram` carries property-grid attributes until the property grid is deleted |
| `Beeper` moves to Core | `Beeper` stays in the app behind `ICuePlayer` | It plays sound through a Windows desktop API |

## 9. Open decisions

Ask Chris before the task that needs each one.

| # | Decision | Needed by | Suggested answer |
|---|---|---|---|
| D1 | Today there is one current program plus sample files, and programs are kept anywhere by Export. The Programs page shows a library. Where do "your programs" live, and does Save in the Builder write a named file there? | P5.1 | Yes: every program is a file in the app's Programs folder, named after the program; the current program is whichever is running. Existing exported files are brought in with Import. |
| D2 | The mockups draw Rest in a darker orange (`#A85A12`) so white text is readable. The settings default is `#D9822B`. Change the default? | P3.1 | Keep the existing default so current users see no change; make it easy to change on the Settings page. |
| D3 | The plan's ten decisions (its section 13) | P0.1 | As recommended there |
| D4 | Undo and redo in the Builder | P5.11 | Leave for 5.1 |

## 10. Log

Fill in as work proceeds.

| Phase | Date | Result and measurements | Signed off |
|---|---|---|---|
| 0 | 17 Nov 2026 | **Go.** All 274 tests pass on .NET 10 (three old forms needed a designer attribute). Exe size 155 MB on .NET 10 (190 MB on .NET 8). Start time: about 14 s the first time a new build runs (self-unpacking; the same on .NET 8), then 0.3 s. Local AI on .NET 10: ready in 31 s. WPF clock surface: average draw 1.3 ms, worst 4.5 ms, at 3840×2160 and at 5760×3240 pixels (limit 8 ms), hardware accelerated. Built-in Fluent theme: works, follows Windows, a button restyles with `BasedOn`; still flagged experimental (`WPF0001`). A published WPF-only app is 133 MB and shows its window in 0.6–0.8 s. Measured on the development PC only, not on a school laptop. | Chris asked to proceed |
| 1 | 17 Nov 2026 | P1.1 to P1.6 done. `GymClock.Core` created; `SessionEngine` (640 lines) and `DisplayModel` (530 lines) extracted; `MainForm.cs` went from 2,218 to 1,261 lines. Tests: 274 before, 296 now (13 engine, 9 display model), all passing locally and on GitHub. The clock was checked by eye in Circuit and Parallel after the moves: unchanged. | |
| 1 (cont.) | 17 Nov 2026 | P1.7 and P1.8 done. `AppSession` and `LinkController` (560 lines) created; `MainForm.cs` is 1,248 lines and `MainForm.Link.cs` 75. Link tests run on two `AppSession`s with no windows and no reflection. Tests: 299, all passing. **Fault found and fixed:** the link tests had been overwriting the real settings file and current program of the PC they ran on (pitfall 2.4); `AppFolders` and `TestIsolation` now prevent it, checked by comparing the real folder before and after a full test run. Not yet done: the gate (Chris's lesson and two-PC test). The linked clock has not been run on two real PCs since the move. | The link code is the riskiest move in Phase 1 and has only been tested on one PC over loopback. |
| 2 | | | |
| 3 | | | |
| 4 | | | |
| 5 | | | |
| 6 | | | |
| 7 | | | |
