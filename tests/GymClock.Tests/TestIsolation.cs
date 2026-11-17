using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>
    /// Keeps the tests away from the real %AppData%\GymClock. The app saves its settings and
    /// current program as part of normal work (for example when a second screen connects), so a
    /// test that drives the app would otherwise overwrite the files of whoever runs the tests.
    /// That happened once: never remove this.
    /// </summary>
    internal static class TestIsolation
    {
        internal static readonly string Folder =
            Path.Combine(Path.GetTempPath(), "gymclock-tests-" + Guid.NewGuid().ToString("N"));

        /// <summary>Runs when the test assembly loads, before any test or fixture.</summary>
        [ModuleInitializer]
        internal static void Redirect()
        {
            Directory.CreateDirectory(Folder);
            AppFolders.UseFolderForTesting(Folder);
            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                try { Directory.Delete(Folder, true); } catch { }
            };
        }
    }

    public class TestIsolationTests
    {
        private static readonly string RealFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GymClock");

        [Fact]
        public void EveryFileTheAppWrites_GoesToTheTestFolder_NotTheRealOne()
        {
            Assert.True(AppFolders.IsRedirected);
            Assert.Equal(TestIsolation.Folder, AppFolders.Data);

            foreach (string path in new[]
            {
                TimerSettings.FilePath,
                ProgramLibrary.RootFolder,
                ProgramLibrary.CurrentProgramFilePath,
                ProgramLibrary.SamplesFolder,
                GymClock.AI.ExerciseCatalog.Folder
            })
            {
                Assert.False(path.StartsWith(RealFolder, StringComparison.OrdinalIgnoreCase),
                    "This path is inside the real GymClock folder: " + path);
            }
        }

        [Fact]
        public void SavingSettings_WritesInsideTheTestFolder()
        {
            TimerSettings settings = new TimerSettings();
            settings.Save();

            Assert.StartsWith(TestIsolation.Folder, TimerSettings.FilePath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(TimerSettings.FilePath));
        }
    }
}
