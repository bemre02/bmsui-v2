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

/// <summary>MAINBUFFER indices (firmware Core/Inc/main.h).</summary>
public static class Reg
{
    public const byte Faults              = 0;
    public const byte Outputs             = 1;
    public const byte ChargingState       = 2;
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
    public const byte EstimatedSoc        = 17;  // x10000 (inspector Scale=100 → %)
    public const byte AllowedDisbalance   = 30;  // mV, writable
    public const byte PrechargePercentage = 32;  // writable
    public const byte PrechargeTimeout    = 33;  // writable
    public const byte SocNvMagic          = 48;  // EKF/NV — USB write blocked in FW
    public const byte SocNvBuildId        = 49;  // EKF/NV — USB write blocked in FW
}

/// <summary>OUTPUTS (idx 1) bit masks.</summary>
public static class OutputBits
{
    public const ushort Air = 1 << 0;
    public const ushort Pre = 1 << 1;
    public const ushort Err = 1 << 2;   // SDC / ERROR_OUT
}

/// <summary>FAULTS (idx 0) bit mask decoding — matches main.h FAULT_* bits 0..15.</summary>
public static class FaultBits
{
    public static readonly string[] Names =
    {
        "PEC / comms error",         // 0  FAULT_PEC_COMMS
        "Cell undervoltage",         // 1  FAULT_CELL_UV
        "Cell overvoltage",          // 2  FAULT_CELL_OV
        "Discharge overcurrent",     // 3  FAULT_DISCHARGE_OC
        "Charge overcurrent",        // 4  FAULT_CHARGE_OC
        "Cell undertemperature",     // 5  FAULT_CELL_UNDERTEMP
        "Cell overtemperature",      // 6  FAULT_CELL_OVERTEMP
        "Cell open wire",            // 7
        "No current sensor",         // 8  FAULT_NO_CURRENT_SENS
        "Slave overtemperature",     // 9  FAULT_SLAVE_OVERTEMP
        "Pack undervoltage",         // 10 FAULT_PACK_UV
        "Pack overvoltage",          // 11 FAULT_PACK_OV
        "Temperature open wire",     // 12
        "Precharge timeout",         // 13 FAULT_PRECHARGE_TO
        "Measurement stale",         // 14 FAULT_MEAS_STALE
        "ADBMS ref drift",           // 15 FAULT_ADBMS_REF_DRIFT
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

/// <summary>
/// USB write denylist — firmware USB_Task case 3 refuses these without a reply
/// (SoC/NV owned by EKF + soc_nv). Also shadowed command indices are not writable.
/// </summary>
public static class WriteGuard
{
    public static bool IsWriteProtected(byte index)
        => index == Reg.EstimatedSoc
           || index == Reg.SocNvMagic
           || index == Reg.SocNvBuildId
           || HvProtocol.IsShadowedRegister(index)
           || index >= HvProtocol.MaxRegisterIndexExclusive;

    public static bool IsWritable(byte index)
        => HvProtocol.IsValidRegister(index) && !IsWriteProtected(index);
}
