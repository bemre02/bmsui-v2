using BmsUi.Model;
using BmsUi.Polling;
using BmsUi.Protocol;
using BmsUi.Serial;
using Xunit;

/// <summary>
/// Uygulama ici sanal cihaz. Gercek kartla ayni kod yolundan (SerialLink -> CRC -> parser)
/// gectigi icin uctan uca surulup dogrulanir.
/// </summary>
public class SimulatedTransportTests
{
    private static SerialLink OpenLink(out SimulatedTransport device)
    {
        device = new SimulatedTransport(seed: 1234);
        var link = new SerialLink(device);
        link.Open();
        return link;
    }

    [Fact]
    public void Ping_Echoes()
    {
        using var link = OpenLink(out _);
        Assert.True(link.Ping());
    }

    [Fact]
    public void CellVoltages_ParseIntoRealisticRange()
    {
        using var link = OpenLink(out _);
        var frame = link.Transact(new[] { HvProtocol.CmdCellVoltages },
                                  HvProtocol.CellFrameLength, HvProtocol.CmdCellVoltages);
        Assert.NotNull(frame);

        var volts = new double[HvProtocol.CellCount];
        Assert.True(FrameParser.TryParseCellVoltages(frame!, volts, out var err), err);
        Assert.All(volts, v => Assert.InRange(v, 3.30, 4.19));
    }

    [Fact]
    public void CellTemps_ParseAndMirrorCell94ToCell20()
    {
        using var link = OpenLink(out _);
        var frame = link.Transact(new[] { HvProtocol.CmdCellTemps },
                                  HvProtocol.CellFrameLength, HvProtocol.CmdCellTemps);
        Assert.NotNull(frame);

        var temps = new double[HvProtocol.CellCount];
        Assert.True(FrameParser.TryParseCellTemps(frame!, temps, out var err), err);
        Assert.All(temps, t => Assert.InRange(t, 15.0, 78.0));
        // Firmware remap'i (main.cpp:971) sadakatle taklit ediliyor
        Assert.Equal(temps[20], temps[94], 2);
    }

    [Fact]
    public void Registers_ReportSignedCurrentAndSaneVoltage()
    {
        using var link = OpenLink(out _);
        ushort? packV = link.ReadRegister(Reg.PackVoltage);
        ushort? packA = link.ReadRegister(Reg.PackCurrent);

        Assert.NotNull(packV);
        Assert.NotNull(packA);
        Assert.InRange(packV!.Value / 100.0, 300.0, 410.0);      // 96 hucre toplami
        Assert.InRange((short)packA!.Value / 10.0, -130.0, 90.0);
    }

    [Fact]
    public void ShadowedIndicesAreNeverAskedAsRegisters()
    {
        using var link = OpenLink(out _);
        Assert.Throws<ArgumentOutOfRangeException>(() => link.ReadRegister(HvProtocol.CmdCellVoltages));
    }

    [Fact]
    public void IndexAbove50_GetsNoResponse_LikeFirmware()
    {
        using var link = OpenLink(out _);
        // Dogrudan Transact ile firmware'in sessiz dusurmesini dogrula
        var frame = link.Transact(new byte[] { 60 }, HvProtocol.RegisterFrameLength, 60);
        Assert.Null(frame);
        Assert.Equal(1, link.TimeoutCount);
    }

    [Fact]
    public void PollWorker_DrivesSimulatedDevice_EndToEnd()
    {
        var device = new SimulatedTransport(seed: 7);
        using var link = new SerialLink(device);
        link.Open();
        using var worker = new PollWorker(link);

        BmsSnapshot? captured = null;
        using var ready = new ManualResetEventSlim(false);
        worker.SnapshotReady += s =>
        {
            if (s.VoltagesAt != default && s.BalanceAt != default) { captured ??= s; ready.Set(); }
            worker.NotifyUiIdle();
        };

        worker.Start();
        ready.Wait(4000);
        worker.Stop();

        Assert.NotNull(captured);
        Assert.True(captured!.VoltageStats.HasData);
        Assert.InRange(captured.VoltageStats.Avg, 3.30, 4.19);
        Assert.InRange(captured.PackVoltage, 300.0, 410.0);
        Assert.True(captured.TempStats.HasData);
        Assert.Equal(0, link.CrcErrorCount);
    }
}
