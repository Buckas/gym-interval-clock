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

    /// <summary>A status line's state to its colour: good green, waiting amber, problem red, off muted. Declared as LineStateBrush.</summary>
    public sealed class LineStateToBrush : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key;
            switch (value is LineState s ? s : LineState.Off)
            {
                case LineState.Good: key = "Success"; break;
                case LineState.Problem: key = "Danger"; break;
                case LineState.Waiting: key = "Warning.SoftText"; break;
                default: key = "Text.Muted"; break;
            }
            return Application.Current != null ? Application.Current.TryFindResource(key) : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
