using System.Text.Json;
using Blip.Core.Protocol;

namespace Blip.Core.Cache;

/// <summary>
/// What the window showed last time: the sidebar's raw inputs plus the newest messages of
/// recently opened conversations. Loaded at startup so the window paints before the Mac
/// answers, then replaced by live data.
/// </summary>
public sealed record Snapshot
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    /// <summary>Which Mac and filters produced it (<see cref="SnapshotStore.KeyFor"/>). A mismatch discards it.</summary>
    public string Key { get; init; } = "";
    public DateTimeOffset SavedAt { get; init; }
    public List<ChatInfo> Chats { get; init; } = [];
    public ReadStateSnapshot? ReadState { get; init; }
    public List<GroupInfo> Groups { get; init; } = [];
    public Dictionary<string, List<Message>> Threads { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// One snapshot file, protected by the caller's <c>protect</c>/<c>unprotect</c> pair. The Windows
/// app passes DPAPI (CurrentUser scope), so the file holds message text only in a form that
/// your Windows login alone can decrypt. Core never writes it unprotected.
/// </summary>
/// <remarks>Any failure to read (wrong user, corrupt file, old version, other Mac) is "no snapshot":
/// the app simply loads from the Mac as before. Deleting the file is always safe.</remarks>
public sealed class SnapshotStore(string path, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect)
{
    public const int MaxThreads = 20;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blip", "snapshot.bin");

    public string FilePath => path;

    /// <summary>Snapshots are only valid for the Mac and filters that produced them.</summary>
    public static string KeyFor(Config.BridgeConfig c) =>
        $"{c.Host.ToLowerInvariant()}:{c.Port}|spam={c.HideSpam}|unknown={c.HideUnknown}";

    public Snapshot? Load(string key)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = unprotect(File.ReadAllBytes(path));
            var snap = JsonSerializer.Deserialize(json, BlipJson.Default.Snapshot);
            return snap is { Version: Snapshot.CurrentVersion } && snap.Key == key ? snap : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(Snapshot snapshot)
    {
        var bytes = protect(JsonSerializer.SerializeToUtf8Bytes(snapshot, BlipJson.Default.Snapshot));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    public void Delete()
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
