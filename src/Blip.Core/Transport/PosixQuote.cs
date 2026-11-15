namespace Blip.Core.Transport;

/// <summary>
/// Quotes argv for blip-dispatch, which <c>shlex.split</c>s SSH_ORIGINAL_COMMAND.
/// POSIX single quotes around every argument, an embedded <c>'</c> as <c>'\''</c>:
/// the exact rule blip-shim's <c>shq()</c> uses, and the only one shlex and a
/// shell agree on for tabs, newlines, apostrophes and leading dashes.
/// </summary>
public static class PosixQuote
{
    public static string Quote(string arg) => "'" + arg.Replace("'", "'\\''") + "'";

    public static string Command(string tool, IEnumerable<string> args)
    {
        // The tool name is never quoted: dispatch matches it exactly against its allowlist.
        var parts = new List<string> { tool };
        parts.AddRange(args.Select(Quote));
        return string.Join(' ', parts);
    }
}
