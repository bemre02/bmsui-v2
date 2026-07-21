using BmsUi.Protocol;

namespace BmsUi.Polling;

public enum PollItem { FastRegisters, SummaryRegisters, CellVoltages, CellTemps, Balance }

/// <summary>
/// 10 Hz temel tick uzerinde katmanli zamanlama (saf fonksiyon — test edilebilir):
/// her tick hizli register'lar, her 2. tick hucre dizileri + ozet, her 5. tick balans.
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
    /// Baglanti kurulunca bir kez okunan, nadiren degisen ayarlar. Balans ozetinde
    /// gosterildigi icin ALLOWED_DISBALANCE okunur; UI'dan hicbir sey YAZILMAZ.
    /// </summary>
    public static readonly byte[] ConfigRegisters = { Reg.AllowedDisbalance };

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
        return items;
    }
}
