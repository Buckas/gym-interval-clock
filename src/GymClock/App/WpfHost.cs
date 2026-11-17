using System.Windows;
using System.Windows.Forms.Integration;

namespace GymClock.Wpf
{
    /// <summary>
    /// Lets the new WPF windows live inside the Windows Forms app while version 5 is being built.
    /// There is one WPF <see cref="Application"/> for the whole app; it never shuts the app down
    /// itself (the old clock window still does that), and it holds the theme resources.
    /// </summary>
    public static class WpfHost
    {
        /// <summary>Creates the WPF application and loads the theme, once. Safe to call again.</summary>
        public static void EnsureApplication()
        {
            if (Application.Current != null) return;

            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ThemeService.Initialise(app);
        }

        /// <summary>
        /// Shows a modeless WPF window. Since version 5 Phase 3 the app runs WPF's message loop,
        /// so this is a plain Show; the keyboard interop is only needed (and only added) if a
        /// Windows Forms message loop is running instead. Call once per window.
        /// </summary>
        public static void ShowModeless(Window window)
        {
            if (System.Windows.Forms.Application.MessageLoop) ElementHost.EnableModelessKeyboardInterop(window);
            window.Show();
        }
    }
}
