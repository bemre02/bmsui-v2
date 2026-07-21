using BmsUi.Ui;
using Xunit;

public class HeatmapTests
{
    [Fact]
    public void VoltageRamp_IsMonotonicallyLighter()
    {
        // Sequential ramp tek hue + monoton aciklik olmali (CVD-guvenli olmasinin sarti)
        var ramp = Heatmap.VoltageRamp;
        for (int i = 1; i < ramp.Length; i++)
            Assert.True(Luminance(ramp[i]) > Luminance(ramp[i - 1]),
                        $"adim {i} bir oncekinden acik degil");
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
    public void Sequential_HigherValueIsLighter_OnDarkSurface()
    {
        var ramp = Heatmap.VoltageRamp;
        var low = Heatmap.Sequential(3.30, 3.20, 4.15, ramp);
        var high = Heatmap.Sequential(4.10, 3.20, 4.15, ramp);
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
    public void Fill_AlarmUsesStatusColor_NotARampStep()
    {
        var fill = Heatmap.Fill(CellState.Alarm, 4.30, 3.20, 4.15, Heatmap.VoltageRamp);
        Assert.Equal(Heatmap.AlarmColor, fill);
        Assert.DoesNotContain(fill, Heatmap.VoltageRamp);
    }

    [Fact]
    public void Fill_InvalidUsesMutedGray()
        => Assert.Equal(Heatmap.InvalidColor,
                        Heatmap.Fill(CellState.Invalid, 0.0, 3.20, 4.15, Heatmap.VoltageRamp));

    [Fact]
    public void InkOn_PicksReadableContrast()
    {
        Assert.Equal(Color.White, Heatmap.InkOn(Heatmap.VoltageRamp[0]));            // koyu dolgu
        Assert.Equal(Heatmap.FromHex(0x0B0B0B), Heatmap.InkOn(Heatmap.VoltageRamp[^1])); // acik dolgu
    }

    private static double Luminance(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
}
