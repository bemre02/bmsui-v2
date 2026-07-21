namespace BmsUi.Protocol;

/// <summary>HV BMS USB CDC protokol sabitleri (firmware main.cpp USB_Task ile birebir).</summary>
public static class HvProtocol
{
    public const byte CmdCellVoltages = 0x29;   // 41
    public const byte CmdCellTemps    = 0x2A;   // 42
    public const byte CmdBalance      = 0x2B;   // 43

    public static readonly byte[] PingCommand = { 0x17, 0x71 };
    public const int PingResponseLength = 2;

    public const int CellCount       = 96;
    public const int SegmentCount    = 6;
    public const int CellsPerSegment = 16;

    public const int CellFrameLength     = 194;  // 96*2 + id + crc
    public const int BalanceFrameLength  = 14;   // 6*2 + id + crc
    public const int RegisterFrameLength = 4;    // 2 + id + crc

    public const byte MaxRegisterIndexExclusive = 50;  // firmware: idx < 50

    /// <summary>
    /// 41/42/43 indeksleri len=1 komutlarinda ozel komut olarak yakalandigi icin
    /// MAINBUFFER register'i olarak okunamaz (main.cpp:1960-1995).
    /// </summary>
    public static bool IsShadowedRegister(byte index)
        => index == CmdCellVoltages || index == CmdCellTemps || index == CmdBalance;

    public static bool IsValidRegister(byte index)
        => index < MaxRegisterIndexExclusive && !IsShadowedRegister(index);
}

/// <summary>MAINBUFFER indeksleri (firmware Core/Inc/main.h:93-123).</summary>
public static class Reg
{
    public const byte Faults              = 0;
    public const byte Outputs             = 1;
    public const byte PackVoltage         = 7;
    public const byte PackCurrent         = 8;   // isaretli, x10
    public const byte MaxCellVoltage      = 9;
    public const byte MinCellVoltage      = 10;
    public const byte TotalCellVoltage    = 11;
    public const byte MaxCellTemp         = 12;  // isaretli
    public const byte MinCellTemp         = 13;  // isaretli
    public const byte AvgCellVoltage      = 14;
    public const byte AvgCellTemp         = 15;
    public const byte MaxSlaveTemp        = 16;  // isaretli
    public const byte EstimatedSoc        = 17;  // x10000
    public const byte AllowedDisbalance   = 30;  // mV, yazilabilir
    public const byte PrechargePercentage = 32;  // yazilabilir
    public const byte PrechargeTimeout    = 33;  // yazilabilir
}

/// <summary>OUTPUTS (idx 1) bit maskeleri.</summary>
public static class OutputBits
{
    public const ushort Air = 1 << 0;
    public const ushort Pre = 1 << 1;
    public const ushort Err = 1 << 2;   // SDC / ERROR_OUT
}

/// <summary>FAULTS (idx 0) bit maskesi cozumlemesi.</summary>
public static class FaultBits
{
    public static readonly string[] Names =
    {
        "PEC / haberleşme hatası",      // 0
        "Hücre düşük voltaj",           // 1
        "Hücre aşırı voltaj",           // 2
        "Deşarj aşırı akım",            // 3
        "Şarj aşırı akım",              // 4
        "Hücre düşük sıcaklık",         // 5
        "Hücre aşırı sıcaklık",         // 6
        "Hücre kopuk kablo",            // 7
        "Akım sensörü yok",             // 8
        "Slave aşırı sıcaklık",         // 9
        "Paket düşük voltaj",           // 10
        "Paket aşırı voltaj",           // 11
        "Sıcaklık kopuk kablo",         // 12
        "Precharge zaman aşımı",        // 13
        "Ölçüm bayat (stale)",          // 14
    };

    public static IReadOnlyList<string> Decode(ushort mask)
    {
        var list = new List<string>();
        for (int i = 0; i < Names.Length; i++)
            if ((mask & (1 << i)) != 0) list.Add(Names[i]);
        return list;
    }

    public static bool IsSet(ushort mask, int bit) => (mask & (1 << bit)) != 0;
}
