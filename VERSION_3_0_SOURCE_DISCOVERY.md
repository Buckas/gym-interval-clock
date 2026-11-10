# Version 3.0 Source Discovery (Checkpoint 2)

Active project inspected: `src/GymClock/GymClock.csproj`

## Root namespace
- `GymClock`

## Program model and related types
- Primary model type: `WorkoutProgram` (`src/GymClock/WorkoutProgram.cs`)
- Execution mode enum: `ExecutionMode` (`Shared`, `Sequential`, `Parallel`, `Circuit`)
- Station type: `StationDef`
- Timeline types: `Timeline`, `TimelineItem`, `ResolvedBlock`
- Repeat type: `RepeatGroup`
- Block type: `Block` + `BlockType`

## Current Program Editor program state
- Field: `private WorkoutProgram _program;` (`src/GymClock/ProgramEditorForm.cs`)
- Constructor load path:
  - `_program = !string.IsNullOrEmpty(settings.ProgramScript) ? ProgramScriptFormat.Parse(settings.ProgramScript) : new WorkoutProgram();`

## Program script control and sync path
- Script text control field: `_script`
- Text change hook: `Script_TextChanged`
- Debounced parse hook: `_scriptSyncTimer` -> `ScriptSyncTimer_Tick`
- Program refresh from script:
  - `WorkoutProgram parsed = ProgramScriptFormat.Parse(_script.Text);`
  - assign `_program = parsed;` then `RefreshOutline(); RefreshPropertiesForOutline();`

## Parser/serializer authoritative methods
- Parse: `ProgramScriptFormat.Parse(string text)` (`src/GymClock/ProgramScript.cs`)
- Serialize: `ProgramScriptFormat.ToScript(WorkoutProgram program)` (`src/GymClock/ProgramScript.cs`)

## Editor refresh/rebind path
- Whole program apply: `ApplyProgram(WorkoutProgram program)`
- Tree/list rebuild: `RefreshOutline()`
- Script regen from model: `RefreshScriptFromProgram()`
- Properties refresh: `RefreshPropertiesForOutline()`

## Save / import / export paths
- Export handler: `ExportProgram_Click`
  - Writes `ProgramScriptFormat.ToScript(_program)` to selected file.
- Import handler: `ImportProgram_Click`
  - Reads file text, parses via `ProgramScriptFormat.Parse`, applies via `ApplyProgram(program)`.
- Save handler: `Save_Click`
  - `ApplyControlsTo(Result)`
  - `Result.Save()`
  - `ProgramLibrary.SaveCurrentProgramScript(Result.ProgramScript)`

## Dirty/change tracking mechanism
- No single global dirty-flag field discovered.
- Current behavior relies on immediate model mutation via UI handlers and explicit save (`Save_Click`) / commit (`Ok_Click`), with script debounce parse path updating `_program` from `_script`.

## Existing undo implementation
- No explicit editor-wide undo stack for program-model changes discovered in `ProgramEditorForm`.
- Timeline/station reorder and edits are direct mutations with refresh.

## Runtime/timeline duration-calculation methods
- `Timeline.TotalSeconds()`
- `WorkoutProgram.BuildRuntimeSequence()` (Shared/Sequential/Circuit flattened runtime)
- `WorkoutProgram.BuildParallelSequences()` (Parallel per-station runtime)
- `ParallelClock.Locate(...)` (parallel time-position semantics)

## Discovery outcome for V3
- Reuse these existing parser/model/editor/runtime pathways.
- Do not invent parallel model/parser stacks.
- Strict AI validation must front `ProgramScriptFormat.Parse` and then perform model-level sanity checks against `WorkoutProgram` runtime semantics.
