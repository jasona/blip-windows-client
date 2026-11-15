using System.Globalization;

namespace Blip.Windows.ViewModels;

public static class Formatting
{
    public static string ListTime(DateTimeOffset t)
    {
        if (t == DateTimeOffset.MinValue) return "";
        var local = t.ToLocalTime();
        var today = DateTime.Today;
        if (local.Date == today) return local.ToString("t", CultureInfo.CurrentCulture);
        if (local.Date == today.AddDays(-1)) return "Yesterday";
        if (local.Date > today.AddDays(-7)) return local.ToString("dddd", CultureInfo.CurrentCulture);
        return local.ToString("d", CultureInfo.CurrentCulture);
    }

    public static string BubbleTime(DateTimeOffset t) => t.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

    public static string DayHeader(DateTimeOffset t)
    {
        var local = t.ToLocalTime();
        if (local.Date == DateTime.Today) return "Today " + BubbleTime(t);
        if (local.Date == DateTime.Today.AddDays(-1)) return "Yesterday " + BubbleTime(t);
        return local.ToString("ddd, MMM d", CultureInfo.CurrentCulture) + " at " + BubbleTime(t);
    }

    public static string Initials(string title)
    {
        var parts = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetter(p[0])).ToArray();
        return parts.Length switch
        {
            0 => "#",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    private static readonly string[] Palette = ["#E0533D", "#2F7DE1", "#2F9E63", "#8A4FD6", "#C9822A", "#3F6FB5", "#6B8F3A", "#A3563F", "#4A4F8C", "#1F8F7A"];

    public static string ColorFor(string id)
    {
        uint h = 2166136261;
        foreach (var ch in id) { h ^= ch; h *= 16777619; }
        return Palette[h % (uint)Palette.Length];
    }

    public static string Size(long? bytes) => bytes switch
    {
        null => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB",
    };
}
