using System.Text.Json;

namespace Blip.Windows.Services;

/// <summary>
/// Local "seen through" marks: opening a conversation clears its dot here without
/// touching the Mac (imsg-read --chat brings Messages to the front there, so pushing
/// reads is opt-in via push_read). A newer inbound message brings the dot back.
/// Stores chat ids and timestamps only, never message text.
/// </summary>
public sealed class ReadMarks
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blip", "read-marks.json");
    private readonly Dictionary<string, DateTimeOffset> _marks;

    public ReadMarks()
    {
        try
        {
            _marks = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch { _marks = []; }
    }

    public bool IsSeen(string chat, DateTimeOffset last) => _marks.TryGetValue(chat, out var m) && m >= last;

    public void MarkSeen(string chat, DateTimeOffset through)
    {
        if (_marks.TryGetValue(chat, out var m) && m >= through) return;
        _marks[chat] = through;
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Bounded: keep the 500 most recent marks.
            var keep = _marks.OrderByDescending(kv => kv.Value).Take(500).ToDictionary();
            File.WriteAllText(_path, JsonSerializer.Serialize(keep));
        }
        catch { /* marks are a convenience */ }
    }
}
