using System;
using System.Threading;
using System.Threading.Tasks;

namespace GymClock.AI
{
    public interface ILocalAiSetupService
    {
        LocalAiSetupStatus LastSetupStatus { get; }

        Task<LocalAiSetupStatus> EnsureReadyAsync(
            Action<LocalAiSetupStatus> onProgress,
            CancellationToken cancellationToken = default);
    }
}
