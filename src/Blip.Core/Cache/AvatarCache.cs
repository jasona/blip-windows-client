using System.Security.Cryptography;
using System.Text;

namespace Blip.Core.Cache;

/// <summary>
/// Contact and group photos, kept between launches under %LOCALAPPDATA%\Blip\avatars so the
/// sidebar has faces before the Mac answers. Each photo costs the Mac a fresh process that
/// scans every address book source, so a cached one is refreshed at most once per
/// <see cref="MaxAge"/>, and "this conversation has no photo" is cached too.
/// </summary>
/// <remarks>File names are a hash of the chat id, so phone numbers and emails never appear on disk
/// as names. Photos only: no message text lives here. Deleting the folder is always safe.</remarks>
public sealed class AvatarCache(string? root = null, TimeSpan? maxAge = null, TimeProvider? clock = null)
{
    private readonly string _root = root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blip", "avatars");
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public TimeSpan MaxAge { get; } = maxAge ?? TimeSpan.FromDays(1);
    public string Root => _root;

    /// <summary>What the cache knows about one conversation's photo.</summary>
    /// <param name="Bytes">The photo, or null when the Mac said there is none.</param>
    /// <param name="Fresh">False once the entry is older than <see cref="MaxAge"/>: show it, then refetch.</param>
    public readonly record struct Entry(byte[]? Bytes, bool Fresh);

    public Entry? TryGet(string chat)
    {
        try
        {
            foreach (var (path, hasPhoto) in new[] { (PathFor(chat, ".img"), true), (PathFor(chat, ".none"), false) })
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                var fresh = _clock.GetUtcNow() - info.LastWriteTimeUtc < MaxAge;
                return new Entry(hasPhoto ? File.ReadAllBytes(path) : null, fresh);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Record the Mac's answer: a photo, or null for "no photo".</summary>
    public void Store(string chat, byte[]? bytes)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var keep = PathFor(chat, bytes is null ? ".none" : ".img");
            var drop = PathFor(chat, bytes is null ? ".img" : ".none");
            var tmp = keep + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
            File.WriteAllBytes(tmp, bytes ?? []);
            File.Move(tmp, keep, overwrite: true);
            File.SetLastWriteTimeUtc(keep, _clock.GetUtcNow().UtcDateTime);
            if (File.Exists(drop)) File.Delete(drop);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal string PathFor(string chat, string ext)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chat)))[..32];
        return Path.Combine(_root, hash + ext);
    }
}
