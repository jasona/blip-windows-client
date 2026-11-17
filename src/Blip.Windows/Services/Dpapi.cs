#if WINDOWS
using System.Security.Cryptography;
#endif

namespace Blip.Windows.Services;

/// <summary>
/// Windows DPAPI, CurrentUser scope: only this Windows account on this PC can decrypt.
/// The key is derived from the account's logon secret, so a copied file (backup, Dropbox,
/// another account) is unreadable. The entropy just keeps Blip's blobs from being
/// interchangeable with another app's. Off Windows there is no protector, so no snapshot.
/// </summary>
internal static class Dpapi
{
    public static bool Available =>
#if WINDOWS
        true;
#else
        false;
#endif

#if WINDOWS
    private static readonly byte[] Entropy = "Blip.Windows.Snapshot.v1"u8.ToArray();

    public static byte[] Protect(byte[] plain) =>
        ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] cipher) =>
        ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
#else
    public static byte[] Protect(byte[] plain) => throw new PlatformNotSupportedException();
    public static byte[] Unprotect(byte[] cipher) => throw new PlatformNotSupportedException();
#endif
}
