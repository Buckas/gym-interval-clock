using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace GymClock.Wpf
{
    /// <summary>
    /// Window placement in screen pixels. WPF's Left, Top, Width and Height are in scaled units
    /// and go wrong across screens with different scaling, so the clock and the Control window
    /// are placed with SetWindowPos instead.
    /// </summary>
    public static class NativeScreen
    {
        /// <summary>The window's handle, made now if the window has not been shown yet.</summary>
        public static IntPtr Handle(Window window)
        {
            return new WindowInteropHelper(window).EnsureHandle();
        }

        /// <summary>The screen the window is (mostly) on.</summary>
        public static Forms.Screen ScreenOf(Window window)
        {
            return Forms.Screen.FromHandle(Handle(window));
        }

        public static void SetBounds(Window window, Rectangle r)
        {
            SetWindowPos(Handle(window), IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public static Rectangle GetBounds(Window window)
        {
            RECT r;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out r)) return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return Rectangle.Empty;
        }

        /// <summary>A window of the given size in WPF units, centred on the screen's working area and kept inside it, in pixels.</summary>
        public static Rectangle Centred(Forms.Screen screen, int widthDip, int heightDip)
        {
            double scale = DpiFor(screen) / 96.0;
            Rectangle area = screen.WorkingArea;
            int width = Math.Min((int)(widthDip * scale), area.Width - 40);
            int height = Math.Min((int)(heightDip * scale), area.Height - 40);
            return new Rectangle(area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2), width, height);
        }

        public static uint DpiFor(Forms.Screen screen)
        {
            try
            {
                Rectangle b = screen.Bounds;
                IntPtr monitor = MonitorFromPoint(new POINT { X = b.X + (b.Width / 2), Y = b.Y + (b.Height / 2) }, MONITOR_DEFAULTTONEAREST);
                uint x, y;
                if (GetDpiForMonitor(monitor, 0, out x, out y) == 0 && x > 0) return x;
            }
            catch { }
            return 96;
        }

        /// <summary>A Windows Forms owner for the old dialogs, so they open over (and block) a WPF window.</summary>
        public static Forms.IWin32Window Owner(Window window)
        {
            return new Win32Owner(Handle(window));
        }

        private sealed class Win32Owner : Forms.IWin32Window
        {
            public Win32Owner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
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
