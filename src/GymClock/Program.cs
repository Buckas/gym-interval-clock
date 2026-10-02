using System;
using System.Windows.Forms;

namespace GymClock
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

#if NET5_0_OR_GREATER
            // Keeps the big digits crisp on high-DPI laptops and projectors.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#endif

            if (!PassesLicenceCheck()) return;

            ProgramLibrary.EnsureInstalled();

            Application.Run(new MainForm());
        }

        /// <summary>
        /// Runs before any window is created. If the free-use period has ended the
        /// user gets one chance to enter a licence key, otherwise the app exits.
        /// </summary>
        private static bool PassesLicenceCheck()
        {
            LicenceStatus status = Licensing.Current;
            if (status.CanRun) return true;

            using (LicenceDialog dialog = new LicenceDialog(status, true))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return false;
            }

            return Licensing.Refresh().CanRun;
        }
    }
}
