using System;
using System.Diagnostics;

namespace GymClock.Link
{
    /// <summary>
    /// A stopwatch that can also be set to a given elapsed time - what the session clocks need
    /// so a second screen can be put at the main PC's position (see MainForm.ApplyLeaderState).
    /// Otherwise it behaves exactly like System.Diagnostics.Stopwatch.
    /// </summary>
    public sealed class RunClock
    {
        private readonly Stopwatch _watch = new Stopwatch();
        private TimeSpan _base = TimeSpan.Zero;

        public TimeSpan Elapsed
        {
            get { return _base + _watch.Elapsed; }
        }

        public bool IsRunning
        {
            get { return _watch.IsRunning; }
        }

        public void Start() { _watch.Start(); }

        public void Stop() { _watch.Stop(); }

        public void Reset()
        {
            _base = TimeSpan.Zero;
            _watch.Reset();
        }

        public void Restart()
        {
            _base = TimeSpan.Zero;
            _watch.Restart();
        }

        /// <summary>Puts the clock at <paramref name="elapsed"/>, running or stopped.</summary>
        public void Set(TimeSpan elapsed, bool running)
        {
            _base = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            if (running) _watch.Restart(); else _watch.Reset();
        }
    }
}
