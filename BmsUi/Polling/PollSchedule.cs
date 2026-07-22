using BmsUi.Protocol;

namespace BmsUi.Polling;

public enum PollItem { FastRegisters, SummaryRegisters, CellVoltages, CellTemps, Balance, AllRegisters }

/// <summary>
/// Tiered schedule on a 10 Hz base tick (pure function — testable): fast registers every
/// tick, cell arrays + summary every 2nd tick, balance every 5th tick.
/// </summary>
public static class PollSchedule
{
    public static readonly byte[] FastRegisters =
        { Reg.Faults, Reg.Outputs, Reg.PackVoltage, Reg.PackCurrent };

    public static readonly byte[] SummaryRegisters =
    {
        Reg.MaxCellVoltage, Reg.MinCellVoltage, Reg.TotalCellVoltage,
        Reg.MaxCellTemp, Reg.MinCellTemp, Reg.AvgCellVoltage,
        Reg.AvgCellTemp, Reg.MaxSlaveTemp, Reg.EstimatedSoc,
    };

    /// <summary>
    /// Rarely-changing settings, read once when the link comes up. ALLOWED_DISBALANCE is
    /// read because the balance summary shows it; the UI never WRITES anything.
    /// </summary>
    public static readonly byte[] ConfigRegisters = { Reg.AllowedDisbalance };

    /// <summary>
    /// Everything the regular schedule does not already cover, for the Registers tab.
    /// Only swept while that tab is visible; the values left out are refreshed at 5-10 Hz
    /// anyway and are already in the snapshot the table reads from.
    /// </summary>
    public static readonly byte[] SweepRegisters = BuildSweep();

    private static byte[] BuildSweep()
    {
        var regular = FastRegisters.Concat(SummaryRegisters).Concat(ConfigRegisters).ToHashSet();
        return RegisterCatalog.All
            .Select(d => d.Index)
            .Where(idx => !regular.Contains(idx))
            .ToArray();
    }

    public const int TickIntervalMs = 100;   // 10 Hz

    public static IReadOnlyList<PollItem> ItemsForTick(long tick)
    {
        var items = new List<PollItem>(5) { PollItem.FastRegisters };
        if (tick % 2 == 0)
        {
            items.Add(PollItem.SummaryRegisters);
            items.Add(PollItem.CellVoltages);
            items.Add(PollItem.CellTemps);
        }
        if (tick % 5 == 0) items.Add(PollItem.Balance);
        if (tick % 10 == 0) items.Add(PollItem.AllRegisters);
        return items;
    }
}
