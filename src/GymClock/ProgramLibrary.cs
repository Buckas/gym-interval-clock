using System;
using System.IO;

namespace GymClock
{
    /// <summary>
    /// Owns the appdata-backed program storage: the single "current" program that
    /// starts up with the app, and a folder of sample programs installed the
    /// first time the app runs. Kept separate from TimerSettings.FilePath so the
    /// settings text file only ever holds settings, not program data.
    /// </summary>
    public static class ProgramLibrary
    {
        /// <summary>%AppData%\GymClock\Programs - where the current program and every sample program live.</summary>
        public static string RootFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GymClock", "Programs");
            }
        }

        /// <summary>Sub-folder holding the three built-in sample programs, installed on first run.</summary>
        public static string SamplesFolder
        {
            get { return Path.Combine(RootFolder, "Samples"); }
        }

        /// <summary>The program that loads automatically when the app starts.</summary>
        public static string CurrentProgramFilePath
        {
            get { return Path.Combine(RootFolder, "current.program.txt"); }
        }

        /// <summary>
        /// Creates the appdata program folders and writes out the sample programs
        /// if they aren't already there. Safe to call on every startup - existing
        /// files are never overwritten, so user edits to the samples are kept.
        /// </summary>
        public static void EnsureInstalled()
        {
            try
            {
                if (!Directory.Exists(RootFolder)) Directory.CreateDirectory(RootFolder);
                if (!Directory.Exists(SamplesFolder)) Directory.CreateDirectory(SamplesFolder);

                WriteIfMissing(Path.Combine(SamplesFolder, "Interval Cardio Circuit.program.txt"), IntervalCardioCircuit);
                WriteIfMissing(Path.Combine(SamplesFolder, "Pyramid Cardio Circuit.program.txt"), PyramidCardioCircuit);
                WriteIfMissing(Path.Combine(SamplesFolder, "Whole Group HIIT.program.txt"), WholeGroupHiit);
                WriteIfMissing(Path.Combine(SamplesFolder, "Team Tabata Blast.program.txt"), TeamTabataBlast);
                WriteIfMissing(Path.Combine(SamplesFolder, "Mixed Pace Stations.program.txt"), MixedPaceStations);

                // Always rewritten (not WriteIfMissing) - the sample previously
                // shipped with a broken script (per-station timing under
                // Sequential mode instead of one shared timeline), so an
                // existing installation would otherwise be stuck with the
                // broken copy on disk forever. Only this file's known-stale
                // content is force-refreshed; every other sample still follows
                // the normal "never overwrite" rule so a teacher's edits to
                // those are preserved.
                File.WriteAllText(Path.Combine(SamplesFolder, "Rotating Group Circuit.program.txt"), RotatingGroupCircuit);

                // First run: seed the startup program itself, so there is always a
                // real default program instead of falling back to a generic
                // synthesised session the first time the app is opened.
                WriteIfMissing(CurrentProgramFilePath, IntervalCardioCircuit);

                // One-time repair: a teacher who loaded "Rotating Group Circuit" as
                // their current/running program before it was reworked to Circuit
                // mode would otherwise be stuck forever with the old broken copy -
                // MODE SHARED with no timing at all before the stations, which is
                // exactly the "EMPTY (0:00)" / nothing-to-edit bug. Detected by name
                // plus the absence of the new MODE CIRCUIT marker, then replaced with
                // the corrected script so it is fixed automatically on next launch.
                if (File.Exists(CurrentProgramFilePath))
                {
                    string current = File.ReadAllText(CurrentProgramFilePath);
                    if (current.IndexOf("Rotating Group Circuit", StringComparison.OrdinalIgnoreCase) >= 0
                        && current.IndexOf("MODE CIRCUIT", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        File.WriteAllText(CurrentProgramFilePath, RotatingGroupCircuit);
                    }
                }
            }
            catch
            {
                // Never let a missing/locked appdata folder stop the app from starting.
            }
        }

        private static void WriteIfMissing(string path, string content)
        {
            if (File.Exists(path)) return;
            File.WriteAllText(path, content);
        }

        /// <summary>Reads the current startup program, or an empty script if none has been saved yet.</summary>
        public static string LoadCurrentProgramScript()
        {
            try
            {
                if (File.Exists(CurrentProgramFilePath)) return File.ReadAllText(CurrentProgramFilePath);
            }
            catch { }
            return string.Empty;
        }

        /// <summary>Saves the program that should load next time the app starts.</summary>
        public static void SaveCurrentProgramScript(string script)
        {
            try
            {
                if (!Directory.Exists(RootFolder)) Directory.CreateDirectory(RootFolder);
                File.WriteAllText(CurrentProgramFilePath, script ?? string.Empty);
            }
            catch
            {
                // A locked/unwritable appdata folder should never crash the settings save.
            }
        }

        private const string IntervalCardioCircuit =
@"PROGRAM ""45/15 Interval Cardio Circuit""
MODE SEQUENTIAL

BETWEEN
    MOVE 60 ""Move to the next station""
END BETWEEN

STATION ""Treadmill""
REPEAT 8
    WORK 45
    REST 15
END
END STATION

STATION ""Exercise Cycle""
REPEAT 8
    WORK 45
    REST 15
END
END STATION

STATION ""Skipping""
REPEAT 8
    WORK 45
    REST 15
END
END STATION

STATION ""Step-Ups""
REPEAT 8
    WORK 45
    REST 15
END
END STATION

STATION ""Battle Ropes""
REPEAT 8
    WORK 45
    REST 15
END
END STATION
";

        private const string PyramidCardioCircuit =
@"PROGRAM ""Pyramid Cardio Circuit""
MODE SEQUENTIAL

BETWEEN
    MOVE 60 ""Move to the next station""
END BETWEEN

STATION ""Burpees""
REPEAT 2
    WORK 20
    REST 10
    WORK 30
    REST 15
    WORK 40
    REST 20
    WORK 50
    REST 25
    WORK 60
    REST 30
    WORK 50
    REST 25
    WORK 40
    REST 20
    WORK 30
    REST 15
    WORK 20
    REST 10
END
END STATION

STATION ""Mountain Climbers""
REPEAT 2
    WORK 20
    REST 10
    WORK 30
    REST 15
    WORK 40
    REST 20
    WORK 50
    REST 25
    WORK 60
    REST 30
    WORK 50
    REST 25
    WORK 40
    REST 20
    WORK 30
    REST 15
    WORK 20
    REST 10
END
END STATION

STATION ""Squat Jumps""
REPEAT 2
    WORK 20
    REST 10
    WORK 30
    REST 15
    WORK 40
    REST 20
    WORK 50
    REST 25
    WORK 60
    REST 30
    WORK 50
    REST 25
    WORK 40
    REST 20
    WORK 30
    REST 15
    WORK 20
    REST 10
END
END STATION

STATION ""Skater Bounds""
REPEAT 2
    WORK 20
    REST 10
    WORK 30
    REST 15
    WORK 40
    REST 20
    WORK 50
    REST 25
    WORK 60
    REST 30
    WORK 50
    REST 25
    WORK 40
    REST 20
    WORK 30
    REST 15
    WORK 20
    REST 10
END
END STATION

STATION ""Step-Ups""
REPEAT 2
    WORK 20
    REST 10
    WORK 30
    REST 15
    WORK 40
    REST 20
    WORK 50
    REST 25
    WORK 60
    REST 30
    WORK 50
    REST 25
    WORK 40
    REST 20
    WORK 30
    REST 15
    WORK 20
    REST 10
END
END STATION
";

        private const string WholeGroupHiit =
@"PROGRAM ""Whole Group HIIT""
MODE SEQUENTIAL

BETWEEN
    MOVE 60 ""Move to the next station""
END BETWEEN

STATION ""Shuttle Runs""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION

STATION ""Bodyweight Squats""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION

STATION ""Skipping""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION

STATION ""Step-Ups""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION

STATION ""Mountain Climbers""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION

STATION ""Skater Bounds""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION

STATION ""Burpees""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION

STATION ""High Knees""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION
";

        /// <summary>
        /// Eight groups, one per station, all doing the exact same 45/15 x 4
        /// work in unison, then everybody moves clockwise to the next station
        /// together. This is Circuit mode: the 4x WORK/REST timing is set ONCE
        /// (as the shared timing) rather than copied into every station, and
        /// the "move clockwise" message is the shared move-between-stations
        /// block, so the program rotates through all eight stations with every
        /// station sharing identical, single-edit timing.
        /// </summary>
        private const string RotatingGroupCircuit =
@"PROGRAM ""Rotating Group Circuit""
MODE CIRCUIT

REPEAT 4
    WORK 45
    REST 15
END

BETWEEN
    MOVE 20 ""Move clockwise to the next station""
END BETWEEN

STATION ""Station 1 - Kettlebell Swings""
COLOUR #E53935
END STATION

STATION ""Station 2 - Box Jumps""
COLOUR #FB8C00
END STATION

STATION ""Station 3 - Battle Ropes""
COLOUR #FDD835
END STATION

STATION ""Station 4 - Medicine Ball Slams""
COLOUR #43A047
END STATION

STATION ""Station 5 - Rowing Machine""
COLOUR #1E88E5
END STATION

STATION ""Station 6 - Assault Bike""
COLOUR #3949AB
END STATION

STATION ""Station 7 - TRX Rows""
COLOUR #8E24AA
END STATION

STATION ""Station 8 - Sled Push""
COLOUR #D81B60
END STATION
";

        /// <summary>
        /// Clear example of Shared Timing mode: everybody in the room does the
        /// exact same Tabata countdown at the same time, with each on-screen
        /// "station" just naming a different exercise for a sub-group to do
        /// during that identical countdown - nobody moves, nothing rotates,
        /// and the station list is purely informational labels.
        /// </summary>
        private const string TeamTabataBlast =
@"PROGRAM ""Team Tabata Blast""
MODE SHARED

REPEAT 8
    WORK 20
    REST 10
END

STATION ""Squats""
COLOUR #E53935
END STATION

STATION ""Push-Ups""
COLOUR #FB8C00
END STATION

STATION ""Sit-Ups""
COLOUR #43A047
END STATION

STATION ""Lunges""
COLOUR #1E88E5
END STATION
";

        /// <summary>
        /// Clear example of Parallel Independent mode: every station runs its
        /// own pattern, at its own length, completely independently - they all
        /// start together but each finishes at a different time, which is
        /// exactly what tells this mode apart from Shared or Sequential.
        /// </summary>
        private const string MixedPaceStations =
@"PROGRAM ""Mixed Pace Stations""
MODE PARALLEL

STATION ""Tabata Burpees""
COLOUR #E53935
REPEAT 8
    WORK 20
    REST 10
END
END STATION

STATION ""Steady Row""
COLOUR #1E88E5
REPEAT 1
    WORK 600
END
END STATION

STATION ""Pyramid Kettlebell Swings""
COLOUR #43A047
REPEAT 1
    WORK 20
    REST 10
    WORK 30
    REST 15
    WORK 40
    REST 20
    WORK 30
    REST 15
    WORK 20
    REST 10
END
END STATION
";
    }
}
