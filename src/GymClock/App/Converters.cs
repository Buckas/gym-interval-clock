using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GymClock.Wpf
{
    /// <summary>
    /// true -> Visible, false -> Collapsed (or the other way round with <see cref="Invert"/>).
    /// Declared in Controls.xaml as VisibleWhenTrue and VisibleWhenFalse.
    /// </summary>
    public sealed class BoolToVisibility : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool on = value is bool b && b;
            if (Invert) on = !on;
            return on ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible = value is Visibility v && v == Visibility.Visible;
            return Invert ? !visible : visible;
        }
    }
}
