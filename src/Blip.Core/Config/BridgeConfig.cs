using System.Text;
using System.Text.RegularExpressions;

namespace Blip.Core.Config;

/// <summary>
/// The Windows twin of the Linux client's <c>~/.config/blip/bridge.conf</c>: same
/// key=value format, same keys where they mean the same thing, and the same
/// allowlists. The file is DATA, never code; unknown keys are ignored.
/// </summary>
/// <remarks>
/// Windows-only keys: <c>port</c> (Windows OpenSSH has no ssh_config lookup through
/// SSH.NET, so the port has to live here) and <c>host_key</c>, the SHA256
/// fingerprint of the Mac's host key pinned on first connect.
/// </remarks>
public sealed partial record BridgeConfig
{
    /// <summary>ssh target, <c>user@host</c>. Required.</summary>
    public string Host { get; init; } = "";
    public int Port { get; init; } = 22;
    /// <summary>Private key path. Defaults to the dedicated Blip key.</summary>
    public string KeyPath { get; init; } = DefaultKeyPath;
    /// <summary>Pinned host key, <c>SHA256:base64</c> as ssh-keygen prints it. Empty until first trust.</summary>
    public string HostKey { get; init; } = "";
    public bool HideSpam { get; init; }
    public bool HideUnknown { get; init; }
    /// <summary>imsg-react drives Messages through Accessibility: opt-in, as on Linux.</summary>
    public bool Tapbacks { get; init; }
    /// <summary>imsg-reply (native threaded reply): opt-in, as on Linux.</summary>
    public bool Replies { get; init; }
    /// <summary>See SendServiceSelector rule 2.</summary>
    public bool PreferIMessage { get; init; }
    /// <summary>
    /// Push "mark read" to the Mac when a conversation is opened. Opening one
    /// conversation brings Messages to the front on the Mac (imsg-read --chat),
    /// which is why the Linux client defaults to off as well.
    /// </summary>
    public bool PushRead { get; init; }
    /// <summary>
    /// Keep an encrypted snapshot of the sidebar and recent threads on this PC so the window
    /// opens instantly (Windows-only key). The app encrypts it with DPAPI for the current
    /// Windows user; turning it off deletes the file.
    /// </summary>
    public bool LocalCache { get; init; } = true;

    public static string DefaultKeyPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "blip_ed25519");

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blip", "bridge.conf");

    // Same allowlists as blip-shim / blip-bridged (audit #7).
    [GeneratedRegex(@"^([A-Za-z0-9._-]+@)?[A-Za-z0-9._:-]+$")]
    private static partial Regex HostRe();
    [GeneratedRegex(@"^SHA256:[A-Za-z0-9+/]{43}=?$")]
    private static partial Regex HostKeyRe();

    public string User => Host.Contains('@') ? Host[..Host.IndexOf('@')] : Environment.UserName;
    public string HostName => Host.Contains('@') ? Host[(Host.IndexOf('@') + 1)..] : Host;

    /// <summary>Throws <see cref="BridgeConfigException"/> when the config cannot be used.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new BridgeConfigException("no Mac configured: set host= (user@your-mac)");
        if (!HostRe().IsMatch(Host))
            throw new BridgeConfigException($"refusing host '{Host}': expected [user@]hostname");
        if (Port is < 1 or > 65535)
            throw new BridgeConfigException($"port {Port} is out of range");
        if (HostKey.Length > 0 && !HostKeyRe().IsMatch(HostKey))
            throw new BridgeConfigException("host_key must look like SHA256:<43 base64 chars>");
    }

    public static BridgeConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new BridgeConfig();
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parsed, not evaluated: known keys only, comments and quotes stripped (as blip-shim does).</summary>
    public static BridgeConfig Parse(string text)
    {
        var c = new BridgeConfig();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = string.Concat(line.Where(ch => !char.IsWhiteSpace(ch)));
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq];
            var v = line[(eq + 1)..].Trim('"', '\'');
            c = k switch
            {
                "host" => c with { Host = v },
                "port" => int.TryParse(v, out var p) ? c with { Port = p } : c,
                "key" => c with { KeyPath = Environment.ExpandEnvironmentVariables(v) },
                "host_key" => c with { HostKey = v },
                "hide_spam" => c with { HideSpam = Truthy(v) },
                "hide_unknown" => c with { HideUnknown = Truthy(v) },
                "tapbacks" => c with { Tapbacks = Truthy(v) },
                "replies" => c with { Replies = Truthy(v) },
                "prefer_imessage" => c with { PreferIMessage = Truthy(v) },
                "push_read" => c with { PushRead = Truthy(v) },
                "local_cache" => c with { LocalCache = Truthy(v) },
                _ => c,
            };
        }
        return c;
    }

    public string Serialize()
    {
        static string B(bool b) => b ? "on" : "off";
        var sb = new StringBuilder();
        sb.AppendLine("# Blip for Windows bridge config. Data, not code: key=value, known keys only.");
        sb.AppendLine($"host={Host}");
        sb.AppendLine($"port={Port}");
        sb.AppendLine($"key={KeyPath}");
        if (HostKey.Length > 0) sb.AppendLine($"host_key={HostKey}");
        sb.AppendLine($"hide_spam={B(HideSpam)}");
        sb.AppendLine($"hide_unknown={B(HideUnknown)}");
        sb.AppendLine($"tapbacks={B(Tapbacks)}");
        sb.AppendLine($"replies={B(Replies)}");
        sb.AppendLine($"prefer_imessage={B(PreferIMessage)}");
        sb.AppendLine($"push_read={B(PushRead)}");
        sb.AppendLine($"local_cache={B(LocalCache)}");
        return sb.ToString();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize());
        File.Move(tmp, path, overwrite: true);
    }

    private static bool Truthy(string v) => v.ToLowerInvariant() is "on" or "yes" or "true" or "1";
}

public sealed class BridgeConfigException(string message) : Exception(message);
