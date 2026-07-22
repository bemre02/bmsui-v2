using System.Globalization;

namespace BmsUi.Protocol;

public enum RegisterGroup { Status, Charger, Pack, Config, Unnamed }

public readonly record struct RegisterDescriptor(
    byte Index, string Name, string Unit, double Scale, bool Signed, RegisterGroup Group)
{
    public bool IsKnown => Name.Length > 0;

    /// <summary>FAULTS and OUTPUTS carry bit fields, so a scaled number is meaningless.</summary>
    public bool IsBitMask => Index is Reg.Faults or Reg.Outputs;

    /// <summary>Decimals implied by the scale: 100 -> 2, 10 -> 1, 1 -> 0.</summary>
    public int Decimals => Scale >= 100 ? 2 : Scale >= 10 ? 1 : 0;
}

/// <summary>
/// Index -> meaning for every readable MAINBUFFER register: name, unit, scale and sign.
///
/// The same knowledge lives implicitly inside BmsSnapshot's properties. It is duplicated here
/// as data because the inspector needs it for indices BmsSnapshot has no property for, and
/// because a table is the natural shape for it. BmsSnapshot is intentionally left alone.
///
/// Scales come from the firmware: main.h:93-123 for the indices, Initialize_System
/// (main.cpp:466-482) for the units the init values imply, and the delay registers are
/// compared against FreeRTOS ticks (main.cpp:699) so they are milliseconds.
/// </summary>
public static class RegisterCatalog
{
    private static readonly Dictionary<byte, RegisterDescriptor> Known = BuildKnown();

    public static IReadOnlyList<RegisterDescriptor> All { get; } = BuildAll();

    public static RegisterDescriptor Describe(byte index)
        => Known.TryGetValue(index, out var d)
           ? d
           : new RegisterDescriptor(index, "", "", 1.0, false, RegisterGroup.Unnamed);

    /// <summary>
    /// The culture argument exists for the tests: the UI defaults to CurrentCulture so the
    /// table matches the rest of the window, but a test asserting on "374.41" would then pass
    /// or fail depending on the machine's language.
    /// </summary>
    public static string FormatRaw(byte index, ushort raw, IFormatProvider? culture = null)
    {
        var d = Describe(index);
        return d.IsBitMask
            ? $"{raw}  (0x{raw:X4})"
            : raw.ToString(culture ?? CultureInfo.CurrentCulture);
    }

    public static string FormatValue(byte index, ushort raw, IFormatProvider? culture = null)
    {
        var d = Describe(index);
        if (d.IsBitMask) return $"0x{raw:X4}";

        double value = (d.Signed ? (short)raw : raw) / d.Scale;
        string number = value.ToString($"F{d.Decimals}", culture ?? CultureInfo.CurrentCulture);
        return d.Unit.Length == 0 ? number : $"{number} {d.Unit}";
    }

    public static string FormatNote(byte index, ushort raw)
    {
        if (index == Reg.Faults)
        {
            var names = FaultBits.Decode(raw);
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }
        if (index == Reg.Outputs)
        {
            var on = new List<string>(3);
            if ((raw & OutputBits.Air) != 0) on.Add("AIR");
            if ((raw & OutputBits.Pre) != 0) on.Add("PRE");
            if ((raw & OutputBits.Err) != 0) on.Add("ERR");
            return on.Count == 0 ? "all open" : string.Join(" + ", on);
        }
        if (index == 2)
        {
            // Firmware enum CHARG_STATE (main.cpp:285-291). Note: the firmware writes this
            // register before assigning the value, so in practice it always reads NO_CHARGER.
            return raw switch
            {
                1 => "NO_CHARGER",
                2 => "CHARGING",
                3 => "BALANCING",
                4 => "CHARGING_COMPLETED",
                5 => "CHARGING_ERROR",
                _ => "",
            };
        }
        return "";
    }

