using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymClock.AI
{
    public sealed class FoundryLocalProgramService : IAiProgramService, ILocalAiSetupService, IAsyncDisposable
    {
        private readonly AiAssistantOptions _options;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        private FoundryLocalManager _manager;
        private ICatalog _catalog;
        private IModel _model;
        private OpenAIChatClient _chatClient;
        private bool _disposed;
        private LocalAiSetupStatus _lastSetupStatus = new LocalAiSetupStatus
        {
            Stage = LocalAiSetupStage.NotStarted,
            Message = "Local AI not set up"
        };

        public LocalAiSetupStatus LastSetupStatus
        {
            get { return _lastSetupStatus; }
        }

        public FoundryLocalProgramService(AiAssistantOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<string> GenerateTextAsync(
            IReadOnlyList<AiChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FoundryLocalProgramService));
            if (messages == null || messages.Count == 0) return string.Empty;

            await EnsureReadyAsync(null, cancellationToken).ConfigureAwait(false);

            IEnumerable<ChatMessage> request = messages.Select(m =>
                m.Role == AiChatRole.User
                    ? ChatMessage.FromUser(m.Content ?? string.Empty)
                    : ChatMessage.FromAssistant(m.Content ?? string.Empty));

            var response = await _chatClient.CompleteChatAsync(request, cancellationToken).ConfigureAwait(false);

            string text = response?.Choices?.FirstOrDefault()?.Message?.Content;
            return text ?? string.Empty;
        }

        public async Task<LocalAiSetupStatus> EnsureReadyAsync(
            Action<LocalAiSetupStatus> onProgress,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FoundryLocalProgramService));
            if (_chatClient != null)
            {
                LocalAiSetupStatus ready = UpdateStatus(LocalAiSetupStage.Ready, "Local AI ready - FREE / ON DEVICE", null, 100, onProgress);
                return ready;
            }

            await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_chatClient != null)
                {
                    return UpdateStatus(LocalAiSetupStage.Ready, "Local AI ready - FREE / ON DEVICE", null, 100, onProgress);
                }

                UpdateStatus(LocalAiSetupStage.CheckingCompatibility, "Checking local AI compatibility...", null, null, onProgress);

                await FoundryLocalManager.CreateAsync(null, NullLogger.Instance, cancellationToken).ConfigureAwait(false);
                _manager = FoundryLocalManager.Instance;

                UpdateStatus(LocalAiSetupStage.DownloadingComponents, "Downloading local AI components...", null, null, onProgress);
                _manager.DiscoverEps();
                await _manager.DownloadAndRegisterEpsAsync(cancellationToken).ConfigureAwait(false);

                _catalog = await _manager.GetCatalogAsync(CatalogType.Local, cancellationToken).ConfigureAwait(false);

                _model = await _catalog.GetModelVariantAsync(_options.LocalModelAlias, cancellationToken).ConfigureAwait(false);
                if (_model == null)
                {
                    _model = await _catalog.GetModelAsync(_options.LocalModelAlias, cancellationToken).ConfigureAwait(false);
                }

                if (_model == null)
                {
                    IEnumerable<IModel> models = await _catalog.ListModelsAsync(cancellationToken).ConfigureAwait(false);
                    _model = models.FirstOrDefault();
                }

                if (_model == null)
                {
                    throw new InvalidOperationException("No compatible local models were found in the Foundry catalog.");
                }

                UpdateStatus(LocalAiSetupStage.DownloadingModel, "Downloading local model...", null, null, onProgress);
                await _model.DownloadAsync(progress =>
                {
                    UpdateStatus(
                        LocalAiSetupStage.DownloadingModel,
                        "Downloading local model...",
                        null,
                        progress,
                        onProgress);
                }, cancellationToken).ConfigureAwait(false);

                UpdateStatus(LocalAiSetupStage.LoadingModel, "Loading local model...", null, null, onProgress);
                await _model.LoadAsync(cancellationToken).ConfigureAwait(false);
                _chatClient = await _model.GetChatClientAsync(cancellationToken).ConfigureAwait(false);

                return UpdateStatus(LocalAiSetupStage.Ready, "Local AI ready - FREE / ON DEVICE", null, 100, onProgress);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LocalAiSetupStatus failed = UpdateStatus(
                    LocalAiSetupStage.Failed,
                    "Local AI could not be started.",
                    ex.Message,
                    null,
                    onProgress);
                throw new LocalAiSetupException(failed.Message, ex);
            }
            finally
            {
                _initLock.Release();
            }
        }

        private LocalAiSetupStatus UpdateStatus(
            LocalAiSetupStage stage,
            string message,
            string details,
            float? progress,
            Action<LocalAiSetupStatus> onProgress)
        {
            LocalAiSetupStatus status = new LocalAiSetupStatus
            {
                Stage = stage,
                Message = message ?? string.Empty,
                Details = details ?? string.Empty,
                ProgressPercent = progress
            };
            _lastSetupStatus = status;
            onProgress?.Invoke(status);
            return status;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_model != null)
                {
                    await _model.UnloadAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                _initLock.Dispose();

                if (_manager != null && FoundryLocalManager.IsInitialized)
                {
                    try { _manager.Shutdown(); } catch { }
                }
            }
        }
    }
}
