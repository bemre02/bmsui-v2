using BmsUi.Ui;
using Xunit;

public class HeatmapTests
{
    [Fact]
    public void VoltageRamp_RunsRedToYellowToGreen()
    {
        // BMS okumasi: dusuk = kirmizi, orta = sari, yuksek = yesil
        var ramp = Heatmap.VoltageRamp;
        var low = ramp[0];
        var mid = ramp[ramp.Length / 2];
        var high = ramp[^1];

        Assert.True(low.R > low.G && low.R > low.B, "dusuk uc kirmizi degil");
        Assert.True(mid.R > 150 && mid.G > 130 && mid.B < 100, "orta sari degil");
        Assert.True(high.G > high.R && high.G > high.B, "yuksek uc yesil degil");
    }

    [Fact]
    public void VoltageRamp_GetsGreenerAsValueRises()
    {
        var ramp = Heatmap.VoltageRamp;
        var low = Heatmap.Sequential(3.25, 3.20, 4.15, ramp);
        var high = Heatmap.Sequential(4.10, 3.20, 4.15, ramp);
        Assert.True(high.G - high.R > low.G - low.R, "yuksek deger daha yesil degil");
    }

    [Fact]
    public void TemperatureRamp_IsMonotonicallyLighter()
    {
        var ramp = Heatmap.TemperatureRamp;
        for (int i = 1; i < ramp.Length; i++)
            Assert.True(Luminance(ramp[i]) > Luminance(ramp[i - 1]),
                        $"adim {i} bir oncekinden acik degil");
    }

    [Fact]
    public void Sequential_ClampsOutsideRange()
    {
        var ramp = Heatmap.VoltageRamp;
        Assert.Equal(ramp[0], Heatmap.Sequential(1.0, 3.2, 4.15, ramp));
        Assert.Equal(ramp[^1], Heatmap.Sequential(9.0, 3.2, 4.15, ramp));
    }

    [Fact]
    public void Sequential_MidpointLandsMidRamp()
    {
        var ramp = Heatmap.VoltageRamp;
        Assert.Equal(ramp[ramp.Length / 2], Heatmap.Sequential(3.675, 3.20, 4.15, ramp));
    }

    [Fact]
    public void TemperatureRamp_HigherValueIsLighter_OnDarkSurface()
    {
        var ramp = Heatmap.TemperatureRamp;
        var low = Heatmap.Sequential(18.0, 15.0, 60.0, ramp);
        var high = Heatmap.Sequential(58.0, 15.0, 60.0, ramp);
        Assert.True(Luminance(high) > Luminance(low));
    }

    [Theory]
    [InlineData(0.00, CellState.Invalid)]   // eksik / stale hucre
    [InlineData(2.40, CellState.Alarm)]     // esigin altinda
    [InlineData(4.30, CellState.Alarm)]     // esigin ustunde
    [InlineData(3.85, CellState.Normal)]
    public void VoltageState_ClassifiesAgainstUserThresholds(double v, CellState expected)
        => Assert.Equal(expected, Heatmap.VoltageState(v, 2.50, 4.23));

    [Theory]
    [InlineData(-273.0, CellState.Invalid)]
    [InlineData(-20.0, CellState.Alarm)]
    [InlineData(95.0, CellState.Alarm)]
    [InlineData(35.0, CellState.Normal)]
    public void TemperatureState_ClassifiesAgainstUserThresholds(double t, CellState expected)
        => Assert.Equal(expected, Heatmap.TemperatureState(t, -10.0, 80.0));

    [Fact]
    public void VoltageState_FollowsUserThresholds_NotFirmwareDefaults()
    {
        // Kullanici esigi daralttiginda alarm daha erken tetiklenmeli
        Assert.Equal(CellState.Normal, Heatmap.VoltageState(3.60, 2.50, 4.23));
        Assert.Equal(CellState.Alarm, Heatmap.VoltageState(3.60, 3.70, 4.10));
    }

