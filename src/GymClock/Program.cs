using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using GymClock.Wpf;

namespace GymClock
{
    internal static class Program
    {
        /// <summary>
        /// Since version 5 the clock is a WPF window and WPF runs the message loop. The settings,
        /// program and licence windows are still Windows Forms until phases 4 and 5 replace them,
        /// so Windows Forms is set up first, as before.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Keeps text crisp on high-DPI laptops and projectors, for both kinds of window.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            // Typing into any Windows Forms window while WPF runs the message loop.
            WindowsFormsHost.EnableWindowsFormsInterop();

            WpfHost.EnsureApplication();

            if (!PassesLicenceCheck()) return;

            ProgramLibrary.EnsureInstalled();

            DisplayWindow clock = new DisplayWindow();
            System.Windows.Application.Current.MainWindow = clock;
            System.Windows.Application.Current.Run(clock);

            // The clock saved its settings, the usage log and the link as it closed. The local AI
            // (Foundry Local) is then asked to stop, but its native threads can keep the process
            // alive, invisibly, holding the link ports - and even Environment.Exit can hang on
            // them (found in Preview 2 and 3, 17 Nov 2026). So the process is ended directly.
            GymClock.AI.AiAssistantRuntime.Shutdown(TimeSpan.FromSeconds(2));
            TerminateProcess(GetCurrentProcess(), 0);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

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
