using BmsUi.Model;
using BmsUi.Protocol;
using Xunit;

public class BmsSnapshotTests
{
    private static BmsSnapshot Make(Action<BmsSnapshot> tweak)
    {
        var s = BmsSnapshot.Empty();
        tweak(s);
        return s;
    }

    [Fact]
    public void VoltageStats_IgnoresZeroCells()
    {
        var v = new double[96];
        for (int i = 0; i < 96; i++) v[i] = 3.90;
        v[10] = 0.00;          // invalid / stale
        v[20] = 4.15;          // max
        v[30] = 3.55;          // min
        var stat = CellStats.Voltage(v);

        Assert.Equal(4.15, stat.Max, 3);
        Assert.Equal(20, stat.MaxIndex);
        Assert.Equal(3.55, stat.Min, 3);
        Assert.Equal(30, stat.MinIndex);
        Assert.Equal(95, stat.ValidCount);
    }

    [Fact]
    public void VoltageStats_AllInvalid_ReportsNoData()
        => Assert.False(CellStats.Voltage(new double[96]).HasData);

    [Fact]
    public void TemperatureStats_HandlesNegativeValues()
    {
        var t = new double[96];
        for (int i = 0; i < 96; i++) t[i] = 25.0;
        t[5] = -8.5;
        var stat = CellStats.Temperature(t);
        Assert.Equal(-8.5, stat.Min, 3);
        Assert.Equal(5, stat.MinIndex);
    }

    [Fact]
    public void PackCurrent_NegativeRegister_IsSigned()
    {
        var s = Make(x => x.SetRegister(Reg.PackCurrent, unchecked((ushort)-375)));
        Assert.Equal(-37.5, s.PackCurrent, 3);
    }

    [Fact]
    public void PowerKw_IsVoltageTimesCurrent()
    {
        var s = Make(x =>
        {
            x.SetRegister(Reg.PackVoltage, 40000);                       // 400.00 V
            x.SetRegister(Reg.PackCurrent, unchecked((ushort)1000));     // 100.0 A
        });
        Assert.Equal(400.0, s.PackVoltage, 3);
        Assert.Equal(100.0, s.PackCurrent, 3);
        Assert.Equal(40.0, s.PowerKw, 3);
    }

    [Fact]
    public void SocPercent_ScaledBy10000()
    {
        var s = Make(x => x.SetRegister(Reg.EstimatedSoc, 8250));
        Assert.Equal(82.50, s.SocPercent, 2);
    }

    [Fact]
    public void OutputBits_DecodedIntoFlags()
    {
        var s = Make(x => x.SetRegister(Reg.Outputs, OutputBits.Air | OutputBits.Err));
        Assert.True(s.AirClosed);
        Assert.False(s.PreActive);
        Assert.True(s.ErrActive);
    }

    [Fact]
    public void IsBalancing_ReadsBitFromSegmentBitmap()
    {
        var s = Make(x => x.SetBalance(new ushort[] { 0b0000_0000_0000_1000, 0, 0, 0, 0, 0 }));
        Assert.True(s.IsBalancing(3));       // segment 0, cell 3
        Assert.False(s.IsBalancing(4));
    }

    [Fact]
    public void IsBalancing_LastSegmentLastCell()
    {
        var maps = new ushort[6];
        maps[5] = 1 << 15;
        var s = Make(x => x.SetBalance(maps));
        Assert.True(s.IsBalancing(95));      // 5*16 + 15
    }

    [Fact]
    public void Clone_ProducesIndependentCopy()
    {
        var s = Make(x => x.SetRegister(Reg.Faults, 0x0004));
        var c = s.Clone();
        c.SetRegister(Reg.Faults, 0x0000);
        Assert.Equal(0x0004, s.Faults);
        Assert.Equal(0x0000, c.Faults);
    }

    [Fact]
    public void Clone_CopiesCellArraysByValue()
    {
        var volts = new double[96];
        volts[0] = 3.99;
        var s = Make(x => x.SetVoltages(volts));
        var c = s.Clone();
        c.CellVoltages[0] = 0.0;
        Assert.Equal(3.99, s.CellVoltages[0], 3);
    }
}
