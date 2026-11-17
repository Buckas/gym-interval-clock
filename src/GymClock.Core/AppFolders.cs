using System;
using System.IO;

namespace GymClock
{
    /// <summary>
    /// Where the app keeps its files on this PC: %AppData%\GymClock. Every file the app writes
    /// (settings, the current program, the licence, the usage log, the exercise library) is
    /// found through <see cref="Data"/>, so the tests can send all of it to a throw-away folder
    /// with one call and never touch the files of whoever is running them.
    /// </summary>
    public static class AppFolders
    {
        private static string _override;

        /// <summary>%AppData%\GymClock, or the folder the tests asked for.</summary>
        public static string Data
        {
            get
            {
                return _override ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GymClock");
            }
        }

        /// <summary>True while the tests have sent the app's files somewhere else.</summary>
        public static bool IsRedirected
        {
            get { return _override != null; }
        }

        /// <summary>
        /// Tests only: keep every file under this folder instead of %AppData%\GymClock.
        /// Pass null to go back to the real folder.
        /// </summary>
        public static void UseFolderForTesting(string folder)
        {
            _override = string.IsNullOrWhiteSpace(folder) ? null : folder;
        }
    }
}
