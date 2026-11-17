namespace GymClock
{
    /// <summary>The sounds a session can ask for.</summary>
    public enum Cue
    {
        WorkStart,
        RestStart,
        MoveStart,
        PrepStart,
        Countdown,
        Pause,
        Finish
    }

    /// <summary>
    /// Plays a sound cue. The timing engine asks; the app decides how (and whether) it sounds,
    /// so the engine needs no audio code and tests can record what was asked for.
    /// </summary>
    public interface ICuePlayer
    {
        void Play(Cue cue);
    }

    /// <summary>A cue player that does nothing, for when no sound is wanted.</summary>
    public sealed class SilentCuePlayer : ICuePlayer
    {
        public void Play(Cue cue)
        {
        }
    }
}
