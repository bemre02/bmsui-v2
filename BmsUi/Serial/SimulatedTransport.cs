using System.Buffers.Binary;
using System.Diagnostics;
using BmsUi.Protocol;

namespace BmsUi.Serial;

/// <summary>
/// In-app virtual HV BMS device — needs no virtual COM port or driver.
/// It implements ISerialTransport, the same interface a real port does, so SerialLink,
/// CRC verification, the parser and PollWorker all run through the SAME code path;
/// only the source of the bytes changes.
///
/// Firmware behaviour is mirrored: commands are dispatched on packet LENGTH
/// (main.cpp:1958, switch (usblen)) and every response ends with a correct CRC8.
/// </summary>
public sealed class SimulatedTransport : ISerialTransport
{
    private readonly object _lock = new();
    private readonly Queue<byte> _pending = new();
    private readonly Random _rng;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly double[] _volts = new double[HvProtocol.CellCount];
    private readonly double[] _temps = new double[HvProtocol.CellCount];
    private readonly ushort[] _registers = new ushort[HvProtocol.MaxRegisterIndexExclusive];

    public SimulatedTransport(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();

        for (int i = 0; i < HvProtocol.CellCount; i++)
        {
            _volts[i] = 3.88 + _rng.NextDouble() * 0.06;
            _temps[i] = 24.0 + _rng.NextDouble() * 5.0;
        }
        _volts[42] -= 0.12;                  // a clearly identifiable min cell
        _volts[7] += 0.09;                   // a clearly identifiable max cell
        foreach (int hot in new[] { 7, 42, 83 }) _temps[hot] += 14.0;

        _registers[Reg.AllowedDisbalance] = 20;
        _registers[Reg.PrechargePercentage] = 95;
        _registers[Reg.PrechargeTimeout] = 5000;

        // So the Registers tab is not a wall of zeros in simulation. Scales follow the
        // firmware: voltages x100, currents x10, delays in milliseconds.
        _registers[2] = 2;                                 // CHARGING_STATE = CHARGING
        _registers[3] = 39000;                             // CHARGER_SET_VOLTAGE  390.00 V
        _registers[4] = 60;                                // CHARGER_SET_CURRENT    6.0 A
        _registers[31] = unchecked((ushort)(short)-60);    // CHARGE_CURRENT        -6.0 A
        _registers[34] = unchecked((ushort)(short)-500);   // CHARGE_OVER_CURRENT_TRESHOLD
        _registers[35] = 3500;                             // DISCHARGE_OVER_CURRENT_TRESHOLD
        for (byte i = 36; i <= 40; i++) _registers[i] = 100;   // error delays, ms
    }

    public bool IsOpen { get; private set; }
    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void DiscardInBuffer() { lock (_lock) _pending.Clear(); }
    public void Dispose() => Close();

