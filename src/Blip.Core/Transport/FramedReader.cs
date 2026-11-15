using System.Text;

namespace Blip.Core.Transport;

/// <summary>
/// Reads newline-terminated header lines and exact-length bodies from one stream:
/// the framing <c>imsg serve</c> uses (<c>{"status":0,"len":N}\n</c> then N bytes).
/// Length framing, not escaping, so a 120 KB JSON reply is never re-encoded.
/// </summary>
internal sealed class FramedReader(Stream inner)
{
    private readonly byte[] _buf = new byte[64 * 1024];
    private int _start;
    private int _end;

    private async ValueTask<bool> FillAsync(CancellationToken ct)
    {
        if (_start > 0 && _start == _end) { _start = _end = 0; }
        if (_end == _buf.Length)
        {
            if (_start == 0) throw new InvalidDataException("frame header longer than 64 KB");
            Buffer.BlockCopy(_buf, _start, _buf, 0, _end - _start);
            _end -= _start; _start = 0;
        }
        var n = await inner.ReadAsync(_buf.AsMemory(_end), ct).ConfigureAwait(false);
        if (n <= 0) return false;
        _end += n;
        return true;
    }

    /// <summary>The next line without its terminator, or null at end of stream.</summary>
    public async ValueTask<string?> ReadLineAsync(int maxLength, CancellationToken ct)
    {
        var scanFrom = _start;
        while (true)
        {
            var idx = Array.IndexOf(_buf, (byte)'\n', scanFrom, _end - scanFrom);
            if (idx >= 0)
            {
                var line = Encoding.UTF8.GetString(_buf, _start, idx - _start).TrimEnd('\r');
                _start = idx + 1;
                return line;
            }
            if (_end - _start > maxLength) throw new InvalidDataException("line exceeds limit");
            if (!await FillAsync(ct).ConfigureAwait(false))
                return _end > _start ? TakeRest() : null;
            scanFrom = _start; // FillAsync may compact the buffer; rescan the (small) pending header
        }
    }

    private string TakeRest()
    {
        var s = Encoding.UTF8.GetString(_buf, _start, _end - _start);
        _start = _end = 0;
        return s;
    }

    public async ValueTask<byte[]> ReadExactAsync(int length, CancellationToken ct)
    {
        var result = new byte[length];
        var off = 0;
        var buffered = Math.Min(_end - _start, length);
        Buffer.BlockCopy(_buf, _start, result, 0, buffered);
        _start += buffered; off += buffered;
        while (off < length)
        {
            var n = await inner.ReadAsync(result.AsMemory(off), ct).ConfigureAwait(false);
            if (n <= 0) throw new EndOfStreamException($"frame truncated at {off} of {length} bytes");
            off += n;
        }
        return result;
    }
}
