using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Blip.Core.Transport;

namespace Blip.Core.Tests;

/// <summary>Records every call; answers from a script. Lets tests assert the exact argv/stdin the Mac would see.</summary>
internal sealed class FakeTransport : IBridgeTransport
{
    public sealed record Call(BridgeTool Tool, IReadOnlyList<string> Args, byte[] Stdin, bool ViaServe);

    public List<Call> Calls { get; } = [];
    public Func<Call, BridgeResult> Respond { get; set; } = _ => new BridgeResult(0, [], "");
    public Channel<string> WatchLines { get; } = Channel.CreateUnbounded<string>();
    public Func<Exception?> WatchFailure { get; set; } = () => null;
    public int WatchStarts;

    public Task<BridgeResult> RunAsync(BridgeTool tool, IReadOnlyList<string> args, ReadOnlyMemory<byte> stdin = default,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var c = new Call(tool, args.ToList(), stdin.ToArray(), false);
        lock (Calls) Calls.Add(c);
        return Task.FromResult(Respond(c));
    }

    public async Task<BridgeResult> StreamAsync(BridgeTool tool, IReadOnlyList<string> args, Stream destination, long maxBytes,
        CancellationToken ct = default)
    {
        var c = new Call(tool, args.ToList(), [], false);
        lock (Calls) Calls.Add(c);
        var r = Respond(c);
        await destination.WriteAsync(r.Stdout, ct);
        return r with { Stdout = [] };
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(BridgeTool tool, IReadOnlyList<string> args,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Interlocked.Increment(ref WatchStarts);
        if (WatchFailure() is { } e) throw e;
        while (await WatchLines.Reader.WaitToReadAsync(ct))
            while (WatchLines.Reader.TryRead(out var line))
            {
                if (line == "<eof>") yield break;
                yield return line;
            }
    }

    public Task<BridgeResult> QueryAsync(IReadOnlyList<string> imsgArgs, string? stdin = null, CancellationToken ct = default)
    {
        var c = new Call(BridgeTool.Imsg, imsgArgs.ToList(), stdin is null ? [] : Encoding.UTF8.GetBytes(stdin), true);
        lock (Calls) Calls.Add(c);
        return Task.FromResult(Respond(c));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static BridgeResult Json(string fixture) =>
        new(0, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), "");
}
