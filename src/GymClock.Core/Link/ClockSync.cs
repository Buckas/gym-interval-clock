using System;
using System.Collections.Generic;

namespace GymClock.Link
{
    public enum SyncAction
    {
        /// <summary>Close enough - leave the clock alone.</summary>
        None,
        /// <summary>A small drift - close part of the gap so the countdown never visibly jumps.</summary>
        Ease,
        /// <summary>A real difference (pause, skip, reconnect) - go straight to the main PC's value.</summary>
        Snap
    }

    /// <summary>
    /// Works out how far this PC's clock is from the main PC's, from ping/pong timings, and how to
    /// correct a second screen's countdown. Pure maths - no network, no timers - so it is unit-tested.
    ///
    /// Each PC has its own millisecond clock that starts when its link starts. A ping records the
    /// local time it left (t0) and came back (t1), and the main PC's time in between. The one-way
    /// delay is taken as half the round trip, so: offset = leaderTime + (t1 - t0) / 2 - t1, and
    /// leader time = local time + offset.
    /// </summary>
    public sealed class ClockSync
    {
        /// <summary>Below this the screens already agree (seconds).</summary>
        public const double IgnoreBelowSeconds = 0.02;
        /// <summary>Up to this a drift is eased out; beyond it the clock snaps (seconds).</summary>
        public const double SnapAboveSeconds = 0.25;
        /// <summary>Share of a small drift closed on each state message (about 4 a second).</summary>
        public const double EaseShare = 0.25;

        private const int Window = 8;
        private readonly List<KeyValuePair<long, long>> _samples = new List<KeyValuePair<long, long>>(); // (round trip, offset)
        private readonly object _lock = new object();

        public bool HasOffset { get; private set; }

        /// <summary>Leader time minus local time, in milliseconds.</summary>
        public long OffsetMs { get; private set; }

        /// <summary>The quickest recent round trip, in milliseconds.</summary>
        public long RoundTripMs { get; private set; }

        public void Reset()
        {
            lock (_lock)
            {
                _samples.Clear();
                HasOffset = false;
                OffsetMs = 0;
                RoundTripMs = 0;
            }
        }

        /// <summary>Adds one ping result: local send time, the leader's time when it answered, local receive time.</summary>
        public void AddSample(long localSentMs, long leaderTimeMs, long localReceivedMs)
        {
            long roundTrip = localReceivedMs - localSentMs;
            if (roundTrip < 0) return;
            long offset = leaderTimeMs + (roundTrip / 2) - localReceivedMs;

            lock (_lock)
            {
                _samples.Add(new KeyValuePair<long, long>(roundTrip, offset));
                if (_samples.Count > Window) _samples.RemoveAt(0);

                // The quickest recent round trip was delayed least, so its offset is the most trustworthy.
                KeyValuePair<long, long> best = _samples[0];
                foreach (KeyValuePair<long, long> sample in _samples)
                {
                    if (sample.Key < best.Key) best = sample;
                }
                RoundTripMs = best.Key;
                OffsetMs = best.Value;
                HasOffset = true;
            }
        }

        /// <summary>The main PC's clock right now, given this PC's clock.</summary>
        public long ToLeaderTime(long localMs)
        {
            return localMs + OffsetMs;
        }

        /// <summary>What to do about a difference between the main PC's countdown and this screen's (seconds, leader minus local).</summary>
        public static SyncAction Classify(double driftSeconds)
        {
            double size = Math.Abs(driftSeconds);
            if (size < IgnoreBelowSeconds) return SyncAction.None;
            return size <= SnapAboveSeconds ? SyncAction.Ease : SyncAction.Snap;
        }

        /// <summary>The corrected local elapsed time for a given drift (seconds).</summary>
        public static double Correct(double localElapsedSeconds, double leaderElapsedSeconds)
        {
            double drift = leaderElapsedSeconds - localElapsedSeconds;
            switch (Classify(drift))
            {
                case SyncAction.Snap: return leaderElapsedSeconds;
                case SyncAction.Ease: return localElapsedSeconds + (drift * EaseShare);
                default: return localElapsedSeconds;
            }
        }
    }
}
