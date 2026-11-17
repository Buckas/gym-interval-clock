namespace GymClock.AI
{
    public sealed class LocalAiSetupStatus
    {
        public LocalAiSetupStage Stage { get; init; } = LocalAiSetupStage.NotStarted;
        public string Message { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
        public float? ProgressPercent { get; init; }

        public bool IsReady
        {
            get { return Stage == LocalAiSetupStage.Ready; }
        }

        public bool IsFailure
        {
            get { return Stage == LocalAiSetupStage.Unavailable || Stage == LocalAiSetupStage.Failed; }
        }
    }
}
