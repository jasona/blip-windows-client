using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Blip.Core.Cache;

/// <summary>
/// Attachment bytes fetched from the Mac, cached under %LOCALAPPDATA%\Blip\att with a
/// size-capped LRU (by last access), the same policy as the Linux client's
/// ~/.cache/blip/att. Attachments are cached here in the clear; message text only ever lands in the DPAPI-protected snapshot (SnapshotStore).
/// </summary>
/// <remarks>The Linux client relies on full-disk encryption for this cache; on Windows
/// that is BitLocker. Deleting the folder is always safe: everything refetches.</remarks>
public sealed partial class AttachmentCache(string? root = null, long capBytes = 500L * 1024 * 1024)
{
    private readonly string _root = root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blip", "att");
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inflight = new();

    public string Root => _root;

    [GeneratedRegex(@"[^A-Za-z0-9._-]")] private static partial Regex Unsafe();

    /// <summary>Cache path for an attachment variant. The id is digits; the name only contributes its extension.</summary>
    public string PathFor(string id, string name, int previewMaxDim = 0)
    {
        var ext = Path.GetExtension(name);
        ext = previewMaxDim > 0 ? ".jpg" : Unsafe().Replace(ext, "");
        if (ext.Length > 12) ext = "";
        var file = previewMaxDim > 0 ? $"{id}-p{previewMaxDim}{ext}" : $"{id}{ext}";
        return Path.Combine(_root, Unsafe().Replace(file, "_"));
    }

    /// <summary>The cached file, fetching it once if needed. Concurrent callers share one fetch.</summary>
    public async Task<string?> GetAsync(string id, string name, int previewMaxDim,
        Func<Stream, CancellationToken, Task<bool>> fetch, CancellationToken ct = default)
    {
        var path = PathFor(id, name, previewMaxDim);
        if (File.Exists(path))
        {
            Touch(path);
            return path;
        }
        var lazy = _inflight.GetOrAdd(path, p => new Lazy<Task<string?>>(() => FetchAsync(p, fetch, ct)));
        try { return await lazy.Value.ConfigureAwait(false); }
        finally { _inflight.TryRemove(path, out _); }
    }

    private async Task<string?> FetchAsync(string path, Func<Stream, CancellationToken, Task<bool>> fetch, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        try
        {
            bool ok;
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                ok = await fetch(fs, ct).ConfigureAwait(false);
            if (!ok || new FileInfo(tmp).Length == 0) { File.Delete(tmp); return null; }
            File.Move(tmp, path, overwrite: true);
            _ = Task.Run(Evict, CancellationToken.None);
            return path;
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    private static void Touch(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
    }

    /// <summary>Drop least-recently-used files until the cache is under its cap.</summary>
    public void Evict()
    {
        try
        {
            var files = new DirectoryInfo(_root).GetFiles().Where(f => !f.Name.EndsWith(".part")).OrderBy(f => f.LastWriteTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= capBytes) break;
                total -= f.Length;
                try { f.Delete(); } catch { }
            }
        }
        catch (DirectoryNotFoundException) { }
    }
}
