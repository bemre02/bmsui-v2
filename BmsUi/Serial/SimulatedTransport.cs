using System.Buffers.Binary;
using System.Diagnostics;
using BmsUi.Protocol;

namespace BmsUi.Serial;

/// <summary>
/// Uygulama ici sanal HV BMS cihazi — sanal COM portu / surucu gerektirmez.
/// ISerialTransport'u gercek portla ayni arayuzden uygular, boylece SerialLink,
/// CRC dogrulama, parser ve PollWorker AYNI kod yolundan gecer; yalnizca baytlarin
/// kaynagi degisir.
///
/// Firmware davranisi taklit edilir: gelen paketin UZUNLUGUNA gore komut ayristirilir
/// (main.cpp:1958 switch (usblen)) ve her cevap dogru CRC8 ile biter.
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
        _volts[42] -= 0.12;                  // belirgin bir min hucre
        _volts[7] += 0.09;                   // belirgin bir maks hucre
        foreach (int hot in new[] { 7, 42, 83 }) _temps[hot] += 14.0;

        _registers[Reg.AllowedDisbalance] = 20;
        _registers[Reg.PrechargePercentage] = 95;
        _registers[Reg.PrechargeTimeout] = 5000;
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
        if (response is null) return;   // firmware gibi sessizce dusur

        lock (_lock)
            foreach (byte b in response) _pending.Enqueue(b);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
                throw new TimeoutException("Simulasyon: cevap yok");

            int n = 0;
            while (n < count && _pending.Count > 0) buffer[offset + n++] = _pending.Dequeue();
            return n;
        }
    }

    // ---------------------------------------------------------------- cihaz modeli

    /// <summary>Zamanla surukelenen gercekci degerler uretir.</summary>
    private void Advance()
    {
        double t = _clock.Elapsed.TotalSeconds;

        // Akim: yavas salinim, sarj/desarj (-120 .. +80 A)
        double current = 80.0 * Math.Sin(t / 7.0) - 40.0 * Math.Sin(t / 3.0);

        for (int i = 0; i < HvProtocol.CellCount; i++)
        {
            double sag = current * 0.0000012 * (i % 5 + 1);       // hucreye gore ic direnc
            _volts[i] += (_rng.NextDouble() - 0.5) * 0.003 - sag;
            _volts[i] = Math.Clamp(_volts[i], 3.30, 4.19);

            _temps[i] += (_rng.NextDouble() - 0.5) * 0.10 + Math.Abs(current) * 0.00008;
            _temps[i] = Math.Clamp(_temps[i], 15.0, 78.0);
        }

        // Firmware'deki 94 -> 20 remap'i (main.cpp:971) sadakatle taklit edilir
        _temps[94] = _temps[20];

        // Fault demosu: ilk 12 sn temiz, sonra sirayla birkac fault
        ushort faults = ((int)(t / 12.0) % 4) switch
        {
            1 => 1 << 2,     // hucre asiri voltaj
            2 => 1 << 6,     // hucre asiri sicaklik
            3 => 1 << 13,    // precharge zaman asimi
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
    }

    private byte[]? RespondToCommand(byte cmd) => cmd switch
    {
        HvProtocol.CmdCellVoltages => CellFrame(_volts, HvProtocol.CmdCellVoltages),
        HvProtocol.CmdCellTemps => CellFrame(_temps, HvProtocol.CmdCellTemps),
        HvProtocol.CmdBalance => BalanceFrame(),
        _ => cmd < HvProtocol.MaxRegisterIndexExclusive
             ? RegisterFrame(cmd, _registers[cmd])
             : null,                       // firmware: idx >= 50 cevapsiz
    };

    private byte[] WriteRegister(byte[] packet)
    {
        byte idx = packet[0];
        if (idx >= HvProtocol.MaxRegisterIndexExclusive) return Array.Empty<byte>();
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

    /// <summary>Ortalamanin ALLOWED_DISBALANCE kadar ustundeki hucreler balansta.</summary>
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
