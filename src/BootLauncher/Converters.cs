using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BootLauncher
{
    /// <summary>null / empty string / false → Collapsed. ConverterParameter="Invert" reverses.</summary>
    public sealed class VisibleIfConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible;
            if (value is bool b) visible = b;
            else if (value is string s) visible = !string.IsNullOrWhiteSpace(s);
            else if (value is int n) visible = n > 0;
            else visible = value != null;
            if (parameter as string == "Invert") visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
    }
}
