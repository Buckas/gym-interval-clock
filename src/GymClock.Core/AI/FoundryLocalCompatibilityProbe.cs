using System.Threading;
using System.Threading.Tasks;
using Microsoft.AI.Foundry.Local;
using OpenAI.Chat;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymClock.AI
{
    /// <summary>
    /// Checkpoint 4 compile-only probe.
    ///
    /// This file exists solely to verify the currently installed Foundry Local
    /// and OpenAI package APIs can be referenced by this project target.
    /// It is intentionally not called at runtime.
    /// </summary>
    internal static class FoundryLocalCompatibilityProbe
    {
        internal static async Task ProbeAsync(CancellationToken cancellationToken = default)
        {
            // Manager creation/lifecycle primitives using installed SDK signature.
            await FoundryLocalManager.CreateAsync(
                null,
                NullLogger.Instance,
                cancellationToken).ConfigureAwait(false);

            FoundryLocalManager manager = FoundryLocalManager.Instance;

            // Model abstraction surface + OpenAI chat type surface.
            // This is a compile compatibility check only; no real generation is run here.
            ChatClient chatClient = null;

            _ = manager;
            _ = chatClient;
        }
    }
}
