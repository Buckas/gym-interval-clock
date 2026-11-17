using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GymClock.Wpf
{
    /// <summary>
    /// The bar at the top of the page area that shows a short message (P4.1): "Saved", "That key
    /// was not accepted" and so on. Goes by itself after 6 seconds (errors after 10), or with ✕.
    /// </summary>
    public sealed class InfoBarHost : ContentControl
    {
        private readonly DispatcherTimer _hide = new DispatcherTimer();
        private readonly TextBlock _text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _bar = new Border();

        public InfoBarHost()
        {
            Focusable = false;
            Visibility = Visibility.Collapsed;
            Margin = new Thickness(32, 16, 32, 0);

            Button close = new Button { Content = Icons.Close, Width = 32, MinWidth = 32, MinHeight = 32, Height = 32, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Close" };
            close.SetResourceReference(StyleProperty, "Button.Subtle");
            close.SetResourceReference(FontFamilyProperty, "IconFontFamily");
            close.FontSize = 11;
            System.Windows.Automation.AutomationProperties.SetName(close, "Close the message");
            close.Click += delegate { Hide(); };

            DockPanel row = new DockPanel();
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            row.Children.Add(_text);
            _bar.Child = row;
            Content = _bar;

            _hide.Tick += delegate { Hide(); };
        }

        /// <summary>The message showing, or empty (for tests).</summary>
        public string Message
        {
            get { return Visibility == Visibility.Visible ? _text.Text : string.Empty; }
        }

        public void Show(string message, InfoKind kind)
        {
            if (string.IsNullOrEmpty(message)) { Hide(); return; }
            _text.Text = message;
            _bar.SetResourceReference(StyleProperty, "InfoBar." + kind);
            System.Windows.Automation.AutomationProperties.SetLiveSetting(_text, System.Windows.Automation.AutomationLiveSetting.Polite);
            Visibility = Visibility.Visible;
            _hide.Stop();
            _hide.Interval = TimeSpan.FromSeconds(kind == InfoKind.Error ? 10 : 6);
            _hide.Start();
        }

        public void Hide()
        {
            _hide.Stop();
            Visibility = Visibility.Collapsed;
        }
    }
}
