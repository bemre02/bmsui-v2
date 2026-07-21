using BmsUi.Polling;
using BmsUi.Protocol;
using Xunit;

public class PollScheduleTests
{
    [Fact]
    public void Tick0_IncludesEverything()
    {
        var items = PollSchedule.ItemsForTick(0);
        Assert.Contains(PollItem.FastRegisters, items);
        Assert.Contains(PollItem.SummaryRegisters, items);
        Assert.Contains(PollItem.CellVoltages, items);
        Assert.Contains(PollItem.CellTemps, items);
        Assert.Contains(PollItem.Balance, items);
    }

    [Fact]
    public void OddTick_OnlyFastRegisters()
    {
        var items = PollSchedule.ItemsForTick(1);
        Assert.Equal(new[] { PollItem.FastRegisters }, items);
    }

    [Fact]
    public void EvenTick_AddsCellsAndSummary_ButNotBalance()
    {
        var items = PollSchedule.ItemsForTick(2);   // 5 Hz
        Assert.Contains(PollItem.CellVoltages, items);
        Assert.Contains(PollItem.CellTemps, items);
        Assert.Contains(PollItem.SummaryRegisters, items);
        Assert.DoesNotContain(PollItem.Balance, items);
    }

    [Fact]
    public void BalanceEveryFifthTick_Gives2Hz()
    {
        Assert.Contains(PollItem.Balance, PollSchedule.ItemsForTick(5));
        Assert.Contains(PollItem.Balance, PollSchedule.ItemsForTick(10));
        Assert.DoesNotContain(PollItem.Balance, PollSchedule.ItemsForTick(6));
    }

    [Fact]
    public void RegisterLists_ContainNoShadowedIndices()
    {
        foreach (byte idx in PollSchedule.FastRegisters.Concat(PollSchedule.SummaryRegisters))
            Assert.True(HvProtocol.IsValidRegister(idx), $"idx {idx} gecersiz");
    }

    [Fact]
    public void TransactionsPerSecond_MatchesDesignBudget()
    {
        // 1 saniye = 10 tick; tasarim butcesi ~97 transaction/s
        int total = 0;
        for (long t = 0; t < 10; t++)
            foreach (var item in PollSchedule.ItemsForTick(t))
                total += item switch
                {
                    PollItem.FastRegisters => PollSchedule.FastRegisters.Length,
                    PollItem.SummaryRegisters => PollSchedule.SummaryRegisters.Length,
                    _ => 1,
                };
        Assert.InRange(total, 90, 105);
    }
}
