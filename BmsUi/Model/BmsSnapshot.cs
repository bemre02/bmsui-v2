using BmsUi.Protocol;

namespace BmsUi.Model;

/// <summary>
/// Full pack state. PollWorker fills it every tick and hands a copy to the UI; fields
/// that were not refreshed on that tick carry their previous values, and the timestamps
/// tell the UI how old each group of data is.
/// </summary>
public sealed class BmsSnapshot
{
    public double[] CellVoltages { get; private set; } = new double[HvProtocol.CellCount];
    public double[] CellTemps { get; private set; } = new double[HvProtocol.CellCount];
    public ushort[] BalanceBitmaps { get; private set; } = new ushort[HvProtocol.SegmentCount];
    public ushort[] Registers { get; private set; } = new ushort[HvProtocol.MaxRegisterIndexExclusive];
    public bool[] RegisterValid { get; private set; } = new bool[HvProtocol.MaxRegisterIndexExclusive];

    public DateTime VoltagesAt { get; private set; }
    public DateTime TempsAt { get; private set; }
    public DateTime BalanceAt { get; private set; }
    public DateTime RegistersAt { get; private set; }

    public static BmsSnapshot Empty() => new();

    public void SetVoltages(double[] v) { Array.Copy(v, CellVoltages, v.Length); VoltagesAt = DateTime.Now; }
    public void SetTemps(double[] t) { Array.Copy(t, CellTemps, t.Length); TempsAt = DateTime.Now; }
    public void SetBalance(ushort[] b) { Array.Copy(b, BalanceBitmaps, b.Length); BalanceAt = DateTime.Now; }

    public void SetRegister(byte idx, ushort value)
    {
        Registers[idx] = value;
        RegisterValid[idx] = true;
        RegistersAt = DateTime.Now;
    }

    public ushort Faults => Registers[Reg.Faults];
    public ushort Outputs => Registers[Reg.Outputs];
    public bool AirClosed => (Outputs & OutputBits.Air) != 0;
    public bool PreActive => (Outputs & OutputBits.Pre) != 0;
    public bool ErrActive => (Outputs & OutputBits.Err) != 0;

    public double PackVoltage => Registers[Reg.PackVoltage] / 100.0;
    public double PackCurrent => (short)Registers[Reg.PackCurrent] / 10.0;

    /// <summary>
    /// Power is computed host-side: the firmware has MAINBUFFER[41]=POWER, but index 41
    /// is shadowed by the 0x29 command so it can never be read over USB (main.cpp:1960).
    /// </summary>
    public double PowerKw => PackVoltage * PackCurrent / 1000.0;

    public double SocPercent => Registers[Reg.EstimatedSoc] / 10000.0 * 100.0;
    public double TotalCellVoltage => Registers[Reg.TotalCellVoltage] / 100.0;
    public double MaxSlaveTemp => (short)Registers[Reg.MaxSlaveTemp] / 100.0;

    // Summaries as reported by the firmware (for cross-checking the host-side maths)
    public double FwMaxCellVoltage => Registers[Reg.MaxCellVoltage] / 100.0;
    public double FwMinCellVoltage => Registers[Reg.MinCellVoltage] / 100.0;
    public double FwAvgCellVoltage => Registers[Reg.AvgCellVoltage] / 100.0;
    public double FwMaxCellTemp => (short)Registers[Reg.MaxCellTemp] / 100.0;
    public double FwMinCellTemp => (short)Registers[Reg.MinCellTemp] / 100.0;
    public double FwAvgCellTemp => (short)Registers[Reg.AvgCellTemp] / 100.0;

    public CellStat VoltageStats => CellStats.Voltage(CellVoltages);
    public CellStat TempStats => CellStats.Temperature(CellTemps);

    /// <summary>Overall mean, per-segment sigma and cell marks (96 elements, cheap).</summary>
    public CellAnalysis VoltageAnalysis => CellAnalysis.Compute(CellVoltages, static v => v >= 0.5);

    /// <summary>cell: linear index 0..95 (segment*16 + cell).</summary>
    public bool IsBalancing(int cell)
    {
        int seg = cell / HvProtocol.CellsPerSegment;
        int bit = cell % HvProtocol.CellsPerSegment;
        return (BalanceBitmaps[seg] & (1 << bit)) != 0;
    }

    public int BalancingCount()
    {
        int n = 0;
        for (int i = 0; i < HvProtocol.CellCount; i++) if (IsBalancing(i)) n++;
        return n;
    }

    public BmsSnapshot Clone() => new()
    {
        CellVoltages = (double[])CellVoltages.Clone(),
        CellTemps = (double[])CellTemps.Clone(),
        BalanceBitmaps = (ushort[])BalanceBitmaps.Clone(),
        Registers = (ushort[])Registers.Clone(),
        RegisterValid = (bool[])RegisterValid.Clone(),
        VoltagesAt = VoltagesAt,
        TempsAt = TempsAt,
        BalanceAt = BalanceAt,
        RegistersAt = RegistersAt,
    };
}
