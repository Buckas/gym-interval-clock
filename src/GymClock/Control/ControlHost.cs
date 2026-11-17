using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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

        private static Window _clock;
        private static IDisplayActions _display;
        private static Func<UpdateInfo> _update;
        private static AppSession _session;
        private static ControlWindow _window;
        private static Rectangle _lastNormal;

        /// <summary>Called once by the clock window, with the session the pages will use.</summary>
        /// <param name="update">A newer version, if the background check has seen one (for About).</param>
        public static void Attach(Window clock, IDisplayActions display, AppSession session, Func<UpdateInfo> update)
        {
            _clock = clock;
            _display = display;
            _update = update;
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
            if (_clock == null) return;
            WpfHost.EnsureApplication();

            if (_window != null)
            {
                _window.Model.Navigate(pageId);
                if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                _window.Activate();
                return;
            }

            IDialogService dialogs = new DialogService(() => _window, (message, kind) => { if (_window != null) _window.Notify(message, kind); });
            Dictionary<string, Func<object>> pages = new Dictionary<string, Func<object>>
            {
                [ControlViewModel.SessionPage] = () => new SessionPageView(new SessionViewModel(_session, _display)),
                [ControlViewModel.SettingsPage] = () => SettingsPage(_session, dialogs),
                [ControlViewModel.LinkPage] = () => LinkPage(_session, dialogs),
                [ControlViewModel.AboutPage] = () => new AboutPageView(new AboutViewModel(dialogs, false, () => _session.Settings, _update)),
            };
            ControlViewModel model = new ControlViewModel(pages);
            model.Navigate(pageId);
            ControlWindow window = new ControlWindow(model);
            new WindowInteropHelper(window).Owner = NativeScreen.Handle(_clock);   // stays above the clock, closes with it
            model.FullWindowRequested += ToFullWindow;
            model.CloseRequested += () => window.Close();
            window.LocationChanged += delegate { RememberNormalBounds(); };
            window.SizeChanged += delegate { RememberNormalBounds(); };
            window.Closing += delegate { SaveBounds(); };
            window.Closed += delegate { _window = null; };
            _window = window;
            _lastNormal = Rectangle.Empty;

            Forms.Screen clockScreen = NativeScreen.ScreenOf(_clock);
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

        /// <summary>Esc on the clock: closes the side panel if it is open. True if it did.</summary>
        public static bool ClosePanel()
        {
            if (_window == null || !_window.Model.IsPanel) return false;
            _window.Close();
            return true;
        }

        /// <summary>Opens About; with <paramref name="showRequest"/>, at the licence request form (the renewal offer).</summary>
        public static void OpenAbout(bool showRequest)
        {
            Open(ControlViewModel.AboutPage);
            AboutPageView about = _window != null ? _window.Model.CurrentView as AboutPageView : null;
            if (about != null && showRequest) about.ShowRequest();
        }

        // ------------------------------------------------------------ pages shared with stand-alone windows

        /// <summary>The Settings page for this PC: every change is applied to the clock at once and saved.</summary>
        public static SettingsPageView SettingsPage(AppSession session, IDialogService dialogs)
        {
            SettingsViewModel model = new SettingsViewModel(session.OwnSettings, edited => ApplyGeneral(session, edited), dialogs);
            return new SettingsPageView(model, () => session.OwnSettings);
        }

        /// <summary>Takes the general settings (the Settings page's) from <paramref name="edited"/>; the program and the link are kept.</summary>
        public static void ApplyGeneral(AppSession session, TimerSettings edited)
        {
            TimerSettings next = session.OwnSettings.Clone();
            next.CopyGeneralFrom(edited);
            session.ReplaceSettings(next, reset: false);
        }

        /// <summary>The Link displays page: every choice takes effect at once, through the link controller.</summary>
        public static LinkPageView LinkPage(AppSession session, IDialogService dialogs)
        {
            return new LinkPageView(new LinkViewModel(session.OwnSettings, session.Link.GetStatus, session.Link.ApplyChange, dialogs));
        }

        // ------------------------------------------------------------ placement

        private static void ToFullWindow()
        {
            if (_window == null || _clock == null) return;
            Forms.Screen clockScreen = NativeScreen.ScreenOf(_clock);
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
            Rectangle bounds = useSaved ? saved : NativeScreen.Centred(target, 1280, 800);

            NativeScreen.SetBounds(w, bounds);
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
            NativeScreen.SetBounds(w, PanelBounds(screen.Bounds));
        }

        /// <summary>The side panel: the right 40% of the screen, full height.</summary>
        public static Rectangle PanelBounds(Rectangle screen)
        {
            int width = (int)Math.Round(screen.Width * PanelShare);
            return new Rectangle(screen.Right - width, screen.Top, width, screen.Height);
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
            Rectangle r = NativeScreen.GetBounds(w);
            if (r.Width > 0) _lastNormal = r;
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
    }
}
