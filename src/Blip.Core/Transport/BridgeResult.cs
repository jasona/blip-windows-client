using System.Text;

namespace Blip.Core.Transport;

public sealed record BridgeResult(int Status, byte[] Stdout, string Stderr)
{
    public bool Ok => Status == BridgeExit.Ok;
    public bool Offline => Status == BridgeExit.Unavailable;
    public string Text => Encoding.UTF8.GetString(Stdout);

    /// <summary>The bridge's own one-line reason, for showing to a person.</summary>
    public string Reason
    {
        get
        {
            var s = Stderr.Trim();
            if (s.Length == 0) s = Text.Trim();
            var lines = s.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? $"exit {Status}" : lines[^1].Trim();
        }
    }
}

public class BridgeException(int status, string message, Exception? inner = null) : Exception(message, inner)
{
    public int Status { get; } = status;
}

/// <summary>The Mac could not be reached. Mirrors the shim's exit 69.</summary>
public sealed class BridgeOfflineException(string message, Exception? inner = null)
    : BridgeException(BridgeExit.Unavailable, message, inner);

/// <summary>The Mac's host key does not match the pinned one. Never retried silently.</summary>
public sealed class HostKeyMismatchException(string expected, string actual)
    : BridgeException(BridgeExit.Config, $"the Mac's host key changed (expected {expected}, got {actual}); refusing to connect")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}
