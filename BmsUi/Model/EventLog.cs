using BmsUi.Protocol;

namespace BmsUi.Model;

/// <summary>
/// A pure state machine over the FAULTS and OUTPUTS registers. It diffs each sample against the
/// previous one and emits a <see cref="PackEvent"/> per changed bit into an in-memory ring
/// buffer. It holds no timer and never reads the clock itself — the timestamp is passed in, so
/// durations are deterministic under test.
///
/// Two rules keep it from inventing events across a gap in the data: a disconnect emits nothing,
/// and the first sample after start or after a disconnect is a silent baseline. Only transitions
/// seen between two consecutive connected samples become events.
/// </summary>
public sealed class EventLog
{
    private const int Capacity = 1000;

    private static readonly (ushort Mask, string On, string Off, EventSeverity Severity)[] Outputs =
    {
        (OutputBits.Air, "AIR closed", "AIR opened",  EventSeverity.Info),
        (OutputBits.Pre, "PRE active", "PRE off",     EventSeverity.Info),
        (OutputBits.Err, "ERR raised", "ERR cleared", EventSeverity.Critical),
    };

    private readonly List<PackEvent> _events = new();
    private readonly DateTime[] _faultOnset = new DateTime[FaultBits.Names.Length];
    private ushort _prevFaults;
    private ushort _prevOutputs;
    private bool _hasBaseline;

    public IReadOnlyList<PackEvent> Events => _events;
    public int DroppedCount { get; private set; }

    public IReadOnlyList<PackEvent> Observe(ushort faults, ushort outputs, DateTime at)
    {
        if (!_hasBaseline)
        {
            _prevFaults = faults;
            _prevOutputs = outputs;
            for (int i = 0; i < _faultOnset.Length; i++)
                if ((faults & (1 << i)) != 0) _faultOnset[i] = at;
            _hasBaseline = true;
            return Array.Empty<PackEvent>();
        }

        var emitted = new List<PackEvent>();

        ushort faultsChanged = (ushort)(faults ^ _prevFaults);
        for (int i = 0; i < _faultOnset.Length; i++)
        {
            int bit = 1 << i;
            if ((faultsChanged & bit) == 0) continue;

            if ((faults & bit) != 0)
            {
                _faultOnset[i] = at;
                emitted.Add(new PackEvent(at, PackEventType.FaultRaised, FaultBits.Names[i],
                                          null, EventSeverity.Critical));
            }
            else
            {
                emitted.Add(new PackEvent(at, PackEventType.FaultCleared, FaultBits.Names[i],
                                          at - _faultOnset[i], EventSeverity.Info));
            }
        }

        foreach (var (mask, on, off, severity) in Outputs)
        {
            if (((outputs ^ _prevOutputs) & mask) == 0) continue;
            bool nowOn = (outputs & mask) != 0;
            emitted.Add(new PackEvent(at, nowOn ? PackEventType.OutputOn : PackEventType.OutputOff,
                                      nowOn ? on : off, null, severity));
        }

        foreach (var e in emitted)
        {
            _events.Add(e);
            if (_events.Count > Capacity) { _events.RemoveAt(0); DroppedCount++; }
        }

        _prevFaults = faults;
        _prevOutputs = outputs;
        return emitted;
    }

    /// <summary>Marks a break in the data. The next Observe becomes a silent baseline.</summary>
    public void SetDisconnected() => _hasBaseline = false;

    /// <summary>Empties the visible log but keeps the baseline, so nothing re-fires as raised.</summary>
    public void Clear()
    {
        _events.Clear();
        DroppedCount = 0;
    }
}
