using BmsUi.Model;
using BmsUi.Polling;
using BmsUi.Protocol;
using BmsUi.Serial;
using Xunit;

/// <summary>
/// PollWorker + SerialLink + FrameParser + BmsSnapshot zincirinin uctan uca testi.
/// Cihaz yerine FakeDeviceTransport konusur (firmware gibi uzunluga gore ayristirir).
/// </summary>
public class PollWorkerTests
{
    /// <summary>Icinde hucre dizileri dolmus ilk snapshot'i bekler.</summary>
    private static BmsSnapshot? RunUntilCellsPopulated(FakeDeviceTransport device,
                                                       int timeoutMs = 4000)
    {
        using var link = new SerialLink(device);
        using var worker = new PollWorker(link);

        BmsSnapshot? captured = null;
        using var gotIt = new ManualResetEventSlim(false);

        worker.SnapshotReady += snapshot =>
        {
            if (snapshot.VoltagesAt != default && snapshot.BalanceAt != default)
            {
                captured ??= snapshot;
                gotIt.Set();
            }
            worker.NotifyUiIdle();      // UI'nin isini bitirdigini bildirmesi taklidi
        };

        worker.Start();
        gotIt.Wait(timeoutMs);
        worker.Stop();
        return captured;
    }

    [Fact]
    public void Worker_PollsDevice_AndFillsSnapshot()
    {
        var device = new FakeDeviceTransport();
        var s = RunUntilCellsPopulated(device);

        Assert.NotNull(s);
        Assert.Equal(3.60, s!.CellVoltages[0], 2);
        // Cihaz (uint16_t)(v*100.0) ile ASAGI KIRPAR: 4.075 -> 407 -> 4.07
        Assert.Equal(4.07, s.CellVoltages[95], 2);
        Assert.Equal(-5.0, s.CellTemps[0], 2);          // negatif sicaklik dogru okundu
        Assert.Equal(372.50, s.PackVoltage, 2);
        Assert.Equal(-123.4, s.PackCurrent, 2);         // isaretli akim
        Assert.Equal(73.0, s.SocPercent, 1);
        Assert.True(s.AirClosed);
        Assert.True(s.PreActive);
        Assert.True(FaultBits.IsSet(s.Faults, 2));
        Assert.True(FaultBits.IsSet(s.Faults, 13));
        Assert.True(s.IsBalancing(0));
        Assert.True(s.IsBalancing(95));
        Assert.False(s.IsBalancing(1));
    }

    [Fact]
    public void Worker_HandlesChunkedResponses()
    {
        var device = new FakeDeviceTransport { Chunked = true };
        var s = RunUntilCellsPopulated(device);

        Assert.NotNull(s);
        Assert.Equal(3.60, s!.CellVoltages[0], 2);
        Assert.Equal(4.07, s.CellVoltages[95], 2);
    }

    [Fact]
    public void Worker_RaisesConnectionLost_WhenDeviceStopsResponding()
    {
        var device = new FakeDeviceTransport { Mute = true };
        using var link = new SerialLink(device, TimeSpan.FromMilliseconds(20));
        using var worker = new PollWorker(link);

        string? reason = null;
        using var lost = new ManualResetEventSlim(false);
        worker.ConnectionLost += r => { reason = r; lost.Set(); };
        worker.SnapshotReady += _ => worker.NotifyUiIdle();

        worker.Start();
        bool fired = lost.Wait(4000);
        worker.Stop();

        Assert.True(fired, "ConnectionLost tetiklenmedi");
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Worker_ProcessesQueuedRegisterWrite()
    {
        var device = new FakeDeviceTransport();
        using var link = new SerialLink(device);
        using var worker = new PollWorker(link);
        worker.SnapshotReady += _ => worker.NotifyUiIdle();

        ushort? echo = null;
        using var done = new ManualResetEventSlim(false);

        worker.Start();
        worker.EnqueueWrite(Reg.AllowedDisbalance, 60, e => { echo = e; done.Set(); });
        bool completed = done.Wait(4000);
        worker.Stop();

        Assert.True(completed, "Yazma geri cagrisi calismadi");
        Assert.Equal((ushort)60, echo);
        Assert.Equal(60, device.Registers[Reg.AllowedDisbalance]);
    }

    [Fact]
    public void Worker_NeverPollsShadowedRegisters()
    {
        // 0x29/0x2A/0x2B register olarak sorgulanirsa SerialLink istisna atar;
        // zincirin bir tur donmesi bunun yasanmadigini kanitlar.
        var device = new FakeDeviceTransport();
        var s = RunUntilCellsPopulated(device);
        Assert.NotNull(s);
        Assert.True(device.CommandCount > 10);
    }
}
