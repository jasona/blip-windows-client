namespace Blip.Core.Transport;

/// <summary>
/// One way to run the Mac's bridge tools. The production implementation is
/// <see cref="SshBridgeTransport"/>; tests substitute a fake.
/// </summary>
public interface IBridgeTransport : IAsyncDisposable
{
    /// <summary>One-shot run. <paramref name="stdin"/> is how message text travels: never argv.</summary>
    Task<BridgeResult> RunAsync(BridgeTool tool, IReadOnlyList<string> args,
        ReadOnlyMemory<byte> stdin = default, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>Stream a binary reply (attachments, avatars) into <paramref name="destination"/>.
    /// Returns the exit status; throws if more than <paramref name="maxBytes"/> arrive.</summary>
    Task<BridgeResult> StreamAsync(BridgeTool tool, IReadOnlyList<string> args, Stream destination,
        long maxBytes, CancellationToken ct = default);

    /// <summary>A long-running command's stdout, one line at a time (<c>imsg watch</c>).</summary>
    IAsyncEnumerable<string> ReadLinesAsync(BridgeTool tool, IReadOnlyList<string> args, CancellationToken ct = default);

    /// <summary>
    /// A read-only imsg query. Uses a pooled <c>imsg serve</c> channel when one is
    /// available and falls back to a one-shot run otherwise, so a dead channel is
    /// never more than a slower answer.
    /// </summary>
    Task<BridgeResult> QueryAsync(IReadOnlyList<string> imsgArgs, string? stdin = null, CancellationToken ct = default);
}
