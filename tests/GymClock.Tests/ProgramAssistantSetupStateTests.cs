using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    public class ProgramAssistantSetupStateTests
    {
        [Fact]
        public void NotStarted_State_EnablesSetup_DisablesRetry()
        {
            LocalAiSetupStatus status = new LocalAiSetupStatus
            {
                Stage = LocalAiSetupStage.NotStarted,
                Message = "Local AI not set up"
            };

            LocalAiSetupUiState ui = LocalAiSetupUiStateEvaluator.Evaluate(status, isBusy: false);

            Assert.False(ui.LocalAiReady);
            Assert.True(ui.EnableSetup);
            Assert.False(ui.EnableRetry);
            Assert.Contains("Local AI runs on this computer", ui.SetupMessage);
        }

        [Fact]
        public void Failed_State_EnablesRetry_AndDetails()
        {
            LocalAiSetupStatus status = new LocalAiSetupStatus
            {
                Stage = LocalAiSetupStage.Failed,
                Message = "Local AI could not be started.",
                Details = "Disk space is insufficient."
            };

            LocalAiSetupUiState ui = LocalAiSetupUiStateEvaluator.Evaluate(status, isBusy: false);

            Assert.False(ui.LocalAiReady);
            Assert.False(ui.EnableSetup);
            Assert.True(ui.EnableRetry);
            Assert.True(ui.EnableDetails);
            Assert.Contains("No cloud or paid AI request has been made", ui.SetupMessage);
        }

        [Fact]
        public void Ready_State_DisablesSetupAndRetry()
        {
            LocalAiSetupStatus status = new LocalAiSetupStatus
            {
                Stage = LocalAiSetupStage.Ready,
                Message = "Local AI ready - FREE / ON DEVICE"
            };

            LocalAiSetupUiState ui = LocalAiSetupUiStateEvaluator.Evaluate(status, isBusy: false);

            Assert.True(ui.LocalAiReady);
            Assert.False(ui.EnableSetup);
            Assert.False(ui.EnableRetry);
            Assert.Equal("Local AI ready - FREE / ON DEVICE", ui.SetupMessage);
        }

        [Fact]
        public void Busy_NotStarted_DisablesSetupTemporarily()
        {
            LocalAiSetupStatus status = new LocalAiSetupStatus
            {
                Stage = LocalAiSetupStage.NotStarted,
                Message = "Checking local AI compatibility..."
            };

            LocalAiSetupUiState ui = LocalAiSetupUiStateEvaluator.Evaluate(status, isBusy: true);

            Assert.False(ui.EnableSetup);
            Assert.False(ui.EnableRetry);
        }

        [Fact]
        public void Progress_IsAppendedToStatusLine()
        {
            LocalAiSetupStatus status = new LocalAiSetupStatus
            {
                Stage = LocalAiSetupStage.DownloadingModel,
                Message = "Downloading local model...",
                ProgressPercent = 52.3f
            };

            LocalAiSetupUiState ui = LocalAiSetupUiStateEvaluator.Evaluate(status, isBusy: false);

            Assert.Contains("52.3%", ui.StatusLine);
        }
    }
}
