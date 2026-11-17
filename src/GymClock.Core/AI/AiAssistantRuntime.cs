using System;
using System.Threading.Tasks;

namespace GymClock.AI
{
    public static class AiAssistantRuntime
    {
        private static readonly object Sync = new object();

        private static bool _initialized;
        private static Task _warmupTask;
        private static Action<LocalAiSetupStatus> _warmupStatusChanged;

        public static AiAssistantOptions Options { get; private set; }
        public static ProgramProposalValidator Validator { get; private set; }
        public static FoundryLocalProgramService Service { get; private set; }
        public static AiProgramGenerationCoordinator Coordinator { get; private set; }

        public static event Action<LocalAiSetupStatus> WarmupStatusChanged
        {
            add
            {
                if (value == null) return;

                LocalAiSetupStatus current = null;
                lock (Sync)
                {
                    _warmupStatusChanged += value;
                    if (_initialized)
                    {
                        current = Service?.LastSetupStatus;
                    }
                }

                if (current != null)
                {
                    value(current);
                }
            }
            remove
            {
                lock (Sync)
                {
                    _warmupStatusChanged -= value;
                }
            }
        }

        public static void EnsureInitialized()
        {
            lock (Sync)
            {
                if (_initialized) return;

                Options = new AiAssistantOptions();
                Validator = new ProgramProposalValidator();
                Service = new FoundryLocalProgramService(Options);
                Coordinator = new AiProgramGenerationCoordinator(Service, Validator, Options);

                _initialized = true;
            }
        }

        public static Task StartWarmupIfNeeded()
        {
            EnsureInitialized();

            lock (Sync)
            {
                if (Service.LastSetupStatus != null && Service.LastSetupStatus.IsReady)
                {
                    PublishStatus(Service.LastSetupStatus);
                    return Task.CompletedTask;
                }

                if (_warmupTask != null && !_warmupTask.IsCompleted)
                {
                    return _warmupTask;
                }

                _warmupTask = WarmupCoreAsync();
                return _warmupTask;
            }
        }

        private static async Task WarmupCoreAsync()
        {
            try
            {
                LocalAiSetupStatus status = await Service.EnsureReadyAsync(PublishStatus).ConfigureAwait(false);
                PublishStatus(status);
            }
            catch
            {
                PublishStatus(Service.LastSetupStatus);
            }
        }

        private static void PublishStatus(LocalAiSetupStatus status)
        {
            Action<LocalAiSetupStatus> handler;
            lock (Sync)
            {
                handler = _warmupStatusChanged;
            }

            handler?.Invoke(status);
        }
    }
}
