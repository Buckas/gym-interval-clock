using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace GymClock.Wpf
{
    /// <summary>
    /// One Control page in a window of its own, shown as a dialog. Used where the Control window
    /// cannot be: from the old program editor (a Windows Forms dialog blocks every other window
    /// while it is open), and for the licence check before the clock starts. Esc closes it
    /// unless <see cref="Blocking"/> is set.
    /// </summary>
    public sealed class PageWindow : Window
    {
        private readonly InfoBarHost _infoBar = new InfoBarHost();

        public PageWindow(string title, double width, double height)
        {
            Title = title;
            Width = width;
            Height = height;
            MinWidth = 520;
            MinHeight = 400;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UseLayoutRounding = true;
            SetResourceReference(BackgroundProperty, "Surface.App");
            SetResourceReference(ForegroundProperty, "Text.Primary");
            SetResourceReference(FontFamilyProperty, "Font.Control");
            FontSize = 14;
            Dialogs = new DialogService(() => this, (m, k) => _infoBar.Show(m, k));
        }

        /// <summary>The dialog service for the page shown in this window.</summary>
        public IDialogService Dialogs { get; }

        /// <summary>True: Esc does nothing and there is no way out but the page's own buttons (the licence check).</summary>
        public bool Blocking { get; set; }

        /// <summary>Shows the page (a view whose DataContext is already set) and waits until it closes.</summary>
        public bool? ShowPage(FrameworkElement page, Forms.IWin32Window owner)
        {
            DockPanel root = new DockPanel();
            DockPanel.SetDock(_infoBar, Dock.Top);
            root.Children.Add(_infoBar);
            root.Children.Add(page);
            Content = root;

            if (owner != null)
            {
                new WindowInteropHelper(this).Owner = owner.Handle;
                Topmost = true;      // the clock, and the old editor over it, are normally always on top
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ShowInTaskbar = true;
            }
            return ShowDialog();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !Blocking)
            {
                e.Handled = true;
                Close();
                return;
            }
            base.OnPreviewKeyDown(e);
        }
    }
}
