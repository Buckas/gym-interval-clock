namespace GymClock
{
    /// <summary>Plays the engine's cues through <see cref="Beeper"/> (which honours its own Enabled and Volume).</summary>
    public sealed class BeeperCuePlayer : ICuePlayer
    {
        public void Play(Cue cue)
        {
            switch (cue)
            {
                case Cue.WorkStart: Beeper.CueWorkStart(); break;
                case Cue.RestStart: Beeper.CueRestStart(); break;
                case Cue.MoveStart: Beeper.CueMoveStart(); break;
                case Cue.PrepStart: Beeper.CuePrepStart(); break;
                case Cue.Countdown: Beeper.CueCountdown(); break;
                case Cue.Pause: Beeper.CuePause(); break;
                case Cue.Finish: Beeper.CueFinish(); break;
            }
        }
    }
}
