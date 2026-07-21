using BmsUi.Serial;

/// <summary>
/// Testler icin ISerialTransport: yazilan komutlari kaydeder, sirali cevaplar dondurur.
/// Cevaplar parca parca verilebilir (CDC'nin parcali teslimini taklit eder).
/// </summary>
public sealed class FakeTransport : ISerialTransport
{
    private readonly Queue<byte[]> _chunks = new();
    public List<byte[]> Written { get; } = new();
    public bool IsOpen { get; private set; } = true;
    public int DiscardCount { get; private set; }

    /// <summary>Cevabi tek parca halinde kuyruga koyar.</summary>
    public void EnqueueResponse(byte[] response) => _chunks.Enqueue(response);

    /// <summary>Cevabi verilen boyutlarda parcalara bolerek kuyruga koyar.</summary>
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

    // DiscardInBuffer kuyrugu TEMIZLEMEZ: testler cevabi transaction'dan once kuyruga
    // koyuyor, gercek portta ise cevap komuttan SONRA gelir. Sayac dogrulanabilsin diye
    // yalnizca cagri sayilir.
    public void DiscardInBuffer() => DiscardCount++;

    public void Write(byte[] buf, int offset, int count)
        => Written.Add(buf[offset..(offset + count)]);

    public int Read(byte[] buf, int offset, int count)
    {
        if (_chunks.Count == 0) throw new TimeoutException("FakeTransport: veri yok");
        var chunk = _chunks.Dequeue();
        int n = Math.Min(chunk.Length, count);
        chunk.AsSpan(0, n).CopyTo(buf.AsSpan(offset, n));
        if (n < chunk.Length)
        {
            // Istenen miktardan fazlasi geldiyse artani sirada tut
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
