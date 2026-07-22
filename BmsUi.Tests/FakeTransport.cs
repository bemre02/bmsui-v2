using BmsUi.Serial;

/// <summary>
/// ISerialTransport for tests: records written commands and returns queued responses.
/// Responses can be delivered in pieces (mimicking CDC's chunked delivery).
/// </summary>
public sealed class FakeTransport : ISerialTransport
{
    private readonly Queue<byte[]> _chunks = new();
    public List<byte[]> Written { get; } = new();
    public bool IsOpen { get; private set; } = true;
    public int DiscardCount { get; private set; }

    /// <summary>Queues a response as a single chunk.</summary>
    public void EnqueueResponse(byte[] response) => _chunks.Enqueue(response);

    /// <summary>Queues a response split into chunks of the given sizes.</summary>
    public void EnqueueChunked(byte[] response, params int[] sizes)
    {
        int off = 0;
        foreach (int size in sizes)
        {
            _chunks.Enqueue(response[off..(off + size)]);
            off += size;
        }
        if (off < response.Length) _chunks.Enqueue(response[off..]);
    }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;

    // DiscardInBuffer does NOT clear the queue: tests enqueue the response before the
    // transaction, whereas on a real port the response arrives AFTER the command. It only
    // counts the calls so the counter can still be asserted.
    public void DiscardInBuffer() => DiscardCount++;

    public void Write(byte[] buf, int offset, int count)
        => Written.Add(buf[offset..(offset + count)]);

    public int Read(byte[] buf, int offset, int count)
    {
        if (_chunks.Count == 0) throw new TimeoutException("FakeTransport: no data");
        var chunk = _chunks.Dequeue();
        int n = Math.Min(chunk.Length, count);
        chunk.AsSpan(0, n).CopyTo(buf.AsSpan(offset, n));
        if (n < chunk.Length)
        {
            // If more arrived than was asked for, keep the remainder queued
            var rest = chunk[n..];
            var pending = _chunks.ToArray();
            _chunks.Clear();
            _chunks.Enqueue(rest);
            foreach (var p in pending) _chunks.Enqueue(p);
        }
        return n;
    }

    public void Dispose() { }
}
