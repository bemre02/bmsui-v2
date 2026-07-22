using BmsUi.Model;
using Xunit;

public class DisplaySettingsTests
{
    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), $"bmsui_settings_{Guid.NewGuid():N}.json");

    [Fact]
    public void Defaults_MatchFirmwareThresholds()
    {
        var s = new DisplaySettings();
        Assert.Equal(2.50, s.VoltageAlarmLow, 3);
        Assert.Equal(4.23, s.VoltageAlarmHigh, 3);
        Assert.Equal(80.0, s.TempAlarmHigh, 3);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        string path = TempFile();
        try
        {
            new DisplaySettings
            {
                VoltageAlarmLow = 3.10,
                VoltageAlarmHigh = 4.05,
                VoltageScaleLow = 3.50,
                VoltageScaleHigh = 4.00,
                TempAlarmHigh = 55.0,
            }.Save(path);

            var loaded = DisplaySettings.Load(path);
            Assert.Equal(3.10, loaded.VoltageAlarmLow, 3);
            Assert.Equal(4.05, loaded.VoltageAlarmHigh, 3);
            Assert.Equal(3.50, loaded.VoltageScaleLow, 3);
            Assert.Equal(55.0, loaded.TempAlarmHigh, 3);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
        => Assert.Equal(4.23, DisplaySettings.Load(TempFile()).VoltageAlarmHigh, 3);

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, "{ this is not json ]]");
            Assert.Equal(2.50, DisplaySettings.Load(path).VoltageAlarmLow, 3);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Normalized_FixesInvertedRanges()
    {
        // An inverted range breaks drawing (divide by zero / empty ramp) — fix it silently
        var s = new DisplaySettings
        {
            VoltageScaleLow = 4.10,
            VoltageScaleHigh = 3.20,
            TempScaleLow = 60,
            TempScaleHigh = 15,
        }.Normalized();

        Assert.True(s.VoltageScaleHigh > s.VoltageScaleLow);
        Assert.True(s.TempScaleHigh > s.TempScaleLow);
    }

    [Fact]
    public void Save_CreatesDirectoryWhenMissing()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bmsui_dir_{Guid.NewGuid():N}");
        string path = Path.Combine(dir, "settings.json");
        try
        {
            new DisplaySettings().Save(path);
            Assert.True(File.Exists(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