    public void Write(byte[] buffer, int offset, int count)
    {
        if (!IsOpen) return;
        var packet = buffer[offset..(offset + count)];
        Advance();

        byte[]? response = packet.Length switch
        {
            1 => RespondToCommand(packet[0]),
            2 => packet[0] == HvProtocol.PingCommand[0] && packet[1] == HvProtocol.PingCommand[1]
                 ? packet : null,
            3 => WriteRegister(packet),
            _ => null,
        };
        if (response is null) return;   // drop silently, like the firmware does

        lock (_lock)
            foreach (byte b in response) _pending.Enqueue(b);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
                throw new TimeoutException("Simulation: no response pending");

            int n = 0;
            while (n < count && _pending.Count > 0) buffer[offset + n++] = _pending.Dequeue();
            return n;
        }
    }

    // ---------------------------------------------------------------- device model

    /// <summary>Produces realistic values that drift over time.</summary>
    private void Advance()
    {
        double t = _clock.Elapsed.TotalSeconds;

        // Current: slow oscillation, charge/discharge (-120 .. +80 A)
        double current = 80.0 * Math.Sin(t / 7.0) - 40.0 * Math.Sin(t / 3.0);

        for (int i = 0; i < HvProtocol.CellCount; i++)
        {
            double sag = current * 0.0000012 * (i % 5 + 1);       // per-cell internal resistance
            _volts[i] += (_rng.NextDouble() - 0.5) * 0.003 - sag;
            _volts[i] = Math.Clamp(_volts[i], 3.30, 4.19);

            _temps[i] += (_rng.NextDouble() - 0.5) * 0.10 + Math.Abs(current) * 0.00008;
            _temps[i] = Math.Clamp(_temps[i], 15.0, 78.0);
        }

        // Faithfully mirrors the firmware 94 -> 20 remap (main.cpp:971)
        _temps[94] = _temps[20];

        // Fault demo: clean for the first 12 s, then a few faults in rotation
        ushort faults = ((int)(t / 12.0) % 4) switch
        {
            1 => 1 << 2,     // cell overvoltage
            2 => 1 << 6,     // cell overtemperature
            3 => 1 << 13,    // precharge timeout
            _ => 0,
        };

        ushort outputs = 0;
        if (t > 2.0) outputs |= OutputBits.Pre;
        if (t > 5.0) outputs |= OutputBits.Air;
        if (faults != 0) outputs |= OutputBits.Err;

        double packV = _volts.Sum();
        double avgV = packV / HvProtocol.CellCount;
        double avgT = _temps.Average();

        _registers[Reg.Faults] = faults;
        _registers[Reg.Outputs] = outputs;
        _registers[Reg.PackVoltage] = (ushort)(packV * 100);
        _registers[Reg.PackCurrent] = unchecked((ushort)(short)(current * 10));
        _registers[Reg.MaxCellVoltage] = (ushort)(_volts.Max() * 100);
        _registers[Reg.MinCellVoltage] = (ushort)(_volts.Min() * 100);
        _registers[Reg.TotalCellVoltage] = (ushort)(packV * 100);
        _registers[Reg.MaxCellTemp] = unchecked((ushort)(short)(_temps.Max() * 100));
        _registers[Reg.MinCellTemp] = unchecked((ushort)(short)(_temps.Min() * 100));
        _registers[Reg.AvgCellVoltage] = (ushort)(avgV * 100);
        _registers[Reg.AvgCellTemp] = unchecked((ushort)(short)(avgT * 100));
        _registers[Reg.MaxSlaveTemp] = unchecked((ushort)(short)(5200));      // 52.00 C
        _registers[Reg.EstimatedSoc] =
            (ushort)(Math.Clamp((avgV - 3.30) / (4.15 - 3.30), 0, 1) * 10000);

        // Charger tracks the pack while "charging", so those rows move in the table too
        _registers[5] = (ushort)(packV * 100);                        // CHARGER_ACTUAL_VOLTAGE
        _registers[6] = (ushort)(58 + 4 * Math.Sin(t / 5.0));         // CHARGER_ACTUAL_CURRENT
    }

    private byte[]? RespondToCommand(byte cmd) => cmd switch
    {
        HvProtocol.CmdCellVoltages => CellFrame(_volts, HvProtocol.CmdCellVoltages),
        HvProtocol.CmdCellTemps => CellFrame(_temps, HvProtocol.CmdCellTemps),
        HvProtocol.CmdBalance => BalanceFrame(),
        _ => cmd < HvProtocol.MaxRegisterIndexExclusive
             ? RegisterFrame(cmd, _registers[cmd])
             : null,                       // firmware: idx >= 50 gets no response
    };

    private byte[] WriteRegister(byte[] packet)
    {
        byte idx = packet[0];
        // Match firmware: silent drop for SoC/NV and shadowed indices
        if (!WriteGuard.IsWritable(idx)) return Array.Empty<byte>();
        _registers[idx] = (ushort)((packet[2] << 8) | packet[1]);
        return RegisterFrame(idx, _registers[idx]);
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

    /// <summary>Cells more than ALLOWED_DISBALANCE above the mean are balancing.</summary>
    private byte[] BalanceFrame()
    {
        double threshold = _volts.Average() + Math.Max(1, (int)_registers[Reg.AllowedDisbalance]) / 1000.0;
        var f = new byte[HvProtocol.BalanceFrameLength];
        for (int seg = 0; seg < HvProtocol.SegmentCount; seg++)
        {
            ushort dcc = 0;
            for (int c = 0; c < HvProtocol.CellsPerSegment; c++)
                if (_volts[seg * HvProtocol.CellsPerSegment + c] > threshold)
                    dcc |= (ushort)(1 << c);
            BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(seg * 2, 2), dcc);
        }
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
}
