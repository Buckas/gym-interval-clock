using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GymClock.AI
{
    public interface IAiProgramService
    {
        Task<string> GenerateTextAsync(
            IReadOnlyList<AiChatMessage> messages,
            CancellationToken cancellationToken = default);
    }
}
