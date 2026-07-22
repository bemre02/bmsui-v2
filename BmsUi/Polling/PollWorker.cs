using System.Collections.Concurrent;
using System.Diagnostics;
using BmsUi.Model;
using BmsUi.Protocol;
using BmsUi.Serial;

namespace BmsUi.Polling;

/// <summary>
/// Background thread: the ONLY place that touches the port. Write requests coming from
/// the UI are queued and executed BETWEEN poll rounds, so two threads never write at once.
/// </summary>
public sealed class PollWorker : IDisposable
{
    private readonly SerialLink _link;
    private readonly BmsSnapshot _state = BmsSnapshot.Empty();
    private readonly ConcurrentQueue<WriteRequest> _writes = new();
    private readonly double[] _voltBuf = new double[HvProtocol.CellCount];
    private readonly double[] _tempBuf = new double[HvProtocol.CellCount];
    private readonly ushort[] _balBuf = new ushort[HvProtocol.SegmentCount];

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private int _uiBusy;   // 0 = idle, 1 = a UI update is in flight

    public const int MaxConsecutiveFailures = 5;

    public PollWorker(SerialLink link) => _link = link;

    /// <summary>
    /// Set while the Registers tab is visible. Off by default, so the extra sweep costs
    /// nothing when nobody is looking at it.
    /// </summary>
    public bool IncludeAllRegisters { get; set; }

    public event Action<BmsSnapshot>? SnapshotReady;
    public event Action<string>? ConnectionLost;

    private sealed record WriteRequest(byte Index, ushort Value, Action<ushort?> Callback);

    public void EnqueueWrite(byte index, ushort value, Action<ushort?> callback)
        => _writes.Enqueue(new WriteRequest(index, value, callback));

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Loop(_cts.Token))
        {
            IsBackground = true,
            Name = "BmsUi Poll Worker",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void Loop(CancellationToken token)
    {
        long tick = 0;
        var sw = Stopwatch.StartNew();

        // Read the rarely-changing settings once (keeps them out of the poll rotation)
        foreach (byte idx in PollSchedule.ConfigRegisters)
        {
            if (token.IsCancellationRequested) return;
            PollRegister(idx);
        }

        while (!token.IsCancellationRequested)
        {
            long dueAt = (tick + 1) * PollSchedule.TickIntervalMs;

            // 1) Pending writes (BEFORE the poll round, keeping the single-thread rule)
            while (_writes.TryDequeue(out var req))
            {
                ushort? echo = _link.WriteRegister(req.Index, req.Value);
                req.Callback(echo);
            }

            // 2) The poll items for this tick
            foreach (var item in PollSchedule.ItemsForTick(tick))
            {
                if (token.IsCancellationRequested) break;
                Poll(item);
            }

            // 3) Link health
            if (_link.ConsecutiveFailures >= MaxConsecutiveFailures)
            {
                ConnectionLost?.Invoke(_link.LastError ?? "Cihaz cevap vermiyor");
                return;
            }

            // 4) Publish to the UI — skip this round if the previous update is still
            //    running, otherwise the message queue piles up
            if (Interlocked.CompareExchange(ref _uiBusy, 1, 0) == 0)
            {
                var handler = SnapshotReady;
                if (handler is null) Interlocked.Exchange(ref _uiBusy, 0);
                else handler(_state.Clone());
            }

            // 5) Keep tick alignment
            int sleep = (int)(dueAt - sw.ElapsedMilliseconds);
            if (sleep > 0) token.WaitHandle.WaitOne(sleep);
            tick++;
        }
    }

    /// <summary>The UI reports it is done with a snapshot so the next round may publish.</summary>
    public void NotifyUiIdle() => Interlocked.Exchange(ref _uiBusy, 0);

    private void Poll(PollItem item)
    {
        switch (item)
        {
            case PollItem.FastRegisters:
                foreach (byte idx in PollSchedule.FastRegisters) PollRegister(idx);
                break;

            case PollItem.SummaryRegisters:
                foreach (byte idx in PollSchedule.SummaryRegisters) PollRegister(idx);
                break;

            case PollItem.AllRegisters:
                if (!IncludeAllRegisters) break;
                foreach (byte idx in PollSchedule.SweepRegisters) PollRegister(idx);
                break;

            case PollItem.CellVoltages:
            {
                var frame = _link.Transact(new[] { HvProtocol.CmdCellVoltages },
                                           HvProtocol.CellFrameLength, HvProtocol.CmdCellVoltages);
                if (frame is not null && FrameParser.TryParseCellVoltages(frame, _voltBuf, out _))
                    _state.SetVoltages(_voltBuf);
                break;
            }

            case PollItem.CellTemps:
            {
                var frame = _link.Transact(new[] { HvProtocol.CmdCellTemps },
                                           HvProtocol.CellFrameLength, HvProtocol.CmdCellTemps);
                if (frame is not null && FrameParser.TryParseCellTemps(frame, _tempBuf, out _))
                    _state.SetTemps(_tempBuf);
                break;
            }

            case PollItem.Balance:
            {
                var frame = _link.Transact(new[] { HvProtocol.CmdBalance },
                                           HvProtocol.BalanceFrameLength, HvProtocol.CmdBalance);
                if (frame is not null && FrameParser.TryParseBalance(frame, _balBuf, out _))
                    _state.SetBalance(_balBuf);
                break;
            }
        }
    }

    private void PollRegister(byte idx)
    {
        ushort? v = _link.ReadRegister(idx);
        if (v.HasValue) _state.SetRegister(idx, v.Value);
    }

    public void Dispose() => Stop();
}
