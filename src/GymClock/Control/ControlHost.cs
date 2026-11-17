using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace GymClock.Wpf
{
    /// <summary>
    /// Opens the Control window in the right place (build guide P2.5):
    /// - two or more screens: a normal window on a screen other than the clock's, where it was
    ///   last time if that was on such a screen, otherwise 1280 x 800 in the middle;
    /// - one screen: a side panel over the right 40% of the clock's screen (mockup 3), with
    ///   "Full window" to turn it into a normal window and Esc to close it.
    /// There is only ever one Control window. All positions here are in screen pixels.
    /// </summary>
    public static class ControlHost
    {
        private const double PanelShare = 0.40;

        private static Forms.Form _clock;
        private static AppSession _session;
        private static ControlWindow _window;
        private static Rectangle _lastNormal;

        /// <summary>Called once by the clock window, with the session the pages will use.</summary>
        public static void Attach(Forms.Form clock, AppSession session)
        {
            _clock = clock;
            _session = session;
            WpfHost.EnsureApplication();
            ThemeService.Apply(session.OwnSettings.Theme);
        }

        public static bool IsOpen
        {
            get { return _window != null; }
        }

        /// <summary>The window, while it is open (for tests).</summary>
        public static ControlWindow Window
        {
            get { return _window; }
        }

        /// <summary>Opens the Control window at a page ("session", "builder", "link" and so on), or brings it forward.</summary>
        public static void Open(string pageId)
        {
            if (_clock == null || _clock.IsDisposed) return;
            WpfHost.EnsureApplication();

            if (_window != null)
            {
                _window.Model.Navigate(pageId);
                if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                _window.Activate();
                return;
            }

            ControlViewModel model = new ControlViewModel();
            model.Navigate(pageId);
            ControlWindow window = new ControlWindow(model);
            new WindowInteropHelper(window).Owner = _clock.Handle;   // stays above the clock, closes with it
            model.FullWindowRequested += ToFullWindow;
            model.CloseRequested += () => window.Close();
            window.LocationChanged += delegate { RememberNormalBounds(); };
            window.SizeChanged += delegate { RememberNormalBounds(); };
            window.Closing += delegate { SaveBounds(); };
            window.Closed += delegate { _window = null; };
            _window = window;
            _lastNormal = Rectangle.Empty;

            Forms.Screen clockScreen = Forms.Screen.FromControl(_clock);
            Forms.Screen other = OtherScreen(clockScreen);
            if (other != null) PlaceAsWindow(other);
            else PlaceAsPanel(clockScreen);

            WpfHost.ShowModeless(window);
            window.Activate();
        }

        /// <summary>Closes the Control window if it is open (the clock is closing).</summary>
        public static void CloseWindow()
        {
            if (_window != null) _window.Close();
        }

        // ------------------------------------------------------------ placement

        private static void ToFullWindow()
        {
            if (_window == null || _clock == null) return;
            Forms.Screen clockScreen = Forms.Screen.FromControl(_clock);
            PlaceAsWindow(OtherScreen(clockScreen) ?? clockScreen);
            _window.Activate();
        }

        private static void PlaceAsWindow(Forms.Screen target)
        {
            ControlWindow w = _window;
            w.Model.IsPanel = false;
            w.WindowStyle = WindowStyle.SingleBorderWindow;
            w.ResizeMode = ResizeMode.CanResize;
            w.ShowInTaskbar = true;
            w.Topmost = false;     // it is owned by the clock, so it stays above it anyway
            w.MinWidth = 900;
            w.MinHeight = 600;

            Rectangle saved;
            bool maximised;
            bool useSaved = TryParseBounds(_session != null ? _session.OwnSettings.ControlBounds : null, out saved, out maximised)
                && IsMostlyOn(saved, target.WorkingArea);
            Rectangle bounds = useSaved ? saved : Centred(target, 1280, 800);

            SetPixelBounds(w, bounds);
            _lastNormal = bounds;
            if (useSaved && maximised) w.WindowState = WindowState.Maximized;
        }

        private static void PlaceAsPanel(Forms.Screen screen)
        {
            ControlWindow w = _window;
            w.Model.IsPanel = true;
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            w.MinWidth = 0;
            w.MinHeight = 0;
            SetPixelBounds(w, PanelBounds(screen.Bounds));
        }

        /// <summary>The side panel: the right 40% of the screen, full height.</summary>
        public static Rectangle PanelBounds(Rectangle screen)
        {
            int width = (int)Math.Round(screen.Width * PanelShare);
            return new Rectangle(screen.Right - width, screen.Top, width, screen.Height);
        }

        /// <summary>A window of the given size in WPF units, centred on the screen's working area and kept inside it.</summary>
        private static Rectangle Centred(Forms.Screen screen, int widthDip, int heightDip)
        {
            double scale = DpiFor(screen) / 96.0;
            Rectangle area = screen.WorkingArea;
            int width = Math.Min((int)(widthDip * scale), area.Width - 40);
            int height = Math.Min((int)(heightDip * scale), area.Height - 40);
            return new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        }

        private static Forms.Screen OtherScreen(Forms.Screen clockScreen)
        {
            foreach (Forms.Screen s in Forms.Screen.AllScreens)
            {
                if (s.DeviceName != clockScreen.DeviceName) return s;
            }
            return null;
        }

        /// <summary>True when at least 100 x 100 pixels of the rectangle are on the area.</summary>
        public static bool IsMostlyOn(Rectangle bounds, Rectangle area)
        {
            Rectangle overlap = Rectangle.Intersect(bounds, area);
            return overlap.Width >= 100 && overlap.Height >= 100;
        }

        // ------------------------------------------------------------ remembering

        private static void RememberNormalBounds()
        {
            ControlWindow w = _window;
            if (w == null || w.Model.IsPanel || w.WindowState != WindowState.Normal) return;
            IntPtr handle = new WindowInteropHelper(w).Handle;
            RECT r;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out r))
                _lastNormal = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        private static void SaveBounds()
        {
            ControlWindow w = _window;
            if (w == null || _session == null || w.Model.IsPanel || _lastNormal.Width <= 0) return;
            _session.OwnSettings.ControlBounds = FormatBounds(_lastNormal, w.WindowState == WindowState.Maximized);
            _session.OwnSettings.Save();
        }

        /// <summary>"x,y,width,height" in screen pixels, plus ",max" when maximised.</summary>
        public static string FormatBounds(Rectangle bounds, bool maximised)
        {
            string text = string.Join(",",
                bounds.X.ToString(CultureInfo.InvariantCulture), bounds.Y.ToString(CultureInfo.InvariantCulture),
                bounds.Width.ToString(CultureInfo.InvariantCulture), bounds.Height.ToString(CultureInfo.InvariantCulture));
            return maximised ? text + ",max" : text;
        }

        public static bool TryParseBounds(string text, out Rectangle bounds, out bool maximised)
        {
            bounds = Rectangle.Empty;
            maximised = false;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split(',');
            if (parts.Length < 4) return false;
            int[] n = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return false;
            }
            if (n[2] <= 0 || n[3] <= 0) return false;
            bounds = new Rectangle(n[0], n[1], n[2], n[3]);
            maximised = parts.Length > 4 && parts[4].Trim().Equals("max", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        // ------------------------------------------------------------ Win32

        private static void SetPixelBounds(Window window, Rectangle r)
        {
            IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
            SetWindowPos(handle, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private static uint DpiFor(Forms.Screen screen)
        {
            try
            {
                Rectangle b = screen.Bounds;
                IntPtr monitor = MonitorFromPoint(new POINT { X = b.X + b.Width / 2, Y = b.Y + b.Height / 2 }, MONITOR_DEFAULTTONEAREST);
                uint x, y;
                if (GetDpiForMonitor(monitor, 0, out x, out y) == 0 && x > 0) return x;
            }
            catch { }
            return 96;
        }

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
