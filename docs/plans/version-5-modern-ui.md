# Plan: Version 5, a modern interface

**Status:** proposal, not started. Nothing in this plan has been built.
**Written:** 17 November 2026, against the code on the `version-4` branch (4.0.0-preview.3).
**Goal:** replace the app's interface with a modern, consistent, flexible one, without removing anything a teacher can do today.

This plan says what to build, in what order, and how. Section 13 lists the decisions to confirm before work starts; each has a recommended answer, and the plan is written assuming those answers.

Two companions go with it:

- **[version-5-build-guide.md](version-5-build-guide.md):** the task-by-task instructions for whoever does the work, with exact class designs, file lists and a pass/fail check for every task. Where the guide and this plan differ, the guide wins (its section 8 lists the differences).
- **[v5-mockups/](v5-mockups/):** images of nine screens, approved by Chris on 17 November 2026, with their source files for exact sizes and colours.

---

## 1. Summary

- **What changes:** every window. The clock on the projector gets a modern look. Everything else (settings, program builder, AI assistant, exercises, link displays, licence) moves out of separate pop-up windows into one control window with a navigation rail.
- **What does not change:** what the app does. Timing, the four modes, program scripts, the AI assistant's logic, linked displays, licensing and every file format stay as they are. Every key and every action available today is still available (section 6 is the checklist).
- **The main UX change:** today the settings window opens on top of the clock, so the class loses the countdown while the teacher changes something. In version 5 the clock (the **Display**) and the controls (the **Control** window) are two surfaces. With a projector attached, the Display stays on the projector and Control opens on the laptop screen.
- **How:** move the interface from Windows Forms to WPF on .NET 10, in seven phases. The app stays shippable after every phase, because WPF and Windows Forms windows can run side by side in one app while the old windows are replaced one at a time.
- **Size:** about 46 working days of development, plus testing time in the gym (section 9).
- **First step:** a 3-day spike (Phase 0) that proves the technology choices on a school PC before anything is rewritten.

## 2. What exists today

### 2.1 Windows

| Window | File | How it is built | Lines |
|---|---|---|---|
| Clock (main window) | `MainForm.cs`, `MainForm.Link.cs` | Custom painted with GDI+ | 2,218 + 600 |
| Program & Settings | `ProgramEditorForm.cs` | Designer, fixed pixel positions | 1,280 + 848 |
| General Settings | `GeneralSettingsForm.cs` | Designer | 105 + 238 |
| Quick Setup | `QuickSetupForm.cs` | Designer | 240 + 355 |
| Pattern picker | `PatternPickerForm.cs` | Code | 143 |
| AI Assistant | `Forms/ProgramAssistantForm.cs` | Code, layout panels | 1,949 |
| Exercise Library | `Forms/ExercisesForm.cs` | Code, layout panels | 646 |
| Link displays | `Link/LinkSetupForm.cs` | Code, layout panels | 704 |
| Link help | `Link/LinkHelpForm.cs` | Code | 194 |
| About / licence | `LicenceDialog.cs` | Designer | 305 + 251 |
| Licence request | `LicenceRequestForm.cs` | Designer | 269 + 245 |

There are also about 60 pop-ups (message boxes, file dialogs and child windows), 20 of them opened from the program editor.

### 2.2 What is wrong with it

These are the problems version 5 exists to fix.

1. **Settings cover the clock.** Every window is modal and opens over the projected countdown.
2. **Three different looks.** Designer windows, code-built windows and the painted clock each have their own fonts, spacing and button styles.
3. **Fixed pixel layouts.** The designer windows place every control at a fixed pixel position and rely on Windows to scale them. In a test capture of the program editor at a higher scaling, button text was cut off ("Simple sessio", "+Statio"). Phase 0 confirms how often this happens on real PCs.
4. **A developer tool in a teacher's app.** The program editor uses a property grid ("LinkedStationIndex: -1", "Source: Custom") to edit a station.
5. **A crowded editor.** Outline, timeline, properties, script, mode, name, AI, import and export are all on one screen at once.
6. **No theme.** There is no dark or light mode and no high-contrast support outside the clock.
7. **One 2,200-line class.** `MainForm` holds the timing engine, the drawing, the keyboard handling and the link. The tests reach into its private fields by name. This makes any interface change risky.
8. **A dated clock face.** Flat rectangles, one font weight, no transitions. The title is cut off when it is long.

