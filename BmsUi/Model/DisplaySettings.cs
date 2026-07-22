using System.Text.Json;

namespace BmsUi.Model;

/// <summary>
/// Display thresholds the user sets from the UI. These affect the VIEW ONLY — none of
/// them is ever written to the BMS; the device keeps its own fault thresholds.
///
/// Defaults match the firmware thresholds (main.h:194-200) so that out of the box a UI
/// alarm lines up with a BMS fault; the user can tighten them afterwards.
/// </summary>
public sealed class DisplaySettings
{
    // Firmware references: CELL_UNDER/OVER_VOLTAGE_TRESHOLD, CELL_OVER_HEAT_TRESHOLD
    public const double FirmwareVoltageLow = 2.50;
    public const double FirmwareVoltageHigh = 4.23;
    public const double FirmwareTempHigh = 80.0;

    public double VoltageAlarmLow { get; set; } = FirmwareVoltageLow;
    public double VoltageAlarmHigh { get; set; } = FirmwareVoltageHigh;
    public double VoltageScaleLow { get; set; } = 3.20;
    public double VoltageScaleHigh { get; set; } = 4.15;

    public double TempAlarmLow { get; set; } = -10.0;
    public double TempAlarmHigh { get; set; } = FirmwareTempHigh;
    public double TempScaleLow { get; set; } = 15.0;
    public double TempScaleHigh { get; set; } = 60.0;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BmsUi", "settings.json");

    /// <summary>Falls back to defaults on a missing/corrupt file — the UI always opens.</summary>
    public static DisplaySettings Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            if (!File.Exists(path)) return new DisplaySettings();
            var loaded = JsonSerializer.Deserialize<DisplaySettings>(File.ReadAllText(path));
            return loaded?.Normalized() ?? new DisplaySettings();
        }
        catch
        {
            return new DisplaySettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Repairs inverted ranges (drawing breaks if scale low >= high).</summary>
    public DisplaySettings Normalized()
    {
        if (VoltageScaleHigh <= VoltageScaleLow) VoltageScaleHigh = VoltageScaleLow + 0.01;
        if (TempScaleHigh <= TempScaleLow) TempScaleHigh = TempScaleLow + 0.1;
        if (VoltageAlarmHigh <= VoltageAlarmLow) VoltageAlarmHigh = VoltageAlarmLow + 0.01;
        if (TempAlarmHigh <= TempAlarmLow) TempAlarmHigh = TempAlarmLow + 0.1;
        return this;
    }

    public DisplaySettings Clone() => (DisplaySettings)MemberwiseClone();
}
