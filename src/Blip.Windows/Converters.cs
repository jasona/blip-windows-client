using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Blip.Windows;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Collapsed for null / "" / empty collection. ConverterParameter=invert flips it (empty-state placeholders).</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var empty = value is null || value is string { Length: 0 } || value is System.Collections.ICollection { Count: 0 };
        if (p is "invert") empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString((string)value)); }
        catch { return Brushes.Gray; }
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Packs (message, kind) for the tapback menu's command parameter.</summary>
public sealed class PairConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c) => values.Clone();
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class FileNameConverter : IValueConverter
{
    public static readonly FileNameConverter Instance = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => Path.GetFileName(value as string ?? "");
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