### 2.3 What must be kept

- A single `GymClock.exe`, no installer, runs from a USB stick or a folder.
- Works offline. No account, no cloud.
- Runs on school-managed Windows 10 and 11 PCs without admin rights.
- `settings.txt`, program files (`.program.txt`), the exercise library files and licence files keep their formats.
- The licence rules on the clock: the copyright line is always on screen, and "Licensed to …" is shown.
- Linked displays keep working, including between a version 4 PC and a version 5 PC (section 8).

## 3. Design principles

1. **The clock is never hidden during a session.** Anything a teacher does mid-session leaves the countdown visible to the class.
2. **One place for everything.** One control window, one navigation rail, one visual style.
3. **Plain words.** No property grids, no internal names. A station has a name, a colour, a work note and a rest note.
4. **Nothing is lost.** Every action in section 6 has an equivalent. Keys keep their meaning.
5. **Scales to any screen.** Layout by proportion and by Windows text size, from a 1024×768 projector to a 4K display at 200%.
6. **Readable from the back of the gym.** The Display favours size and contrast over decoration. Colour is never the only signal.
7. **Safe to change.** Each phase leaves a working app, covered by tests that do not depend on window internals.

## 4. The new experience

### 4.1 Two surfaces

| Surface | What it is | Where it appears |
|---|---|---|
| **Display** | The clock. Full screen, no controls unless asked for. | The projector or TV. |
| **Control** | Everything else, in one window. | The teacher's laptop screen. |

- **Two screens** (laptop + projector): the Display fills the projector. Control opens on the laptop screen and can stay open during a session. This is the main improvement.
- **One screen:** Control opens as a panel that slides in from the right, covering about 40% of the width. The clock shrinks into the remaining space and keeps counting. A "Full window" button expands Control for long jobs such as building a program.
- **A second screen in linked displays** shows only the Display, as now.

`F6` still moves the Display to the next monitor. Control always opens on a different monitor from the Display when one exists.

### 4.2 The Control window

A navigation rail on the left, one page at a time on the right.

| Page | Replaces | Contents |
|---|---|---|
| **Session** | (new) | What is running, large Start / Pause / Back / Skip / Reset buttons, the next three blocks, jump to station, sound and volume. Everything the keyboard does, for mouse and touch. |
| **Programs** | Import / Export, samples, "Simple session / Built program" | The programs on this PC as cards: name, mode, stations, length. Run, edit, duplicate, export, delete, import. Samples in their own group. A "Simple session" card for the work/rest/rounds session. |
| **Builder** | Program & Settings, Quick Setup, Pattern picker | The program editor (4.3). |
| **Assistant** | AI Assistant | The chat, the proposed program, Apply and Save As. |
| **Exercises** | Exercise Library | List with search and filter chips, a detail panel, gym equipment. |
| **Link displays** | Link displays, Link help | The same guided setup and built-in guide, as pages. |
| **Settings** | General Settings | Sound, clock, window, station panel, colours, appearance (light / dark / system), Display layout. |
| **About** | About / licence, Licence request | Version, licence state, enter a key, request a licence, save report, update notice. |

Message boxes become in-window dialogs and information bars in the same style. File open and save keep the standard Windows dialogs.

### 4.3 The Builder

The biggest redesign, because the current editor is the hardest window to use. It keeps the same model: stations, timelines of blocks, repeat groups, a move between stations, and a script that always matches.