    private static Dictionary<byte, RegisterDescriptor> BuildKnown()
    {
        var list = new List<RegisterDescriptor>
        {
            new(Reg.Faults,  "FAULTS",  "", 1, false, RegisterGroup.Status),
            new(Reg.Outputs, "OUTPUTS", "", 1, false, RegisterGroup.Status),

            new(2,  "CHARGING_STATE",         "",  1,   false, RegisterGroup.Charger),
            new(3,  "CHARGER_SET_VOLTAGE",    "V", 100, false, RegisterGroup.Charger),
            new(4,  "CHARGER_SET_CURRENT",    "A", 10,  false, RegisterGroup.Charger),
            new(5,  "CHARGER_ACTUAL_VOLTAGE", "V", 100, false, RegisterGroup.Charger),
            new(6,  "CHARGER_ACTUAL_CURRENT", "A", 10,  false, RegisterGroup.Charger),
            new(31, "CHARGE_CURRENT",         "A", 10,  true,  RegisterGroup.Charger),

            new(Reg.PackVoltage,      "PACK_VOLTAGE",       "V",  100, false, RegisterGroup.Pack),
            new(Reg.PackCurrent,      "PACK_CURRENT",       "A",  10,  true,  RegisterGroup.Pack),
            new(Reg.MaxCellVoltage,   "MAX_CELL_VOLTAGE",   "V",  100, false, RegisterGroup.Pack),
            new(Reg.MinCellVoltage,   "MIN_CELL_VOLTAGE",   "V",  100, false, RegisterGroup.Pack),
            new(Reg.TotalCellVoltage, "TOTAL_CELL_VOLTAGE", "V",  100, false, RegisterGroup.Pack),
            new(Reg.MaxCellTemp,      "MAX_CELL_TEMP",      "°C", 100, true,  RegisterGroup.Pack),
            new(Reg.MinCellTemp,      "MIN_CELL_TEMP",      "°C", 100, true,  RegisterGroup.Pack),
            new(Reg.AvgCellVoltage,   "AVG_CELL_VOLTAGE",   "V",  100, false, RegisterGroup.Pack),
            new(Reg.AvgCellTemp,      "AVG_CELL_TEMP",      "°C", 100, true,  RegisterGroup.Pack),
            new(Reg.MaxSlaveTemp,     "MAX_SLAVE_TEMP",     "°C", 100, true,  RegisterGroup.Pack),
            new(Reg.EstimatedSoc,     "ESTIMATED_SoC",      "%",  100, false, RegisterGroup.Pack),
            new(18, "OPEN_CIRCUIT_VOLTAGE", "V", 100, false, RegisterGroup.Pack),

            new(Reg.AllowedDisbalance,   "ALLOWED_DISBALANCE",   "mV", 1, false, RegisterGroup.Config),
            new(Reg.PrechargePercentage, "PRECHARGE_PERCENTAGE", "%",  1, false, RegisterGroup.Config),
            new(Reg.PrechargeTimeout,    "PRECHARGE_TIMEOUT",    "ms", 1, false, RegisterGroup.Config),
            new(34, "CHARGE_OVER_CURRENT_TRESHOLD",    "A",  10, true,  RegisterGroup.Config),
            new(35, "DISCHARGE_OVER_CURRENT_TRESHOLD", "A",  10, true,  RegisterGroup.Config),
            new(36, "OVER_VOLTAGE_ERROR_DELAY",        "ms", 1,  false, RegisterGroup.Config),
            new(37, "UNDER_VOLTAGE_ERROR_DELAY",       "ms", 1,  false, RegisterGroup.Config),
            new(38, "OVER_CURRENT_ERROR_DELAY",        "ms", 1,  false, RegisterGroup.Config),
            new(39, "OPEN_WIRE_ERROR_DELAY",           "ms", 1,  false, RegisterGroup.Config),
            new(40, "HEAT_ERROR_DELAY",                "ms", 1,  false, RegisterGroup.Config),
        };
        return list.ToDictionary(d => d.Index);
    }

    private static IReadOnlyList<RegisterDescriptor> BuildAll()
    {
        var all = new List<RegisterDescriptor>();
        for (byte i = 0; i < HvProtocol.MaxRegisterIndexExclusive; i++)
        {
            if (!HvProtocol.IsValidRegister(i)) continue;   // skips 41/42/43
            all.Add(Describe(i));
        }
        return all.OrderBy(d => (int)d.Group).ThenBy(d => d.Index).ToList();
    }
}
