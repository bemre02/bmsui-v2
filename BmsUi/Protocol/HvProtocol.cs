namespace BmsUi.Protocol;

/// <summary>HV BMS USB CDC protocol constants (matches USB_Task in firmware main.cpp).</summary>
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
    /// For 1-byte packets the device intercepts 41/42/43 as dedicated commands, so those
    /// indices can never be read back as MAINBUFFER registers (main.cpp:1960-1995).
    /// </summary>
    public static bool IsShadowedRegister(byte index)
        => index == CmdCellVoltages || index == CmdCellTemps || index == CmdBalance;

    public static bool IsValidRegister(byte index)
        => index < MaxRegisterIndexExclusive && !IsShadowedRegister(index);
}

/// <summary>MAINBUFFER indices (firmware Core/Inc/main.h:93-123).</summary>
public static class Reg
{
    public const byte Faults              = 0;
    public const byte Outputs             = 1;
    public const byte PackVoltage         = 7;
    public const byte PackCurrent         = 8;   // signed, x10
    public const byte MaxCellVoltage      = 9;
    public const byte MinCellVoltage      = 10;
    public const byte TotalCellVoltage    = 11;
    public const byte MaxCellTemp         = 12;  // signed
    public const byte MinCellTemp         = 13;  // signed
    public const byte AvgCellVoltage      = 14;
    public const byte AvgCellTemp         = 15;
    public const byte MaxSlaveTemp        = 16;  // signed
    public const byte EstimatedSoc        = 17;  // x10000
    public const byte AllowedDisbalance   = 30;  // mV, writable
    public const byte PrechargePercentage = 32;  // writable
    public const byte PrechargeTimeout    = 33;  // writable
}

/// <summary>OUTPUTS (idx 1) bit masks.</summary>
public static class OutputBits
{
    public const ushort Air = 1 << 0;
    public const ushort Pre = 1 << 1;
    public const ushort Err = 1 << 2;   // SDC / ERROR_OUT
}

/// <summary>FAULTS (idx 0) bit mask decoding.</summary>
public static class FaultBits
{
    public static readonly string[] Names =
    {
        "PEC / comms error",         // 0
        "Cell undervoltage",         // 1
        "Cell overvoltage",          // 2
        "Discharge overcurrent",     // 3
        "Charge overcurrent",        // 4
        "Cell undertemperature",     // 5
        "Cell overtemperature",      // 6
        "Cell open wire",            // 7
        "No current sensor",         // 8
        "Slave overtemperature",     // 9
        "Pack undervoltage",         // 10
        "Pack overvoltage",          // 11
        "Temperature open wire",     // 12
        "Precharge timeout",         // 13
        "Measurement stale",         // 14
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
