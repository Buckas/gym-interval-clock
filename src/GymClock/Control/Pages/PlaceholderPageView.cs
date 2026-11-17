using System.Windows;
using System.Windows.Controls;

namespace GymClock.Wpf
{
    /// <summary>
    /// Stands in for a Control page that has not been built yet: the title, and where that job is
    /// done in the meantime. Each is replaced by the real page in phases 4 to 6.
    /// </summary>
    public sealed class PlaceholderPageView : ScrollViewer
    {
        public PlaceholderPageView(string title, string phase, string today)
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;

            StackPanel page = new StackPanel { Margin = (Thickness)FindStyleResource("Padding.Page") };

            TextBlock heading = new TextBlock { Text = title };
            heading.SetResourceReference(StyleProperty, "TextStyle.PageTitle");
            page.Children.Add(heading);

            Border card = new Border { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
            card.SetResourceReference(StyleProperty, "Card");
            StackPanel inside = new StackPanel();

            Border tag = new Border { Margin = new Thickness(0, 0, 0, 12) };
            tag.SetResourceReference(StyleProperty, "Tag.Neutral");
            tag.Child = new TextBlock { Text = ("COMING IN " + phase).ToUpperInvariant() };
            inside.Children.Add(tag);

            TextBlock text = new TextBlock { Text = today };
            text.SetResourceReference(StyleProperty, "TextStyle.Body");
            inside.Children.Add(text);

            card.Child = inside;
            page.Children.Add(card);
            Content = page;
        }

        private static object FindStyleResource(string key)
        {
            object value = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return value ?? new Thickness(32, 28, 32, 28);
        }
    }
}
