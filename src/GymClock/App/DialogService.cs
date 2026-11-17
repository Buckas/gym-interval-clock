using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;

namespace GymClock.Wpf
{
    /// <summary>
    /// The real <see cref="IDialogService"/>: small styled windows over the window the page is in
    /// (the Control window, or a page shown on its own), Windows' own file dialogs, and the info
    /// bar of that window.
    /// </summary>
    public sealed class DialogService : IDialogService
    {
        private readonly Func<Window> _owner;
        private readonly Action<string, InfoKind> _notify;

        /// <param name="owner">The window the dialogs open over (asked each time; may be null).</param>
        /// <param name="notify">Shows a message in that window's info bar.</param>
        public DialogService(Func<Window> owner, Action<string, InfoKind> notify)
        {
            _owner = owner;
            _notify = notify;
        }

        public bool Confirm(string title, string message, string yesText, string noText)
        {
            return Show(title, message, yesText, noText, null) == true;
        }

        public void Info(string title, string message)
        {
            Show(title, message, "OK", null, null);
        }

        public string Prompt(string title, string label, string initial)
        {
            TextBox box = new TextBox { Text = initial ?? string.Empty, Margin = new Thickness(0, 8, 0, 0) };
            box.Loaded += delegate { box.Focus(); box.SelectAll(); };
            return Show(title, label, "OK", "Cancel", box) == true ? box.Text : null;
        }

        public string PickOpenFile(string title, string filter)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
            return dialog.ShowDialog(_owner()) == true ? dialog.FileName : null;
        }

        public string PickSaveFile(string title, string filter, string suggestedName)
        {
            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = title, Filter = filter, FileName = suggestedName ?? string.Empty,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            return dialog.ShowDialog(_owner()) == true ? dialog.FileName : null;
        }

        public string PickColour(string title, string current)
        {
            ColourPicker picker = new ColourPicker(current);
            return Show(title, null, "OK", "Cancel", picker) == true ? picker.Hex : null;
        }

        public void Notify(string message, InfoKind kind)
        {
            if (_notify != null) _notify(message, kind);
        }

        public bool CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Open(string target, string arguments = null)
        {
            try
            {
                ProcessStartInfo info = arguments == null ? new ProcessStartInfo(target) : new ProcessStartInfo(target, arguments);
                info.UseShellExecute = true;
                Process.Start(info);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------ the dialog window

        /// <summary>A small card-like window: title, message, an optional control, and one or two buttons.</summary>
        private bool? Show(string title, string message, string yesText, string noText, FrameworkElement extra)
        {
            Window owner = _owner();
            Window dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                MinWidth = 360,
                MaxWidth = 560,
            };
            if (owner != null && owner.IsLoaded) dialog.Owner = owner;
            if (owner != null) dialog.Topmost = owner.Topmost;
            dialog.SetResourceReference(Control.BackgroundProperty, "Surface.App");
            dialog.SetResourceReference(Control.ForegroundProperty, "Text.Primary");
            dialog.SetResourceReference(Control.FontFamilyProperty, "Font.Control");
            dialog.FontSize = 14;

            StackPanel body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            TextBlock heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 10) };
            heading.SetResourceReference(FrameworkElement.StyleProperty, "TextStyle.Subtitle");
            body.Children.Add(heading);

            if (!string.IsNullOrEmpty(message))
            {
                TextBlock text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
                body.Children.Add(text);
            }
            if (extra != null) body.Children.Add(extra);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            Button yes = new Button { Content = yesText, IsDefault = true, MinWidth = 96 };
            yes.SetResourceReference(FrameworkElement.StyleProperty, "Button.Primary");
            yes.Click += delegate { dialog.DialogResult = true; };
            if (noText != null)
            {
                Button no = new Button { Content = noText, IsCancel = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
                no.SetResourceReference(FrameworkElement.StyleProperty, "Button.Standard");
                buttons.Children.Add(no);
            }
            else
            {
                yes.IsCancel = true;
            }
            buttons.Children.Add(yes);
            body.Children.Add(buttons);

            dialog.Content = body;
            return dialog.ShowDialog();
        }

        // ------------------------------------------------------------ colour picker

        /// <summary>24 swatches (the app's own phase colours first) and a #RRGGBB box.</summary>
        private sealed class ColourPicker : StackPanel
        {
            private static readonly string[] Swatches =
            {
                "#0F8A46", "#D9822B", "#1E63D0", "#1E9AA6", "#4A90D9", "#6C3FC5", "#17A2B8", "#546E7A",
                "#B23A78", "#5B4B9E", "#333B47", "#1A1E25", "#B3261E", "#E53935", "#F4511E", "#FB8C00",
                "#C0A000", "#7CB342", "#2E7D32", "#00897B", "#0277BD", "#283593", "#8E24AA", "#6D4C41",
            };

            private readonly TextBox _hex = new TextBox { Width = 120 };
            private readonly Border _preview = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), Margin = new Thickness(12, 0, 0, 0) };

            public ColourPicker(string current)
            {
                UniformGrid grid = new UniformGrid { Columns = 8, Margin = new Thickness(0, 4, 0, 12) };
                foreach (string swatch in Swatches)
                {
                    string value = swatch;
                    Button b = new Button
                    {
                        Width = 40, Height = 40, Margin = new Thickness(3), Padding = new Thickness(0), MinWidth = 0, MinHeight = 0,
                        Background = Brush(value), BorderThickness = new Thickness(0), ToolTip = value, Cursor = Cursors.Hand,
                    };
                    b.SetResourceReference(FrameworkElement.StyleProperty, "Button.Base");
                    AutomationProperties.SetName(b, "Colour " + value);
                    b.Click += delegate { _hex.Text = value; };
                    grid.Children.Add(b);
                }
                Children.Add(grid);

                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = "Colour code", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                row.Children.Add(_hex);
                row.Children.Add(_preview);
                Children.Add(row);

                _hex.TextChanged += delegate { _preview.Background = Brush(Normalise(_hex.Text) ?? "#000000"); };
                _hex.Text = Normalise(current) ?? "#0F8A46";
            }

            /// <summary>The chosen colour as #RRGGBB, or null if what is typed is not a colour.</summary>
            public string Hex
            {
                get { return Normalise(_hex.Text); }
            }

            private static SolidColorBrush Brush(string hex)
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
                catch { return new SolidColorBrush(Colors.Black); }
            }
        }

        /// <summary>"#rgb", "rrggbb", "#RRGGBB" or a colour name to "#RRGGBB"; null if it is not a colour.</summary>
        public static string Normalise(string text)
        {
            string t = (text ?? string.Empty).Trim();
            if (t.Length == 0) return null;
            if (!t.StartsWith("#", StringComparison.Ordinal) && (t.Length == 6 || t.Length == 3) && IsHex(t)) t = "#" + t;
            try
            {
                Color c = (Color)ColorConverter.ConvertFromString(t);
                return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsHex(string t)
        {
            foreach (char ch in t) if (!Uri.IsHexDigit(ch)) return false;
            return true;
        }
    }
}
