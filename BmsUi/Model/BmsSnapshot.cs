using BmsUi.Protocol;

namespace BmsUi.Model;

/// <summary>
/// Paketin tam durumu. PollWorker her turda doldurup UI'ya kopyasini gonderir;
/// o turda yenilenmeyen alanlar onceki degerleriyle tasinir, zaman damgalari veri
/// yasini gosterir.
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
    /// Guc host'ta hesaplanir: firmware'de MAINBUFFER[41]=POWER var ama 0x29 komutuyla
    /// golgeli oldugu icin USB'den okunamaz (main.cpp:1960).
    /// </summary>
    public double PowerKw => PackVoltage * PackCurrent / 1000.0;

    public double SocPercent => Registers[Reg.EstimatedSoc] / 10000.0 * 100.0;
    public double TotalCellVoltage => Registers[Reg.TotalCellVoltage] / 100.0;
    public double MaxSlaveTemp => (short)Registers[Reg.MaxSlaveTemp] / 100.0;

    // Firmware'in bildirdigi ozetler (host hesabiyla capraz kontrol icin)
    public double FwMaxCellVoltage => Registers[Reg.MaxCellVoltage] / 100.0;
    public double FwMinCellVoltage => Registers[Reg.MinCellVoltage] / 100.0;
    public double FwAvgCellVoltage => Registers[Reg.AvgCellVoltage] / 100.0;
    public double FwMaxCellTemp => (short)Registers[Reg.MaxCellTemp] / 100.0;
    public double FwMinCellTemp => (short)Registers[Reg.MinCellTemp] / 100.0;
    public double FwAvgCellTemp => (short)Registers[Reg.AvgCellTemp] / 100.0;

    public CellStat VoltageStats => CellStats.Voltage(CellVoltages);
    public CellStat TempStats => CellStats.Temperature(CellTemps);

    /// <summary>Genel ortalama, segment bazlı σ ve hücre işaretleri (96 eleman, ucuz).</summary>
    public CellAnalysis VoltageAnalysis => CellAnalysis.Compute(CellVoltages, static v => v >= 0.5);

    /// <summary>cell: 0..95 lineer indeks (segment*16 + hucre).</summary>
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
