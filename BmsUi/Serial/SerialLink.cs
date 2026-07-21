using System.Diagnostics;
using BmsUi.Protocol;

namespace BmsUi.Serial;

/// <summary>
/// Senkron komut-cevap katmani. TEK transaction kurali: komut tek Write ile yazilir,
/// cevabin tamami okunmadan yeni komut gonderilmez (firmware paket UZUNLUGUNA gore
/// ayristirir; birlesen iki komut yanlis yorumlanir — main.cpp:1958 switch (usblen)).
/// </summary>
public sealed class SerialLink : IDisposable
{
    private readonly ISerialTransport _transport;
    private readonly TimeSpan _deadline;
    private readonly byte[] _rx = new byte[HvProtocol.CellFrameLength];

    public SerialLink(ISerialTransport transport, TimeSpan? deadline = null)
    {
        _transport = transport;
        _deadline = deadline ?? TimeSpan.FromMilliseconds(300);
    }

    public int CrcErrorCount { get; private set; }
    public int TimeoutCount { get; private set; }
    public int IdMismatchCount { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public string? LastError { get; private set; }

    public bool IsOpen => _transport.IsOpen;
    public void Open() => _transport.Open();
    public void Close() => _transport.Close();

    /// <summary>0x17 0x71 gonderir, ayni iki baytin echo'sunu bekler (CRC yok).</summary>
    public bool Ping()
    {
        var echo = Exchange(HvProtocol.PingCommand, HvProtocol.PingResponseLength);
        if (echo is null) return false;

        bool ok = echo[0] == HvProtocol.PingCommand[0] && echo[1] == HvProtocol.PingCommand[1];
        if (!ok)
        {
            LastError = "Ping echo uyusmadi";
            Fail();
        }
        else ConsecutiveFailures = 0;
        return ok;
    }

    /// <summary>Komutu yazar, tam uzunlukta cevabi okur, kimlik + CRC dogrular.</summary>
    public byte[]? Transact(byte[] command, int expectedLength, byte expectedId)
    {
        var frame = Exchange(command, expectedLength);
        if (frame is null) return null;

        if (frame[expectedLength - 2] != expectedId)
        {
            IdMismatchCount++;
            LastError = $"Kimlik uyusmazligi: beklenen 0x{expectedId:X2}, " +
                        $"gelen 0x{frame[expectedLength - 2]:X2}";
            Fail();
            return null;
        }
        if (Crc8.Compute(frame.AsSpan(0, expectedLength - 1)) != frame[expectedLength - 1])
        {
            CrcErrorCount++;
            LastError = "CRC uyusmazligi";
            Fail();
            return null;
        }
        ConsecutiveFailures = 0;
        return frame;
    }

    public ushort? ReadRegister(byte index)
    {
        if (!HvProtocol.IsValidRegister(index))
            throw new ArgumentOutOfRangeException(nameof(index),
                $"idx {index} okunamaz (>=50 veya 0x29/0x2A/0x2B ile golgeli)");

        var frame = Transact(new[] { index }, HvProtocol.RegisterFrameLength, index);
        if (frame is null) return null;
        return (ushort)(frame[0] | (frame[1] << 8));
    }

    public ushort? WriteRegister(byte index, ushort value)
    {
        if (!HvProtocol.IsValidRegister(index))
            throw new ArgumentOutOfRangeException(nameof(index),
                $"idx {index} yazilamaz (>=50 veya 0x29/0x2A/0x2B ile golgeli)");

        var cmd = new[] { index, (byte)(value & 0xFF), (byte)(value >> 8) };
        var frame = Transact(cmd, HvProtocol.RegisterFrameLength, index);
        if (frame is null) return null;
        return (ushort)(frame[0] | (frame[1] << 8));
    }

    /// <summary>Yaz + tam N bayta kadar biriktir. Zarf dogrulamasi yapmaz.</summary>
    private byte[]? Exchange(byte[] command, int expectedLength)
    {
        try
        {
            _transport.DiscardInBuffer();          // onceki timeout artiklari akisi zehirler
            _transport.Write(command, 0, command.Length);

            int got = 0;
            var sw = Stopwatch.StartNew();
            while (got < expectedLength)
            {
                int n = _transport.Read(_rx, got, expectedLength - got);
                if (n <= 0) break;
                got += n;
                if (sw.Elapsed > _deadline) break;
            }

            if (got < expectedLength)
            {
                TimeoutCount++;
                LastError = $"Eksik cevap: {got}/{expectedLength} bayt";
                Fail();
                return null;
            }
            return _rx[..expectedLength];
        }
        catch (TimeoutException)
        {
            TimeoutCount++;
            LastError = "Zaman asimi";
            Fail();
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Fail();
            return null;
        }
    }

    private void Fail() => ConsecutiveFailures++;

    public void ResetCounters()
        => CrcErrorCount = TimeoutCount = IdMismatchCount = ConsecutiveFailures = 0;

    public void Dispose() => _transport.Dispose();
}
