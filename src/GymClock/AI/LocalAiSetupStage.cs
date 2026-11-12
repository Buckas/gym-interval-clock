namespace GymClock.AI
{
    public enum LocalAiSetupStage
    {
        NotStarted,
        CheckingCompatibility,
        DownloadingComponents,
        DownloadingModel,
        LoadingModel,
        Ready,
        Unavailable,
        Failed
    }
}
