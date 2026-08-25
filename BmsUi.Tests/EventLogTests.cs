using BmsUi.Model;
using BmsUi.Protocol;
using Xunit;

public class EventLogTests
{
    private static readonly DateTime T0 = new(2026, 7, 23, 14, 0, 0);
    private const ushort Overvoltage = 1 << 2;   // "Cell overvoltage"
    private const ushort Overtemp    = 1 << 6;   // "Cell overtemperature"

    [Fact]
    public void FirstSample_IsASilentBaseline()
    {
        var log = new EventLog();
        var emitted = log.Observe(Overvoltage, OutputBits.Air, T0);
        Assert.Empty(emitted);
        Assert.Empty(log.Events);
    }

    [Fact]
    public void SteadyState_EmitsNothing()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        var emitted = log.Observe(0, 0, T0.AddMilliseconds(100));
        Assert.Empty(emitted);
    }

    [Fact]
    public void RisingFaultBit_EmitsOneRaised()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        var emitted = log.Observe(Overvoltage, 0, T0.AddMilliseconds(100));

        var e = Assert.Single(emitted);
        Assert.Equal(PackEventType.FaultRaised, e.Type);
        Assert.Equal("Cell overvoltage", e.Label);
        Assert.Equal(EventSeverity.Critical, e.Severity);
        Assert.Null(e.Duration);
    }

    [Fact]
    public void FallingFaultBit_EmitsClearedWithDuration()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        log.Observe(Overvoltage, 0, T0.AddMilliseconds(1000));
        var emitted = log.Observe(0, 0, T0.AddMilliseconds(3300));

        var e = Assert.Single(emitted);
        Assert.Equal(PackEventType.FaultCleared, e.Type);
        Assert.Equal("Cell overvoltage", e.Label);
        // Exact-millisecond timestamps: the subtraction is exact, so no float tolerance is
        // needed. TimeSpan.FromSeconds(2.3) would differ by a tick against DateTime maths.
        Assert.Equal(TimeSpan.FromMilliseconds(2300), e.Duration);
        Assert.Equal(EventSeverity.Info, e.Severity);
    }

    [Fact]
    public void TwoBitsChangingInOneSample_EmitTwoEvents()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        var emitted = log.Observe(Overvoltage | Overtemp, 0, T0.AddMilliseconds(100));

        Assert.Equal(2, emitted.Count);
        Assert.Contains(emitted, x => x.Label == "Cell overvoltage");
        Assert.Contains(emitted, x => x.Label == "Cell overtemperature");
    }

    [Fact]
    public void OutputTransitions_CarryTheRightSeverity()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);

        var air = Assert.Single(log.Observe(0, OutputBits.Air, T0.AddMilliseconds(100)));
        Assert.Equal(PackEventType.OutputOn, air.Type);
        Assert.Equal("AIR closed", air.Label);
        Assert.Equal(EventSeverity.Info, air.Severity);

        var err = Assert.Single(log.Observe(0, OutputBits.Air | OutputBits.Err, T0.AddMilliseconds(200)));
        Assert.Equal("ERR raised", err.Label);
        Assert.Equal(EventSeverity.Critical, err.Severity);

        var errOff = Assert.Single(log.Observe(0, OutputBits.Air, T0.AddMilliseconds(300)));
        Assert.Equal(PackEventType.OutputOff, errOff.Type);
        Assert.Equal("ERR cleared", errOff.Label);
    }

    [Fact]
    public void RisingFaultBit15_EmitsAdbmsRefDrift()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        var emitted = log.Observe((ushort)(1 << 15), 0, T0.AddMilliseconds(100));

        var e = Assert.Single(emitted);
        Assert.Equal(PackEventType.FaultRaised, e.Type);
        Assert.Equal("ADBMS ref drift", e.Label);
        Assert.Equal(EventSeverity.Critical, e.Severity);
    }

    [Fact]
    public void ReconnectReBaselinesSilently()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        log.SetDisconnected();

        // A fault is present on the first sample after reconnect: no event, just a new baseline
        var afterGap = log.Observe(Overvoltage, 0, T0.AddSeconds(10));
        Assert.Empty(afterGap);

        // ...and a change from that baseline emits again
        var cleared = log.Observe(0, 0, T0.AddSeconds(11));
        Assert.Single(cleared);
    }

    [Fact]
    public void Disconnect_DoesNotEmitClears()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        log.Observe(Overvoltage, OutputBits.Air, T0.AddSeconds(1));
        int before = log.Events.Count;

        log.SetDisconnected();
        Assert.Equal(before, log.Events.Count);   // the disconnect itself added nothing
    }

    [Fact]
    public void RingBuffer_DropsOldestPastCapacity()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);

        // Toggle one bit on and off 1200 times -> 2400 events, capacity is 1000
        for (int i = 0; i < 1200; i++)
        {
            log.Observe(Overvoltage, 0, T0.AddMilliseconds(i * 2 + 1));
            log.Observe(0, 0, T0.AddMilliseconds(i * 2 + 2));
        }

        Assert.Equal(1000, log.Events.Count);
        Assert.Equal(1400, log.DroppedCount);
    }

    [Fact]
    public void Clear_EmptiesBufferButKeepsBaseline()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        log.Observe(Overvoltage, 0, T0.AddSeconds(1));
        Assert.NotEmpty(log.Events);

        log.Clear();
        Assert.Empty(log.Events);
        Assert.Equal(0, log.DroppedCount);

        // Baseline kept: an unchanged sample still emits nothing, no phantom "raised"
        Assert.Empty(log.Observe(Overvoltage, 0, T0.AddSeconds(2)));
    }
}
