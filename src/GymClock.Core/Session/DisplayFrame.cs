using System.Collections.Generic;
using System.Drawing;

namespace GymClock
{
    /// <summary>
    /// Everything the clock shows at one moment, already worded and coloured: the answer to
    /// "what should be on screen right now?". A window draws a frame; it decides nothing.
    /// Built by <see cref="DisplayModel.BuildFrame"/>.
    /// </summary>
    public sealed class DisplayFrame
    {
        /// <summary>The whole-screen colour for the phase, with the last-three-seconds pulse applied.</summary>
        public Color Background;

        /// <summary>True while a Parallel program is running: draw the tiles instead of the single countdown.</summary>
        public bool IsParallel;

        // ---- top of the screen
        /// <summary>The summary line, top left: the program name, "STATION 2 OF 4", or the simple session's timing.</summary>
        public string Summary = string.Empty;
        public string SessionElapsed = string.Empty;
        /// <summary>The time of day, or empty when the clock is switched off.</summary>
        public string TimeOfDay = string.Empty;

        // ---- the single countdown (not Parallel)
        /// <summary>The line above the phase word: a station name, a block's announcement, or empty.</summary>
        public string Secondary = string.Empty;
        public string PhaseLabel = string.Empty;
        public string BigNumber = string.Empty;
        /// <summary>The current station's work or rest note, where the whole class is at one station; otherwise empty.</summary>
        public string Tip = string.Empty;
        public string RoundLine = string.Empty;
        public string NextUp = string.Empty;
        /// <summary>Time left in the current interval as a fraction of its length, 0 to 1.</summary>
        public double ProgressFraction;
        public bool ShowProgress;

        // ---- Parallel
        public string RotationHeader = string.Empty;
        public List<TileFrame> Tiles = new List<TileFrame>();

        // ---- the station list (empty when it is not shown)
        public List<StationRow> Stations = new List<StationRow>();

        // ---- bottom of the screen
        /// <summary>The key hints, or empty when they are hidden.</summary>
        public string Hint = string.Empty;
        public string Licensee = string.Empty;
        public string Copyright = string.Empty;
        /// <summary>The licence status line shown when the copy is not fully licensed; otherwise empty.</summary>
        public string LicenceWarning = string.Empty;
        /// <summary>True when the free-use period is about to end (drawn in a warning colour).</summary>
        public bool LicenceWarningIsUrgent;

        // ---- set by the window, which owns these
        public string UpdateNotice = string.Empty;
        public string LinkBadge = string.Empty;
        public bool LinkBadgeIsWarning;
        public string Toast = string.Empty;
    }

    /// <summary>One station's tile in a Parallel program.</summary>
    public sealed class TileFrame
    {
        public int StationIndex;
        public string Name = string.Empty;
        /// <summary>The station's own colour for the swatch beside its name, or Color.Empty.</summary>
        public Color Swatch = Color.Empty;
        public Color Background;
        /// <summary>WORK, REST, RECOVER... or DONE.</summary>
        public string PhaseLabel = string.Empty;
        public string Note = string.Empty;
        /// <summary>Empty when the station has finished.</summary>
        public string Countdown = string.Empty;
        public double ProgressFraction;
        public bool Finished;
    }

    /// <summary>One row of the station list.</summary>
    public sealed class StationRow
    {
        /// <summary>The number shown in the list (1-based, counting only the stations that are shown).</summary>
        public int Number;
        /// <summary>The station's position in the program (0-based).</summary>
        public int StationIndex;
        public string Name = string.Empty;
        /// <summary>"WORK: High cadence", or empty.</summary>
        public string WorkLine = string.Empty;
        /// <summary>"REST: Easy spin", or empty.</summary>
        public string RestLine = string.Empty;
        /// <summary>The station's own colour, or Color.Empty when it has none.</summary>
        public Color Colour = Color.Empty;
        /// <summary>True for the one station the whole class is at (Sequential, or the simple rotating session).</summary>
        public bool IsCurrent;
    }
}
