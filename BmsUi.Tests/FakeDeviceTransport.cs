using System.Buffers.Binary;
using BmsUi.Protocol;
using BmsUi.Serial;

/// <summary>
/// C# twin of bms_simulator.py: like the firmware, it answers based on the LENGTH of the
/// incoming command. Used to drive PollWorker end to end without a real port.
/// </summary>
public sealed class FakeDeviceTransport : ISerialTransport
{
    private readonly object _lock = new();
    private readonly Queue<byte> _pending = new();

    public ushort[] Registers { get; } = new ushort[HvProtocol.MaxRegisterIndexExclusive];
    public double[] Voltages { get; } = new double[HvProtocol.CellCount];
    public double[] Temps { get; } = new double[HvProtocol.CellCount];
    public ushort[] BalanceBitmaps { get; } = new ushort[HvProtocol.SegmentCount];

    /// <summary>Splits responses into 64-byte chunks (CDC behaviour).</summary>
    public bool Chunked { get; set; }

    /// <summary>While true the device never answers (simulates a dropped link).</summary>
    public bool Mute { get; set; }

    public int CommandCount { get; private set; }

    public FakeDeviceTransport()
    {
        for (int i = 0; i < HvProtocol.CellCount; i++)
        {
            Voltages[i] = 3.60 + i * 0.005;      // 3.600 .. 4.075
            Temps[i] = -5.0 + i * 0.5;           // -5.0 .. 42.5  (includes the negative end)
        }
        Registers[Reg.Faults] = (1 << 2) | (1 << 13);
        Registers[Reg.Outputs] = OutputBits.Air | OutputBits.Pre;
        Registers[Reg.PackVoltage] = 37250;                      // 372.50 V
        Registers[Reg.PackCurrent] = unchecked((ushort)-1234);   // -123.4 A
        Registers[Reg.EstimatedSoc] = 7300;                      // %73.00
        Registers[Reg.AllowedDisbalance] = 20;
        Registers[Reg.PrechargePercentage] = 95;
        Registers[Reg.PrechargeTimeout] = 5000;
        BalanceBitmaps[0] = 0b0000_0000_0000_0101;
        BalanceBitmaps[5] = 0b1000_0000_0000_0000;
    }

    public bool IsOpen { get; private set; } = true;
    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void DiscardInBuffer() { lock (_lock) _pending.Clear(); }
    public void Dispose() { }

    public void Write(byte[] buffer, int offset, int count)
    {
        CommandCount++;
        var packet = buffer[offset..(offset + count)];
        if (Mute) return;

        byte[]? response = packet.Length switch
        {
            1 => Respond(packet[0]),
            2 => packet[0] == 0x17 && packet[1] == 0x71 ? packet : null,
            3 => WriteRegister(packet),
            _ => null,
        };
        if (response is null) return;

        lock (_lock)
            foreach (byte b in response) _pending.Enqueue(b);
    }

    private byte[]? Respond(byte cmd) => cmd switch
    {
        HvProtocol.CmdCellVoltages => CellFrame(Voltages, HvProtocol.CmdCellVoltages),
        HvProtocol.CmdCellTemps => CellFrame(Temps, HvProtocol.CmdCellTemps),
        HvProtocol.CmdBalance => BalanceFrame(),
        // firmware: idx >= 50 is dropped silently
        _ => cmd < HvProtocol.MaxRegisterIndexExclusive ? RegisterFrame(cmd, Registers[cmd]) : null,
    };

    private byte[] WriteRegister(byte[] packet)
    {
        byte idx = packet[0];
        if (!WriteGuard.IsWritable(idx)) return Array.Empty<byte>();
        Registers[idx] = (ushort)((packet[2] << 8) | packet[1]);
        return RegisterFrame(idx, Registers[idx]);
    }

    private static byte[] CellFrame(double[] values, byte id)
    {
        var f = new byte[HvProtocol.CellFrameLength];
        for (int i = 0; i < HvProtocol.CellCount; i++)
            BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(i * 2, 2), (short)(values[i] * 100));
        f[192] = id;
        f[193] = Crc8.Compute(f.AsSpan(0, 193));
        return f;
    }

    private byte[] BalanceFrame()
    {
        var f = new byte[HvProtocol.BalanceFrameLength];
        for (int i = 0; i < HvProtocol.SegmentCount; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(i * 2, 2), BalanceBitmaps[i]);
        f[12] = HvProtocol.CmdBalance;
        f[13] = Crc8.Compute(f.AsSpan(0, 13));
        return f;
    }

    private static byte[] RegisterFrame(byte idx, ushort value)
    {
        var f = new byte[HvProtocol.RegisterFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0, 2), value);
        f[2] = idx;
        f[3] = Crc8.Compute(f.AsSpan(0, 3));
        return f;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            if (_pending.Count == 0) throw new TimeoutException("FakeDevice: no response pending");
            int limit = Chunked ? Math.Min(count, 64) : count;
            int n = 0;
            while (n < limit && _pending.Count > 0) buffer[offset + n++] = _pending.Dequeue();
            return n;
        }
    }
}
