using System.Windows;

namespace GymClock.Wpf
{
    /// <summary>
    /// Small attached settings the styles read.
    /// Ui.CornerRadius rounds a styled button: 8 by default, 12 for the big Session transport
    /// buttons, for example &lt;Button Style="{StaticResource Button.Primary}" local:Ui.CornerRadius="12" /&gt;.
    /// Ui.InPanel is true inside the Control window while it is the side panel; it is inherited, so
    /// a page can read it. TextStyle.PageTitle uses it to hide the page title (the panel header shows it).
    /// </summary>
    public static class Ui
    {
        public static readonly DependencyProperty InPanelProperty = DependencyProperty.RegisterAttached(
            "InPanel", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static bool GetInPanel(DependencyObject element)
        {
            return (bool)element.GetValue(InPanelProperty);
        }

        public static void SetInPanel(DependencyObject element, bool value)
        {
            element.SetValue(InPanelProperty, value);
        }

        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
            "CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(8)));

        public static CornerRadius GetCornerRadius(DependencyObject element)
        {
            return (CornerRadius)element.GetValue(CornerRadiusProperty);
        }

        public static void SetCornerRadius(DependencyObject element, CornerRadius value)
        {
            element.SetValue(CornerRadiusProperty, value);
        }
    }
}
