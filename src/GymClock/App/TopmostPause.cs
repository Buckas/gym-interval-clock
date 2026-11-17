using System.Collections.Generic;
using System.Windows;

namespace GymClock.Wpf
{
    /// <summary>
    /// The clock and the app's windows are normally always on top, which would hide a Windows
    /// screen the app opens (Bluetooth settings). Pause drops always-on-top from every window of
    /// the app; Resume puts it back on the ones that had it.
    /// </summary>
    public static class TopmostPause
    {
        private static readonly List<Window> Paused = new List<Window>();

        public static void Pause()
        {
            if (Application.Current == null) return;
            foreach (Window window in Application.Current.Windows)
            {
                if (!window.Topmost) continue;
                Paused.Add(window);
                window.Topmost = false;
            }
        }

        public static void Resume()
        {
            foreach (Window window in Paused)
            {
                if (window.IsLoaded) window.Topmost = true;
            }
            Paused.Clear();
        }
    }
}
