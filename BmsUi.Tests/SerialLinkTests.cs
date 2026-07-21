using BmsUi.Protocol;
using BmsUi.Serial;
using Xunit;

public class SerialLinkTests
{
    [Fact]
    public void Ping_EchoReceived_ReturnsTrue()
    {
        var t = new FakeTransport();
        t.EnqueueResponse(new byte[] { 0x17, 0x71 });
        using var link = new SerialLink(t);
        Assert.True(link.Ping());
        Assert.Equal(new byte[] { 0x17, 0x71 }, t.Written[0]);
    }

    [Fact]
    public void Ping_WrongEcho_ReturnsFalse()
    {
        var t = new FakeTransport();
        t.EnqueueResponse(new byte[] { 0x00, 0x00 });
        using var link = new SerialLink(t);
        Assert.False(link.Ping());
    }

    [Fact]
    public void Ping_NoResponse_ReturnsFalseAndCountsTimeout()
    {
        var t = new FakeTransport();
        using var link = new SerialLink(t);
        Assert.False(link.Ping());
        Assert.Equal(1, link.TimeoutCount);
    }

    [Fact]
    public void Transact_ChunkedResponse_Reassembles()
    {
        var raw = new short[96];
        for (int i = 0; i < 96; i++) raw[i] = 400;
        var frame = FrameBuilder.CellFrame(raw, HvProtocol.CmdCellVoltages);

        var t = new FakeTransport();
        t.EnqueueChunked(frame, 64, 64, 64, 2);   // CDC 64 baytlik paketler
        using var link = new SerialLink(t);

        var got = link.Transact(new[] { HvProtocol.CmdCellVoltages },
                                HvProtocol.CellFrameLength, HvProtocol.CmdCellVoltages);
        Assert.NotNull(got);
        Assert.Equal(frame, got);
    }

    [Fact]
    public void Transact_CorruptCrc_ReturnsNullAndCountsCrcError()
    {
        var frame = FrameBuilder.RegisterFrame(Reg.PackVoltage, 1234);
        frame[3] ^= 0xFF;
        var t = new FakeTransport();
        t.EnqueueResponse(frame);
        using var link = new SerialLink(t);

        Assert.Null(link.Transact(new[] { Reg.PackVoltage },
                                  HvProtocol.RegisterFrameLength, Reg.PackVoltage));
        Assert.Equal(1, link.CrcErrorCount);
    }

    [Fact]
    public void Transact_WrongId_CountsIdMismatch()
    {
        var frame = FrameBuilder.RegisterFrame(Reg.PackVoltage, 1234);
        var t = new FakeTransport();
        t.EnqueueResponse(frame);
        using var link = new SerialLink(t);

        Assert.Null(link.Transact(new[] { Reg.PackCurrent },
                                  HvProtocol.RegisterFrameLength, Reg.PackCurrent));
        Assert.Equal(1, link.IdMismatchCount);
    }

    [Fact]
    public void Transact_DiscardsInputBufferBeforeWriting()
    {
        var t = new FakeTransport();
        t.EnqueueResponse(FrameBuilder.RegisterFrame(Reg.Faults, 0));
        using var link = new SerialLink(t);
        link.ReadRegister(Reg.Faults);
        Assert.Equal(1, t.DiscardCount);
    }

    [Fact]
    public void ReadRegister_ReturnsValue_AndResetsConsecutiveFailures()
    {
        var t = new FakeTransport();
        t.EnqueueResponse(FrameBuilder.RegisterFrame(Reg.Faults, 0x0006));
        using var link = new SerialLink(t);
        Assert.Equal((ushort)0x0006, link.ReadRegister(Reg.Faults));
        Assert.Equal(0, link.ConsecutiveFailures);
    }

    [Fact]
    public void ReadRegister_ShadowedIndex_ThrowsWithoutTouchingPort()
    {
        var t = new FakeTransport();
        using var link = new SerialLink(t);
        Assert.Throws<ArgumentOutOfRangeException>(() => link.ReadRegister(0x29));
        Assert.Empty(t.Written);
    }

    [Fact]
    public void WriteRegister_SendsThreeBytesLittleEndian()
    {
        var t = new FakeTransport();
        t.EnqueueResponse(FrameBuilder.RegisterFrame(Reg.AllowedDisbalance, 0x0102));
        using var link = new SerialLink(t);

        Assert.Equal((ushort)0x0102, link.WriteRegister(Reg.AllowedDisbalance, 0x0102));
        Assert.Equal(new byte[] { Reg.AllowedDisbalance, 0x02, 0x01 }, t.Written[0]);
    }

    [Fact]
    public void WriteCommand_IsSentAsSingleWriteCall()
    {
        // Firmware paket UZUNLUGUNA gore ayristirir; komut tek Write ile gitmeli
        var t = new FakeTransport();
        t.EnqueueResponse(FrameBuilder.RegisterFrame(Reg.Faults, 0));
        using var link = new SerialLink(t);
        link.ReadRegister(Reg.Faults);
        Assert.Single(t.Written);
        Assert.Single(t.Written[0]);
    }

    [Fact]
    public void ConsecutiveFailures_IncrementsOnRepeatedTimeouts()
    {
        var t = new FakeTransport();
        using var link = new SerialLink(t);
        link.ReadRegister(Reg.Faults);
        link.ReadRegister(Reg.Faults);
        Assert.Equal(2, link.ConsecutiveFailures);
    }
}
