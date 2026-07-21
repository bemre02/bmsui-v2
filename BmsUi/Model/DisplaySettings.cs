using System.Text.Json;

namespace BmsUi.Model;

/// <summary>
/// Kullanicinin UI'dan ayarladigi gosterim esikleri. YALNIZCA gorunumu etkiler —
/// bu degerlerin hicbiri BMS'e yazilmaz, cihazin kendi fault esikleri ayridir.
///
/// Varsayilanlar firmware esikleriyle ayni secildi (main.h:194-200), boylece kutudan
/// ciktigi haliyle UI alarmi ile BMS fault'u ortusur; kullanici isterse daraltir.
/// </summary>
public sealed class DisplaySettings
{
    // Firmware referanslari: CELL_UNDER/OVER_VOLTAGE_TRESHOLD, CELL_OVER_HEAT_TRESHOLD
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

    /// <summary>Bozuk/eksik dosyada varsayilanlara doner — UI asla acilmazlik etmez.</summary>
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

    /// <summary>Ters cevrilmis araliklari duzeltir (skala low >= high ise cizim bozulur).</summary>
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
