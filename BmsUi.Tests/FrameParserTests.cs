using BmsUi.Protocol;
using Xunit;

public class FrameParserTests
{
    private static short[] Ramp(short start)
    {
        var a = new short[HvProtocol.CellCount];
        for (int i = 0; i < a.Length; i++) a[i] = (short)(start + i);
        return a;
    }

    [Fact]
    public void TryParseCellVoltages_ValidFrame_ScalesByHundred()
    {
        var frame = FrameBuilder.CellFrame(Ramp(360), HvProtocol.CmdCellVoltages);
        var volts = new double[96];
        Assert.True(FrameParser.TryParseCellVoltages(frame, volts, out var err), err);
        Assert.Equal(3.60, volts[0], 3);
        Assert.Equal(4.55, volts[95], 3);   // 360+95 = 455 -> 4.55 V
    }

    [Fact]
    public void TryParseCellTemps_NegativeValues_StaySigned()
    {
        var raw = new short[96];
        raw[0] = -1250;    // -12.50 C
        raw[1] = 2537;     //  25.37 C
        raw[95] = -50;     //  -0.50 C
        var frame = FrameBuilder.CellFrame(raw, HvProtocol.CmdCellTemps);
        var temps = new double[96];
        Assert.True(FrameParser.TryParseCellTemps(frame, temps, out var err), err);
        Assert.Equal(-12.50, temps[0], 3);
        Assert.Equal(25.37, temps[1], 3);
        Assert.Equal(-0.50, temps[95], 3);
    }

    [Fact]
    public void TryParseCellVoltages_CorruptedCrc_Fails()
    {
        var frame = FrameBuilder.CellFrame(Ramp(360), HvProtocol.CmdCellVoltages);
        frame[193] ^= 0xFF;
        Assert.False(FrameParser.TryParseCellVoltages(frame, new double[96], out var err));
        Assert.Contains("CRC", err);
    }

    [Fact]
    public void TryParseCellVoltages_WrongId_Fails()
    {
        // Sicaklik cercevesi voltaj olarak ayristirilirsa reddedilmeli
        var frame = FrameBuilder.CellFrame(Ramp(360), HvProtocol.CmdCellTemps);
        Assert.False(FrameParser.TryParseCellVoltages(frame, new double[96], out var err));
        Assert.Contains("kimlik", err);
    }

    [Fact]
    public void TryParseCellVoltages_ShortFrame_Fails()
        => Assert.False(FrameParser.TryParseCellVoltages(new byte[193], new double[96], out _));

    [Fact]
    public void TryParseBalance_BitmapDecoded()
    {
        var dcc = new ushort[] { 0b0000_0000_0000_0101, 0, 0, 0, 0, 0b1000_0000_0000_0000 };
        var frame = FrameBuilder.BalanceFrame(dcc);
        var target = new ushort[6];
        Assert.True(FrameParser.TryParseBalance(frame, target, out var err), err);
        Assert.Equal(0b101, target[0]);
        Assert.Equal(0x8000, target[5]);
    }

    [Fact]
    public void TryParseRegister_MatchingIndex_ReturnsValue()
    {
        var frame = FrameBuilder.RegisterFrame(Reg.PackVoltage, 41234);
        Assert.True(FrameParser.TryParseRegister(frame, Reg.PackVoltage, out var v, out var err), err);
        Assert.Equal(41234, v);
    }

    [Fact]
    public void TryParseRegister_IndexMismatch_Fails()
    {
        var frame = FrameBuilder.RegisterFrame(Reg.PackVoltage, 100);
        Assert.False(FrameParser.TryParseRegister(frame, Reg.PackCurrent, out _, out var err));
        Assert.Contains("kimlik", err);
    }

    [Fact]
    public void TryParseRegister_NegativeCurrent_InterpretedBySigned()
    {
        // -37.5 A -> raw -375 -> uint16 olarak 65161
        var frame = FrameBuilder.RegisterFrame(Reg.PackCurrent, unchecked((ushort)-375));
        Assert.True(FrameParser.TryParseRegister(frame, Reg.PackCurrent, out var v, out _));
        Assert.Equal(-375, (short)v);
        Assert.Equal(-37.5, (short)v / 10.0, 3);
    }
}
