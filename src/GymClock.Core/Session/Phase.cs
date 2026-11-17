namespace GymClock
{
    /// <summary>Where a session is: not started, in the get-ready countdown, running its blocks, or done.</summary>
    public enum Phase
    {
        Idle,
        Prep,
        Active,
        Finished
    }
}