    [Fact]
    public void Fill_AlarmKeepsRampColor_SoGridStaysReadable()
    {
        // Alarm dolguyu ele gecirseydi, esik yanlis ayarlaninca tum izgara tek renge
        // duser ve hicbir hucre digerinden ayirt edilemezdi
        var alarmFill = Heatmap.Fill(CellState.Alarm, 4.30, 3.20, 4.15, Heatmap.VoltageRamp);
        var normalFill = Heatmap.Fill(CellState.Normal, 4.30, 3.20, 4.15, Heatmap.VoltageRamp);
        Assert.Equal(normalFill, alarmFill);
        Assert.Contains(alarmFill, Heatmap.VoltageRamp);
    }

    [Fact]
    public void Fill_LowAlarmAndHighAlarm_AreDistinguishable()
    {
        // Ikisi de alarm ama biri kirmizi ucta biri yesil ucta olmali
        var low = Heatmap.Fill(CellState.Alarm, 2.00, 3.20, 4.15, Heatmap.VoltageRamp);
        var high = Heatmap.Fill(CellState.Alarm, 4.50, 3.20, 4.15, Heatmap.VoltageRamp);
        Assert.NotEqual(low, high);
    }

    [Fact]
    public void Fill_InvalidUsesMutedGray()
        => Assert.Equal(Heatmap.InvalidColor,
                        Heatmap.Fill(CellState.Invalid, 0.0, 3.20, 4.15, Heatmap.VoltageRamp));

    [Fact]
    public void InkOn_PicksReadableContrast()
    {
        // Koyu kirmizi dolgu -> beyaz yazi
        Assert.Equal(Color.White, Heatmap.InkOn(Heatmap.VoltageRamp[0]));
        // Parlak sari dolgu -> koyu yazi (beyaz okunmaz)
        Assert.Equal(Heatmap.DarkInk, Heatmap.InkOn(Heatmap.FromHex(0xEAB308)));
        // Acik krem dolgu -> koyu yazi
        Assert.Equal(Heatmap.DarkInk, Heatmap.InkOn(Heatmap.TemperatureRamp[^1]));
    }

    [Fact]
    public void InkOn_GreenEnd_UsesDarkInk()
    {
        // Regresyon: doygun yesil algisal parlaklik formulunde esigin altina dusup
        // beyaz yazi aliyordu; gercek kontrast koyu yazidan yana (8.6'ya karsi 2.3)
        Assert.Equal(Heatmap.DarkInk, Heatmap.InkOn(Heatmap.VoltageRamp[^1]));
    }

    [Fact]
    public void InkOn_AlwaysPicksTheHigherContrastOption()
    {
        foreach (var fill in Heatmap.VoltageRamp.Concat(Heatmap.TemperatureRamp))
        {
            double l = Heatmap.RelativeLuminance(fill);
            double white = 1.05 / (l + 0.05);
            double dark = (l + 0.05) / (Heatmap.RelativeLuminance(Heatmap.DarkInk) + 0.05);
            var expected = white >= dark ? Color.White : Heatmap.DarkInk;
            Assert.Equal(expected, Heatmap.InkOn(fill));
        }
    }

    [Fact]
    public void InkOn_ChosenInkAlwaysClears4_5To1()
    {
        // Secilen murekkep her ramp adiminda okunabilir olmali (WCAG AA metin esigi)
        foreach (var fill in Heatmap.VoltageRamp.Concat(Heatmap.TemperatureRamp))
        {
            var ink = Heatmap.InkOn(fill);
            double lf = Heatmap.RelativeLuminance(fill);
            double li = Heatmap.RelativeLuminance(ink);
            double ratio = (Math.Max(lf, li) + 0.05) / (Math.Min(lf, li) + 0.05);
            Assert.True(ratio >= 4.5, $"#{fill.R:X2}{fill.G:X2}{fill.B:X2} kontrasti {ratio:F2}");
        }
    }

    private static double Luminance(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
}
