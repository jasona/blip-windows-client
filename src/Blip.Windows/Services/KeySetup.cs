using System.Diagnostics;

namespace Blip.Windows.Services;

/// <summary>Creates the dedicated key with Windows' own OpenSSH ssh-keygen and builds the Mac-side line.</summary>
public static class KeySetup
{
    public static string AuthorizedKeysLine(string publicKey) =>
        $"restrict,command=\"$HOME/.blip/bin/blip-dispatch\" {publicKey.Trim()}";

    /// <summary>One command to paste into Terminal on the Mac. Appends; never rewrites the file.</summary>
    public static string MacCommand(string publicKey) =>
        "mkdir -p ~/.ssh && chmod 700 ~/.ssh && printf '%s\\n' '" +
        AuthorizedKeysLine(publicKey).Replace("'", "'\\''") +
        "' >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys";

    public static string? ReadPublicKey(string keyPath) =>
        File.Exists(keyPath + ".pub") ? File.ReadAllText(keyPath + ".pub").Trim() : null;

    /// <summary>Generate an unencrypted ed25519 key (the bridge runs unattended). Returns an error or null.</summary>
    public static async Task<string?> GenerateAsync(string keyPath)
    {
        if (File.Exists(keyPath)) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh-keygen.exe");
        if (!File.Exists(exe)) exe = "ssh-keygen";
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in new[] { "-q", "-t", "ed25519", "-N", "", "-C", $"blip-windows@{Environment.MachineName}", "-f", keyPath })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? null : err.Trim();
        }
        catch (Exception e)
        {
            return $"ssh-keygen not found ({e.Message}). Enable the Windows OpenSSH Client optional feature.";
        }
    }
}
