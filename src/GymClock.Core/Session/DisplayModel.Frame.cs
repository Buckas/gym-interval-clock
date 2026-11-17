using System;
using System.Collections.Generic;
using System.Drawing;

namespace GymClock
{
    public sealed partial class DisplayModel
    {
        /// <summary>
        /// One frame: everything the clock shows right now. The decisions are the ones the old
        /// MainForm.OnPaint, DrawStationTile and DrawStationsTable made while drawing; here they
        /// are made once, as data, so any window can draw them and tests can check them.
        /// </summary>
        /// <param name="showHints">True while the key hints are showing (after H, when idle, paused or finished).</param>
        public DisplayFrame BuildFrame(bool showHints)
        {
            DisplayFrame frame = new DisplayFrame();
            bool parallel = IsParallelActive;
            bool timed = _phase == Phase.Prep || _phase == Phase.Active;
            double remaining = RemainingSeconds();

            // Background: colour alone tells the room which phase they are in. Parallel tiles
            // pulse individually, so the base behind them stays put.
            Color background = BackgroundColour();
            if (!parallel && timed) background = ApplyPulse(background, remaining, _phaseClock.IsRunning);
            frame.Background = background;
            frame.IsParallel = parallel;

            frame.Summary = ConfigSummaryText();
            frame.SessionElapsed = SessionElapsedText();
            frame.TimeOfDay = Settings.ShowClock ? CurrentTimeText() : string.Empty;

            if (parallel)
            {
                frame.RotationHeader = ParallelRotationText();
                frame.Tiles = BuildTiles();
            }
            else
            {
                frame.Secondary = SecondaryDescriptionText();
                frame.PhaseLabel = PhaseLabelText();
                frame.BigNumber = BigNumberText();
                frame.Tip = TipText();
                frame.RoundLine = RoundText();
                frame.NextUp = NextUpText();
                frame.ShowProgress = timed;
                if (timed)
                {
                    double length = _engine.PhaseLength;
                    double fraction = length > 0 ? remaining / length : 0;
                    frame.ProgressFraction = Math.Max(0, Math.Min(1, fraction));
                }
            }

            // The station list is not shown once the Parallel tiles are up - they already list every station.
            if (Settings.ShowStationsTable && !parallel) frame.Stations = BuildStationRows();

            frame.Hint = showHints ? HintText() : string.Empty;

            LicenceStatus licence = Licensing.Current;
            frame.Copyright = Licensing.ShortCopyright;
            if (licence.State == LicenceState.Licensed)
            {
                frame.Licensee = licence.Licence != null ? "Licensed to " + licence.Licence.Name : string.Empty;
            }
            else
            {
                // Deliberately unobtrusive but always present; only warnings need to show here.
                frame.LicenceWarning = Licensing.ShortDescription(licence);
                frame.LicenceWarningIsUrgent = licence.State == LicenceState.TrialEnding;
            }

            return frame;
        }

        /// <summary>
        /// The work or rest note for the station the whole class is at: a Sequential program's
        /// current station, or the simple session's rotating station. Empty in Shared and Circuit,
        /// where groups are at different stations and no single note applies.
        /// </summary>
        public string TipText()
        {
            if (_phase != Phase.Active || IsPaused) return string.Empty;
            RuntimeBlock current = CurrentBlock();
            if (current == null) return string.Empty;

            if (UsingLegacyStationRotation)
            {
                Station legacy = LegacyCurrentStation();
                if (legacy == null) return string.Empty;
                switch (current.Block.Type)
                {
                    case BlockType.Work: return legacy.WorkDetail ?? string.Empty;
                    case BlockType.Rest:
                    case BlockType.Recovery: return legacy.RestDetail ?? string.Empty;
                    default: return string.Empty;
                }
            }

            if (_program.Mode != ExecutionMode.Sequential) return string.Empty;
            int stationIndex = ActiveStationIndexForDisplay();
            return stationIndex < 0 ? string.Empty : TileNoteFor(stationIndex, current.Block);
        }

