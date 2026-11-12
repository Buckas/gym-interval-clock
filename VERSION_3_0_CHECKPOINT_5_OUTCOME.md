# Version 3.0 Checkpoint 5 Outcome (Real local model lifecycle)

## Scope
Checkpoint 5 requires implementation of the real local provider lifecycle and replacement of placeholder provider code.

## Implemented

### Production provider
Added `src/GymClock/AI/FoundryLocalProgramService.cs` implementing:
- `IAiProgramService`
- `IAsyncDisposable`

Implemented lifecycle behavior:
1. One-time lazy initialization guarded by `SemaphoreSlim`.
2. Foundry manager initialization using installed API:
   - `FoundryLocalManager.CreateAsync(...)`
   - `FoundryLocalManager.Instance`
3. Execution-provider discovery and download/registration:
   - `DiscoverEps()`
   - `DownloadAndRegisterEpsAsync(...)`
4. Catalog retrieval and model resolution:
   - `GetCatalogAsync(CatalogType.Local, ...)`
   - `GetModelVariantAsync(alias, ...)` then fallback to `GetModelAsync(alias, ...)`
   - fallback to first `ListModelsAsync(...)` result if alias not found
5. Model download/load and local chat-client creation:
   - `IModel.DownloadAsync(...)`
   - `IModel.LoadAsync(...)`
   - `IModel.GetChatClientAsync(...)`
6. Local generation:
   - Maps `AiChatMessage` to `Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage`
   - Calls `OpenAIChatClient.CompleteChatAsync(...)`
   - Returns first choice content (or empty string)
7. Dispose/unload:
   - `IModel.UnloadAsync(...)`
   - manager shutdown via `_manager.Shutdown()` (guarded)

### Program Editor wiring
Updated `src/GymClock/ProgramEditorForm.cs`:
- Replaced placeholder provider usage with:
  - `await using (FoundryLocalProgramService aiService = new FoundryLocalProgramService(options))`
- Removed placeholder provider class.

## Verification
- Solution build: success.
- Tests: `GymClock.Tests` 24/24 passed.

## Known runtime-environment constraints
- Availability of compatible model variants and EPs depends on local machine environment.
- First-run download/load may fail on constrained/offline devices; those are runtime environment conditions, not compile-time incompatibilities.

## Checkpoint 5 status
- **PASS (implementation + compile/test gates)**
- Runtime smoke of actual model generation on target hardware still recommended/required by later checkpoints.
