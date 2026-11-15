using System.Security.Cryptography;

namespace Blip.Windows.Services;

/// <summary>
/// Windows DPAPI, CurrentUser scope: only this Windows account on this PC can decrypt.
/// The key is derived from the account's logon secret, so a copied file (backup, Dropbox,
/// another account) is unreadable. The entropy just keeps Blip's blobs from being
/// interchangeable with another app's.
/// </summary>
internal static class Dpapi
{
    private static readonly byte[] Entropy = "Blip.Windows.Snapshot.v1"u8.ToArray();

    public static byte[] Protect(byte[] plain) =>
        ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] cipher) =>
        ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
}