- **Header:** program name, and the mode as four cards with one-line descriptions (the user's four definitions, unchanged).
- **Left, outline:** Shared Timing, Move Between Stations, then the stations. Drag to reorder. Add, duplicate and delete buttons.
- **Centre, timeline:** the selected timeline as a column of cards, one per block, coloured by block type, showing the length and label. Repeat groups are a bordered group with a "× 4" control. Drag to reorder. "Add block" opens a small menu: Work, Rest, Recovery, Move, Pattern…
- **Right, inspector:** fields for the selected item. For a station: name, colour (swatches), work note, rest note, show in panel, link to another station. For a block: type, length, label, colour. This replaces the property grid.
- **Preview:** a small live Display showing how the selected block will look on the projector.
- **Script tab:** the script text, two-way synced exactly as now, for people who prefer typing.
- **Quick Setup** becomes "New program" on the Programs page: the same pattern choices, as a short guided flow. The AI assistant's guided builder is offered beside it.
- **Save / Discard** stay explicit. A program being edited does not change the running session until saved. "Undo AI Change" stays.

New in the Builder, and optional for 5.0: general undo and redo (`Ctrl+Z`, `Ctrl+Y`).

### 4.4 The Display

Same information, same proportions, modern drawing.

- **Typography:** Segoe UI Variable Display on Windows 11, falling back to Segoe UI. Tabular (fixed-width) digits so the countdown does not shift as numbers change.
- **Shapes:** rounded station tiles and progress bars; a soft shadow under tiles.
- **Motion:** a 200 ms colour cross-fade between phases, and a short scale pulse on the last three seconds, replacing today's brightness flash. A "Reduce motion" setting turns these off, and it follows the Windows setting by default.
- **Text always fits:** every line is fitted to its box, so long program names no longer cut off.
- **Layouts (the "flexible" part):** the teacher chooses in Settings, and a second screen can choose its own.
  - **Standard:** today's layout.
  - **Big number:** the countdown and phase only, as large as possible.
  - **Stations:** the station list given half the screen, for circuits with long notes.
- **Safe margin:** a slider (0 to 8%) for projectors that cut off the edges.
- **Control bar:** moving the mouse or touching the screen shows a bar along the bottom for 4 seconds: Start/Pause, Back, Skip, Reset, Sound, Link, Controls, Full screen. It never shows on a second screen, where the session keys do nothing.
- **Unchanged:** phase colours from settings, the station table, Parallel tiles, the rotation header, toasts, the link status line, the licence and copyright lines, and the hint bar.

## 5. Technology

### 5.1 Recommendation: WPF on .NET 10

| | Stay on Windows Forms | **WPF (recommended)** | WinUI 3 | Avalonia | Web view |
|---|---|---|---|---|---|
| Modern styling, themes, animation | Poor: owner-draw everything | Good: styles, templates, animation built in | Best | Good | Good |
| Single exe, no installer | Yes | Yes | Hard: extra runtime files | Yes | Needs WebView2 |
| School-managed PCs | Yes | Yes | Risky | Yes | Depends on WebView2 |
| Runs beside the existing windows during the change | n/a | Yes, same process | No | No | Partly |
| New dependencies | None | None (part of .NET) | Windows App SDK | Avalonia packages | WebView2 |
| Screen readers, high contrast | Partial | Good | Good | Partial | Good |
| Risk | Low, but the result is still dated | Medium | High | Medium-high | High |

**Why WPF:** it is part of .NET on Windows, so there is nothing to install and no new dependency. It has real styling and animation. It can show Windows Forms windows from the same app, which is what makes a phase-by-phase change possible. The app is Windows-only by design (Bluetooth, local AI, licensing by machine), so cross-platform toolkits buy nothing.

**Why .NET 10:** .NET 8, which the app uses now, reached the end of Microsoft support on 10 November 2026. .NET 10 is the current long-term release, supported until November 2028. The SDK is already installed on the development PC.

**Fallback:** if the spike fails (section 9, Phase 0), keep Windows Forms and do Phases 1, 2 and 4 to 7 as a restyle with layout panels and a shared theme class. The two-surface model, the navigation shell and the Builder redesign still apply. The Display keeps GDI+ and loses the animation. This costs about the same and gives a lesser result.

### 5.2 To verify in the spike, not assumed

- `Microsoft.AI.Foundry.Local` and `OpenAI` packages work under `net10.0-windows`.
- A single-file, self-contained WPF publish starts in under 3 seconds on a school laptop and is not much larger than today's 199 MB exe.
- Whether to build on WPF's built-in Fluent theme or write the theme files ourselves. The built-in theme was introduced as experimental; the spike checks its state in .NET 10 and whether it can be restyled enough. Either way the app's own tokens (section 7.3) sit on top.
- The Display drawn in WPF holds 25 frames a second at 4K with eight Parallel tiles.
- Bluetooth sockets, the UDP and TCP link, and the licence check behave the same under .NET 10.

### 5.3 Rules

- No new NuGet packages for the interface. MVVM support (an observable base class and a command class) is about 60 lines written in the project.
- Icons come from the system icon fonts: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10. No image files, no icon library.
- Minimum Windows: Windows 10 22H2 and Windows 11.

## 6. Interaction parity checklist

Version 5 is not finished until every row works. This list is the acceptance test.

### 6.1 Keys on the Display

| Key | Today | Version 5 |
|---|---|---|
| `Space`, `Enter`, left click | Start / pause | Same |
| `R` | Reset | Same |
| `→`, `PageDown`, `N` | Start, or skip to the next block | Same |
| `←`, `PageUp`, `P` | Restart the interval, or go back | Same |
| `1`–`9` | Jump to a station | Same |
| `S`, `F2` | Open Program & Settings | Open Control at the Builder |
| `A` | Open the AI assistant | Open Control at the Assistant |
| `L` | Link displays | Open Control at Link displays |
| `M` | Sound on / off | Same |
| `T` | Always on top on / off | Same |
| `F`, `F11` | Full screen on / off | Same |
| `F6` | Move to the next monitor | Same |
| `F3` | About / licence | Open Control at About |
| `H`, `F1` | Show the key hints | Same |
| `Esc` | Leave full screen | Close the Control panel if open, otherwise leave full screen |
| `Q` | Quit | Same |

New, additive: `C` opens Control at the Session page. On a second screen the session keys stay blocked, as now.

### 6.2 Actions

| Area | Actions that must still be possible |
|---|---|
| Session | Start, pause, resume, skip, back, restart interval, reset, jump to station, prep countdown, finish and run again |
| Simple session | Work, rest, rounds (0 = continuous), work and rest wording, rest after final round, variable plan (`45/15x2+50/10x4`), move time and message |
| Program | Name, the four modes, stations (add, rename, duplicate, delete, reorder, colour, work note, rest note, link, hide from panel), timelines (add, edit, duplicate, delete, reorder blocks), repeat groups, patterns, move between stations, rotations for Parallel, edit as script, import, export, save |
| Quick setup | Create a program from a pattern, with preview |
| AI assistant | Set up local AI, retry, view details, chat, create a program by guided questions, revise, suggest and swap stations, ask questions, station tips and Apply, exercise ideas, validate, apply to editor, save as, copy script, load program, new chat, cancel, undo AI change |
| Exercises | Search, add, edit, hide, show hidden, revert to library, delete own, gym equipment (tick, add, remove, don't filter), check for updates |
| Link displays | Main PC in one click, second screen by code, new code, more options (sound, stations, address, Bluetooth main PC), turn off, built-in guide, link test |
| Settings | Prep seconds, sound and volume, show clock and format, always on top, station table, station name as description. Colours can be changed today only by editing `settings.txt`; that must keep working, and version 5 adds them to the Settings page. |
| Licence | See state, enter a key, load a key file, check online, request a licence, copy the computer ID, save report, open the site, see the update notice |

## 7. Architecture: how it is built

### 7.1 Projects

```
GymClock.sln
  src/GymClock.Core/        net10.0-windows   no windows at all
  src/GymClock.App/         net10.0-windows   WPF: Display, Control, themes
  src/GymClock.LinkTest/    unchanged
  tests/GymClock.Tests/     references Core and App
```

The assembly stays `GymClock.exe`. During the change `GymClock.App` also references Windows Forms, so old windows keep working until each is replaced. The reference is removed in Phase 7.

### 7.2 Core: split `MainForm` first

This is done before any new interface exists, inside the current app, with every test still passing. It is what makes the rest safe.

| New class (in Core) | Taken from | Responsibility |
|---|---|---|
| `SessionEngine` | `MainForm` lines 212–600 | Phase, index, the two `RunClock`s, `Start`, `TogglePause`, `Advance`, `GoBack`, `Reset`, `JumpToStation`, `Tick()`. Raises `Changed` and `CueRequested`. No drawing, no windows. |
| `DisplayModel` | `MainForm` text and colour methods (`PhaseLabelText`, `BigNumberText`, `RoundText`, `NextUpText`, `BackgroundColour`, `ComputeParallelPositions`, station table rows…) | Given the engine state and settings, returns a `DisplayFrame`: every string, colour, fraction and tile the screen should show. Pure, so it is unit-tested. |
| `ICuePlayer` | calls to `Beeper` | Lets the engine ask for a sound without knowing how it is played. |
| `LinkController` | `MainForm.Link.cs` | Main PC publishing, second-screen following and correction, `GetLinkStatus`, `ApplyLinkChange`. Works on a `SessionEngine`. |
| `AppSession` | `MainForm` fields `_settings`, `_ownSettings`, `_program` | Holds the settings, the program and the engine; the one object both surfaces share. |

Other moves into Core: `WorkoutProgram`, `ProgramScript`, `ProgramLibrary`, `TimerSettings`, `Licensing`, `OnlineServices`, `UsageLog`, everything in `AI/` except windows, everything in `Link/` except windows, `ExerciseCatalog`, `AppVersion`.

Two details:
- `TimerSettings` exposes `System.Drawing.Color`. Core keeps colours as `#RRGGBB` strings plus a small `Rgb` struct; each interface converts to its own colour type.
- Tests that reach into `MainForm` by reflection (`LinkNetworkTests`, `ProgramEditorModeTests`) are rewritten against the public `SessionEngine`, `LinkController` and view models. No test uses reflection on a window after Phase 1.

### 7.3 Design system

One place for every visual decision, in `GymClock.App/Themes/`.

| File | Contents |
|---|---|
| `Tokens.xaml` | Colours (surface, text, accent, success, warning, danger, per block type), type ramp (Caption 12, Body 14, Subtitle 18, Title 24, Display by proportion), spacing on a 4-pixel grid, corner radius (4 / 8), elevation |
| `Tokens.Dark.xaml`, `Tokens.Light.xaml`, `Tokens.HighContrast.xaml` | The colour values per theme. Follows Windows by default, with an override in Settings. |
| `Controls.xaml` | Styles for Button (primary, standard, subtle, danger), TextBox, NumberBox, ComboBox, CheckBox, ToggleSwitch, Slider, ListView, Card, NavigationRail, InfoBar, Dialog, Tooltip, ScrollBar |
| `Icons.cs` | Names for the icon-font glyphs used, with the Windows 10 fallbacks |

Rules for every control: a visible keyboard focus ring, a name for screen readers, a hit area of at least 32 pixels (40 on the Display's control bar), and contrast of at least 4.5:1 for text.

### 7.4 App structure

```
GymClock.App/
  App.xaml(.cs)            start-up: licence check, AppSession, windows
  Display/
    DisplayWindow.xaml     borderless, full screen, key handling
    ClockSurface.cs        draws a DisplayFrame (OnRender), 25 frames a second
    ControlBar.xaml        the mouse/touch bar
  Control/
    ControlWindow.xaml     navigation rail + page host; window or side panel
    Pages/                 SessionPage, ProgramsPage, BuilderPage, AssistantPage,
                           ExercisesPage, LinkPage, SettingsPage, AboutPage
    Dialogs/               ConfirmDialog, InputDialog, InfoBar host
  ViewModels/              one per page, plus ObservableObject and RelayCommand
  Themes/
```

- **Pattern:** MVVM. Pages bind to view models; view models call Core. No logic in page code-behind beyond focus and drag handling.
- **`ClockSurface`** is a near line-for-line port of `MainForm.OnPaint`: the same proportional rectangles (fractions of the height), drawing a `DisplayFrame` instead of reading fields. Text fitting uses cached `FormattedText`. Animation is driven by frame time, not WPF storyboards, so a frame is always consistent with the clock.
- **Start-up:** `App` runs the licence check (the blocking dialog stays a simple window), builds `AppSession`, shows `DisplayWindow`, and starts the link and background checks as `MainForm.OnShown` does now.
- **One timer** (40 ms) in `AppSession` calls `SessionEngine.Tick()` and `LinkController.Tick()`. Both surfaces redraw from `Changed` events.

### 7.5 Old window to new page

| Old | New | Notes |
|---|---|---|
| `MainForm` | `DisplayWindow` + `ClockSurface` + `SessionEngine` + `DisplayModel` | Phases 1 and 3 |
| `ProgramEditorForm` | `BuilderPage`, `ProgramsPage` | Phase 5 |
| `QuickSetupForm`, `PatternPickerForm` | "New program" flow, "Add pattern" menu | Phase 5 |
| `GeneralSettingsForm` | `SettingsPage` | Phase 4 |
| `ProgramAssistantForm` | `AssistantPage` | Phase 6. `AiProgramGenerationCoordinator`, `GuidedProgramBuilder`, `StationTips` unchanged. |
| `ExercisesForm` | `ExercisesPage` | Phase 6 |
| `LinkSetupForm`, `LinkHelpForm` | `LinkPage` | Phase 4. `LinkHelp` text unchanged. |
| `LicenceDialog`, `LicenceRequestForm` | `AboutPage`, blocking `LicenceWindow` | Phase 4 |
| `ColourUITypeEditor` | Colour swatch control | Phase 5 |

## 8. Compatibility and migration

- **Files:** no format changes. New settings keys are added to `settings.txt` and ignored by older versions: `theme`, `displaylayout`, `safemargin`, `reducemotion`, `controlbar`.
- **Linked displays:** protocol version 1 is unchanged, so version 4 and version 5 PCs link to each other. The settings text a main PC sends may contain the new keys; a version 4 second screen ignores them. Display layout is a per-PC choice and is not sent.
- **Upgrade:** replacing the exe is the whole upgrade. First run of version 5 shows a one-screen "What's new" in Control, with the key list.
- **Licences:** unaffected.

## 9. Phases

Each phase ends with a working app, all tests passing, and a preview build. Effort is development time for one developer and is an estimate.

| Phase | Work | Done when | Days |
|---|---|---|---|
| **0. Spike** | Branch `version-5`. Retarget to .NET 10. Throwaway WPF project: single-file publish, start-up time on a school PC, Foundry package, a `ClockSurface` prototype at 4K, built-in Fluent theme vs own theme. | Section 5.2 answered in writing; go, or switch to the fallback | 3 |
| **1. Core split** | Create `GymClock.Core`. Extract `SessionEngine`, `DisplayModel`, `ICuePlayer`, `LinkController`, `AppSession` (7.2). `MainForm` becomes a thin window over them. Rewrite reflection tests. | All tests pass with no reflection on windows; the app looks and behaves exactly as before; a two-PC link test passes | 6 |
| **2. Design system and shell** | `Themes/`, the control styles, `ControlWindow` with the navigation rail, the side-panel mode, light / dark / high contrast, a hidden gallery page showing every control | Gallery reviewed and approved; passes at 100, 125, 150 and 200% text size | 5 |
| **3. Display** | `DisplayWindow`, `ClockSurface`, control bar, layouts, safe margin, motion. The old `MainForm` is deleted. Old dialogs still open from the new Display. | Section 6.1 passes; snapshot tests match; side-by-side with version 4 shows the same information in every mode | 6 |
| **4. Session, Settings, Link, About** | The four simplest pages and the in-window dialogs | Their rows in 6.2 pass; `GeneralSettingsForm`, `LinkSetupForm`, `LinkHelpForm`, `LicenceDialog`, `LicenceRequestForm` deleted | 5 |
| **5. Programs and Builder** | `ProgramsPage`, `BuilderPage` (outline, timeline cards, inspector, preview, script tab, drag and drop), new-program flow | Program, Simple session and Quick setup rows in 6.2 pass; the three Everest samples can be rebuilt from scratch in the Builder; `ProgramEditorForm`, `QuickSetupForm`, `PatternPickerForm` deleted | 10 |
| **6. Assistant and Exercises** | `AssistantPage`, `ExercisesPage` | Their rows in 6.2 pass; the saved test chats give the same results; the last two old windows deleted | 6 |
| **7. Finish** | Remove the Windows Forms reference. Accessibility pass. "What's new". README and READ-ME-FIRST. Performance and start-up. Fixes from gym testing. | The full checklist in section 6 signed off on two PCs; release | 5 |
| | | **Total** | **46** |

Gym and classroom testing by Chris is on top of this: about half a day per phase from Phase 3 on.

Phases 5 and 6 do not depend on each other and can be done in either order. Phase 1 can ship as a 4.x release, because it changes no behaviour.

### 9.1 Branch and version rules

The same rules as version 4.

- Branch `version-5`, created from `main` after 4.0.0 is released.
- `<Version>5.0.0-preview.N</Version>`, with `<AssemblyVersion>` and `<FileVersion>` `4.99.N.0`, so the update check still offers the real 5.0.0.
- Fixes to the released app go on `main` first, then `git merge main` into `version-5`.
- A tag `v5.0.0-preview.N` publishes a pre-release.

## 10. Testing

| Kind | What | When |
|---|---|---|
| Unit | `SessionEngine` (every mode, start to finish, pause, skip, back, jump), `DisplayModel` (every string and colour for known states) | Phase 1 on |
| Existing | All 274 current tests keep passing, rewritten off reflection | Every phase |
| View model | Each page's view model driven without a window: commands, validation, save and discard | Phases 4–6 |
| Snapshot | `ClockSurface` rendered to an image for about 30 fixed states (each mode, idle, prep, work, rest, move, paused, finished, Parallel with 2, 4 and 8 tiles, each layout) and compared with approved images, with a small tolerance | Phase 3 on |
| Smoke | Every page and dialog constructed and shown on a test thread | Phases 2–6 |
| Scaling | 100, 125, 150, 200% text size; 1024×768, 1280×720, 1920×1080, 3840×2160 | Phases 2, 3, 7 |
| Accessibility | Keyboard-only walk through every page; Narrator reads every control; high contrast | Phase 7 |
| Parity | The section 6 checklist, by hand | End of each phase, and in full in Phase 7 |
| Link | A version 5 main PC with a version 4 second screen, and the reverse | Phases 1, 3, 7 |
| Soak | A 40-minute session on a projector, watching for stutter and memory growth | Phases 3 and 7 |

## 11. Risks

| Risk | Likelihood | What we do |
|---|---|---|
| The core split changes timing behaviour | Medium | Do it first, alone, with the existing tests as the safety net; ship it as 4.x before any interface work |
| WPF start-up or exe size is unacceptable on school PCs | Low–medium | Measured in the spike; fallback in 5.1 |
| The Foundry AI package does not support .NET 10 yet | Low | Checked in the spike; if not, stay on .NET 8 for the AI part or wait for the package |
| The Display stutters in WPF at 4K | Low | Prototype in the spike; `ClockSurface` draws directly and caches fitted text |
| The Builder redesign loses a capability | Medium | The checklist in 6.2, and rebuilding the Everest samples from scratch as the exit test |
| Teachers used to version 4 cannot find things | Medium | Keys unchanged; "What's new" page; the hint bar still lists the keys |
| Scope grows ("while we're here…") | High | Section 12 is the line. New ideas go on a 5.1 list. |
| Two interface toolkits in one app misbehave during the change | Low | Each surface is its own window; no mixed-toolkit controls inside one window |

## 12. Out of scope for 5.0

- New timing modes or changes to how sessions run.
- Changes to the AI assistant's behaviour or model.
- A phone or tablet remote, a web version, or any cloud feature.
- macOS or Linux.
- An installer or the Microsoft Store.
- Translation into other languages. Text is moved into one resource file per page so it becomes possible later.
- A touch-first redesign. Touch works through the control bar and standard controls, but the app stays keyboard-and-mouse first.

## 13. Decisions to confirm before starting

Each has the answer this plan assumes.

| # | Decision | Recommended |
|---|---|---|
| 1 | Technology | WPF on .NET 10, with the Windows Forms restyle as the fallback |
| 2 | Two surfaces: Display on the projector, Control on the laptop | Yes |
| 3 | How far the clock face changes | Same information and proportions; modern type, shapes and motion; three layouts |
| 4 | Default theme for Control | Follow Windows |
| 5 | Program edits | Explicit Save / Discard, as now. App settings apply immediately. |
| 6 | General undo and redo in the Builder | In 5.0 if Phase 5 is on time, otherwise 5.1 |
| 7 | Minimum Windows | Windows 10 22H2 and Windows 11 |
| 8 | Ship the core split (Phase 1) as a 4.x release first | Yes |
| 9 | When to start | After 4.0.0 is released, so version 5 branches from a tested base |
| 10 | Who approves the look | Chris and James review the Phase 2 gallery before any page is built |

## 14. First week

What the first five days look like once the decisions are confirmed.

1. **Day 1:** create `version-5`; retarget both projects to `net10.0-windows`; run all tests; publish and time start-up on this PC and on a school PC.
2. **Day 2:** throwaway WPF project: a window that draws the countdown with `ClockSurface` at 4K; measure frame time. Try the built-in Fluent theme.
3. **Day 3:** write up the spike against section 5.2; go or fallback. Delete the throwaway project.
4. **Days 4–5:** start Phase 1: create `GymClock.Core`, move the classes that need no changes, and begin extracting `SessionEngine` with its tests.
