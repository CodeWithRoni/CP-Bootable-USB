using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CP.Shared;
using UsbMaker.Models;

namespace UsbMaker;

/// <summary>"#0284C7" → SolidColorBrush; invalid text → transparent.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && ConfigService.IsValidColor(s))
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Trim())); }
            catch { /* fall through */ }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// File path → BitmapImage loaded into memory (does not lock the file).
/// null / missing file → the built-in COMPUTER PLUS shop logo.
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    private static readonly Lazy<BitmapImage> BuiltInLogo = new(() =>
    {
        var bmp = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute));
        bmp.Freeze();
        return bmp;
    });

    public static ImageSource DefaultLogo => BuiltInLogo.Value;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return BuiltInLogo.Value;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelHeight = 256;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return BuiltInLogo.Value; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>null/empty → Collapsed. ConverterParameter="Invert" reverses it.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true
        };
        if (parameter as string == "Invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
}

/// <summary>Colours log lines by level.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LogLevel.Success => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
        LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        LogLevel.Error => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        LogLevel.Detail => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
        _ => new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1))
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
