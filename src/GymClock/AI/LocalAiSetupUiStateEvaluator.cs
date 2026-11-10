using System;
using System.Globalization;

namespace GymClock.AI
{
    public sealed class LocalAiSetupUiState
    {
        public bool LocalAiReady { get; init; }
        public bool EnableSetup { get; init; }
        public bool EnableRetry { get; init; }
        public bool EnableDetails { get; init; }
        public string StatusLine { get; init; } = string.Empty;
        public string SetupMessage { get; init; } = string.Empty;
    }

    public static class LocalAiSetupUiStateEvaluator
    {
        public static LocalAiSetupUiState Evaluate(LocalAiSetupStatus status, bool isBusy)
        {
            LocalAiSetupStatus s = status ?? new LocalAiSetupStatus
            {
                Stage = LocalAiSetupStage.NotStarted,
                Message = "Local AI not set up"
            };

            string message = s.Message ?? string.Empty;
            if (s.ProgressPercent.HasValue)
            {
                message += " " + s.ProgressPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            }

            if (s.IsReady)
            {
                return new LocalAiSetupUiState
                {
                    LocalAiReady = true,
                    EnableSetup = false,
                    EnableRetry = false,
                    EnableDetails = !isBusy && !string.IsNullOrWhiteSpace(s.Details),
                    StatusLine = "Local AI status: " + message,
                    SetupMessage = "Local AI ready - FREE / ON DEVICE"
                };
            }

            if (s.IsFailure)
            {
                return new LocalAiSetupUiState
                {
                    LocalAiReady = false,
                    EnableSetup = false,
                    EnableRetry = !isBusy,
                    EnableDetails = !isBusy,
                    StatusLine = "Local AI status: " + message,
                    SetupMessage = "Local AI could not be started." + Environment.NewLine
                        + "No cloud or paid AI request has been made."
                };
            }

            return new LocalAiSetupUiState
            {
                LocalAiReady = false,
                EnableSetup = !isBusy,
                EnableRetry = false,
                EnableDetails = !isBusy && !string.IsNullOrWhiteSpace(s.Details),
                StatusLine = "Local AI status: " + message,
                SetupMessage = "Local AI runs on this computer and has no per-request API charge."
                    + Environment.NewLine
                    + "An internet connection may be required for the initial runtime/model download."
                    + Environment.NewLine
                    + "No cloud AI service will be used by Gym Interval Clock Version 3.0."
            };
        }
    }
}
