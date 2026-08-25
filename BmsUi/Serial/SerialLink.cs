using System.Diagnostics;
using BmsUi.Protocol;

namespace BmsUi.Serial;

/// <summary>
/// Synchronous request/response layer. ONE transaction at a time: a command is written with
/// a single Write call and no new command is sent until the previous response has been read
/// in full. The firmware dispatches on packet LENGTH, so two commands merged into one USB
/// packet would be misread (main.cpp:1958, switch (usblen)).
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

    /// <summary>Sends 0x17 0x71 and expects the same two bytes echoed back (no CRC).</summary>
    public bool Ping()
    {
        var echo = Exchange(HvProtocol.PingCommand, HvProtocol.PingResponseLength);
        if (echo is null) return false;

        bool ok = echo[0] == HvProtocol.PingCommand[0] && echo[1] == HvProtocol.PingCommand[1];
        if (!ok)
        {
            LastError = "Ping echo did not match";
            Fail();
        }
        else ConsecutiveFailures = 0;
        return ok;
    }

    /// <summary>Writes the command, reads the full response, verifies id + CRC.</summary>
    public byte[]? Transact(byte[] command, int expectedLength, byte expectedId)
    {
        var frame = Exchange(command, expectedLength);
        if (frame is null) return null;

        if (frame[expectedLength - 2] != expectedId)
        {
            IdMismatchCount++;
            LastError = $"Id mismatch: expected 0x{expectedId:X2}, " +
                        $"got 0x{frame[expectedLength - 2]:X2}";
            Fail();
            return null;
        }
        if (Crc8.Compute(frame.AsSpan(0, expectedLength - 1)) != frame[expectedLength - 1])
        {
            CrcErrorCount++;
            LastError = "CRC mismatch";
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
                $"idx {index} is not readable (>=50 or shadowed by 0x29/0x2A/0x2B)");

        var frame = Transact(new[] { index }, HvProtocol.RegisterFrameLength, index);
        if (frame is null) return null;
        return (ushort)(frame[0] | (frame[1] << 8));
    }

    public ushort? WriteRegister(byte index, ushort value)
    {
        if (!WriteGuard.IsWritable(index))
            throw new ArgumentOutOfRangeException(nameof(index),
                $"idx {index} is not writable (shadowed, >=50, or FW write-protected SoC/NV 17/48/49)");

        var cmd = new[] { index, (byte)(value & 0xFF), (byte)(value >> 8) };
        var frame = Transact(cmd, HvProtocol.RegisterFrameLength, index);
        if (frame is null) return null;
        return (ushort)(frame[0] | (frame[1] << 8));
    }

    /// <summary>Write, then accumulate until exactly N bytes. Does not validate the envelope.</summary>
    private byte[]? Exchange(byte[] command, int expectedLength)
    {
        try
        {
            _transport.DiscardInBuffer();   // leftovers from a timed-out transaction poison the stream
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
                LastError = $"Short response: {got}/{expectedLength} bytes";
                Fail();
                return null;
            }
            return _rx[..expectedLength];
        }
        catch (TimeoutException)
        {
            TimeoutCount++;
            LastError = "Timeout";
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