        /// <summary>One tile per station of a running Parallel program (the data DrawStationTile used to work out).</summary>
        private List<TileFrame> BuildTiles()
        {
            List<TileFrame> tiles = new List<TileFrame>();
            List<StationPosition> positions = _engine.ComputeParallelPositions();

            for (int index = 0; index < positions.Count; index++)
            {
                StationPosition position = positions[index];
                bool hasStation = index < _program.Stations.Count;

                TileFrame tile = new TileFrame
                {
                    StationIndex = index,
                    Name = hasStation ? _program.Stations[index].Name : _program.Name
                };

                string colourText = hasStation ? _program.Stations[index].Colour : string.Empty;
                if (!string.IsNullOrEmpty(colourText))
                {
                    Color swatch = TimerSettings.ParseColour(colourText, Color.Transparent);
                    if (swatch.A > 0) tile.Swatch = swatch;
                }

                if (position.Block == null || position.Finished)
                {
                    tile.Background = Settings.DoneBg;
                    tile.PhaseLabel = "DONE";
                    tile.Finished = true;
                }
                else
                {
                    Block block = position.Block.Block;
                    Color baseColour = Settings.ColourFor(block.Type, block.Colour);
                    tile.Background = ApplyPulse(baseColour, position.RemainingSeconds, _phaseClock.IsRunning);
                    tile.PhaseLabel = block.Word;
                    tile.Note = TileNoteFor(index, block);
                    tile.Countdown = FormatSeconds(position.RemainingSeconds);
                    double fraction = block.Seconds > 0 ? position.RemainingSeconds / block.Seconds : 0;
                    tile.ProgressFraction = Math.Max(0, Math.Min(1, fraction));
                }

                tiles.Add(tile);
            }
            return tiles;
        }

        /// <summary>
        /// The station list (the data DrawStationsTable used to work out). In Shared Timing and
        /// Circuit every station is in use at once, so nothing is marked current; in Sequential the
        /// one station running is; the simple rotating session marks its own.
        /// </summary>
        private List<StationRow> BuildStationRows()
        {
            List<StationRow> rows = new List<StationRow>();
            List<StationDef> all = _program.Stations;
            if (all.Count == 0) return rows;

            int currentAllIndex = -1;
            if (_phase != Phase.Idle && _phase != Phase.Finished)
            {
                if (UsingLegacyStationRotation)
                {
                    Station legacy = LegacyCurrentStation();
                    if (legacy != null) currentAllIndex = Settings.Stations.IndexOf(legacy);
                }
                else if (_program.Mode == ExecutionMode.Sequential)
                {
                    currentAllIndex = ActiveStationIndexForDisplay();
                }
            }

            string workLabel = (Settings.WorkLabel ?? string.Empty).ToUpperInvariant();
            string restLabel = (Settings.RestLabel ?? string.Empty).ToUpperInvariant();

            for (int i = 0; i < all.Count; i++)
            {
                StationDef station = all[i];
                if (!station.ShowInPanel) continue;

                StationRow row = new StationRow
                {
                    Number = rows.Count + 1,
                    StationIndex = i,
                    Name = station.Name,
                    IsCurrent = i == currentAllIndex,
                    WorkLine = string.IsNullOrEmpty(station.WorkInstruction) ? string.Empty : workLabel + ": " + station.WorkInstruction,
                    RestLine = string.IsNullOrEmpty(station.RestInstruction) ? string.Empty : restLabel + ": " + station.RestInstruction
                };

                if (!string.IsNullOrEmpty(station.Colour))
                {
                    Color colour = TimerSettings.ParseColour(station.Colour, Color.Transparent);
                    if (colour.A > 0) row.Colour = colour;
                }

                rows.Add(row);
            }
            return rows;
        }
    }
}
