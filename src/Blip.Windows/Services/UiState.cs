using System.Text.Json;

namespace Blip.Windows.Services;

/// <summary>Window layout the person chose (sidebar width). Layout only, never conversation data.</summary>
public sealed class UiState
{
    public const double DefaultSidebar = 320, MinSidebar = 270, MaxSidebar = 560;

    public double SidebarWidth { get; set; } = DefaultSidebar;

    private static string PathFor => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blip", "ui.json");

    public static UiState Load()
    {
        try
        {
            if (File.Exists(PathFor) && JsonSerializer.Deserialize<UiState>(File.ReadAllText(PathFor)) is { } s)
            {
                s.SidebarWidth = Math.Clamp(s.SidebarWidth, MinSidebar, MaxSidebar);
                return s;
            }
        }
        catch { /* a broken file just means defaults */ }
        return new UiState();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFor)!);
            File.WriteAllText(PathFor, JsonSerializer.Serialize(this));
        }
        catch { /* layout is a nicety */ }
    }
}
