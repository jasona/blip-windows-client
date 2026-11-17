using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Blip.Core.Live;

namespace Blip.Windows;

/// <summary>The sidebar's connection dot.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public static readonly StatusBrushConverter Instance = new();

    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        BridgeStatus.Online => new SolidColorBrush(Color.Parse("#30C759")),
        BridgeStatus.Offline => Brushes.Gray,
        BridgeStatus.NeedsAttention => new SolidColorBrush(Color.Parse("#FF453A")),
        _ => Brushes.Orange,
    };

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class FileNameConverter : IValueConverter
{
    public static readonly FileNameConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) => Path.GetFileName(value as string ?? "");
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
