using BmsUi.Protocol;
using Xunit;

public class HvProtocolTests
{
    [Fact]
    public void FaultNames_Has15Entries()
        => Assert.Equal(15, FaultBits.Names.Length);

    [Fact]
    public void Decode_NoBits_ReturnsEmpty()
        => Assert.Empty(FaultBits.Decode(0x0000));

    [Fact]
    public void Decode_Bit2AndBit13_ReturnsCellOvAndPrechargeTimeout()
    {
        var names = FaultBits.Decode((ushort)((1 << 2) | (1 << 13)));
        Assert.Equal(2, names.Count);
        Assert.Equal(FaultBits.Names[2], names[0]);
        Assert.Equal(FaultBits.Names[13], names[1]);
    }

    [Theory]
    [InlineData(0x29, true)]   // cell voltage command
    [InlineData(0x2A, true)]   // cell temperature command
    [InlineData(0x2B, true)]   // balance command
    [InlineData(0x08, false)]  // PACK_CURRENT — an ordinary register
    public void IsShadowedRegister_DetectsCommandCollisions(byte idx, bool expected)
        => Assert.Equal(expected, HvProtocol.IsShadowedRegister(idx));

    [Fact]
    public void IsValidRegister_RejectsIndexAt50AndAbove()
    {
        Assert.False(HvProtocol.IsValidRegister(50));
        Assert.False(HvProtocol.IsValidRegister(200));
        Assert.True(HvProtocol.IsValidRegister(49));
    }

    [Fact]
    public void FrameLengths_MatchFirmware()
    {
        Assert.Equal(194, HvProtocol.CellFrameLength);
        Assert.Equal(14, HvProtocol.BalanceFrameLength);
        Assert.Equal(4, HvProtocol.RegisterFrameLength);
        Assert.Equal(96, HvProtocol.CellCount);
    }
}
