using System.Diagnostics;

namespace Blip.Windows.Services;

/// <summary>
/// Opening a file someone sent you. The name (and so the extension) is the SENDER's
/// choice, so: every fetched file gets Mark-of-the-Web (SmartScreen and Office
/// Protected View then treat it as downloaded, which it is), and anything Windows
/// would execute is only revealed in Explorer, never launched.
/// </summary>
public static class SafeOpen
{
    private static readonly HashSet<string> Executable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".mst", ".msix", ".appx", ".appinstaller", ".lnk", ".url", ".hta", ".cpl", ".reg", ".inf", ".scf",
        ".dll", ".sys", ".jar", ".application", ".gadget", ".settingcontent-ms", ".library-ms", ".search-ms", ".iso",
        ".img", ".vhd", ".vhdx", ".chm", ".xll", ".xlam", ".ppam", ".docm", ".xlsm", ".pptm",
    };

    public static void MarkOfTheWeb(string path)
    {
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); } catch { /* non-NTFS */ }
    }

    /// <summary>Returns a message for the person when the file was revealed instead of opened.</summary>
    public static string? Open(string path)
    {
        MarkOfTheWeb(path);
        if (Executable.Contains(Path.GetExtension(path)))
        {
            // Windows file names cannot contain '"', so quoting the cache path is safe.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            return "Shown in Explorer: Blip does not open program files";
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return null;
    }
}
