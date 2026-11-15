using Blip.Core;
using Blip.Core.Cache;
using Blip.Core.Config;
using Blip.Core.Live;
using Blip.Core.Transport;

namespace Blip.Windows.Services;

/// <summary>Everything that lives as long as one bridge configuration: transport, client, push monitor, cache.</summary>
public sealed class BlipSession : IAsyncDisposable
{
    public BridgeConfig Config { get; }
    public SshBridgeTransport Transport { get; }
    public BlipClient Client { get; }
    public BridgeMonitor Monitor { get; }
    public AttachmentCache Cache { get; } = new();
    public AvatarCache Avatars { get; } = new();
    /// <summary>The DPAPI-protected startup snapshot, or null when <c>local_cache=off</c>.</summary>
    public SnapshotStore? Snapshots { get; }

    public BlipSession(BridgeConfig config, Func<string, bool> trustNewHostKey)
    {
        Config = config;
        Transport = new SshBridgeTransport(config, trustNewHostKey);
        // Persist the pinned host key the first time it is trusted.
        Transport.HostKeyPinned += fp =>
        {
            var latest = BridgeConfig.Load();
            (latest with { HostKey = fp }).Save();
        };
        Client = new BlipClient(Transport, config);
        Monitor = new BridgeMonitor(Transport);
        var store = new SnapshotStore(SnapshotStore.DefaultPath, Dpapi.Protect, Dpapi.Unprotect);
        if (config.LocalCache) Snapshots = store;
        else store.Delete();   // turning the option off removes what it stored
    }

    public async ValueTask DisposeAsync()
    {
        await Monitor.DisposeAsync();
        await Transport.DisposeAsync();
    }
}
