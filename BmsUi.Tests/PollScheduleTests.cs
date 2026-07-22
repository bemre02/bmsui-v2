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
            Assert.True(HvProtocol.IsValidRegister(idx), $"idx {idx} is invalid");
    }

    [Fact]
    public void TransactionsPerSecond_MatchesDesignBudget()
    {
        // 1 second = 10 ticks. Budget is ~97 transactions/s with the Registers tab closed:
        // the sweep is opt-in, so PollItem.AllRegisters costs nothing until it is enabled.
        Assert.InRange(TransactionsPerSecond(sweepEnabled: false), 90, 105);
    }

    [Fact]
    public void RegisterSweep_AddsItsCost_OnlyWhenEnabled()
    {
        int closed = TransactionsPerSecond(sweepEnabled: false);
        int open = TransactionsPerSecond(sweepEnabled: true);

        // The sweep runs at 1 Hz, so it adds exactly one pass over SweepRegisters per second
        Assert.Equal(closed + PollSchedule.SweepRegisters.Length, open);
        Assert.InRange(open, 120, 140);
    }

    private static int TransactionsPerSecond(bool sweepEnabled)
    {
        int total = 0;
        for (long t = 0; t < 10; t++)
            foreach (var item in PollSchedule.ItemsForTick(t))
                total += item switch
                {
                    PollItem.FastRegisters => PollSchedule.FastRegisters.Length,
                    PollItem.SummaryRegisters => PollSchedule.SummaryRegisters.Length,
                    PollItem.AllRegisters => sweepEnabled ? PollSchedule.SweepRegisters.Length : 0,
                    _ => 1,
                };
        return total;
    }

    [Fact]
    public void SweepRegisters_ExcludeTheOnesAlreadyPolled()
    {
        // Asking again for values refreshed at 5-10 Hz would be redundant traffic
        var regular = PollSchedule.FastRegisters
            .Concat(PollSchedule.SummaryRegisters)
            .Concat(PollSchedule.ConfigRegisters)
            .ToHashSet();

        Assert.All(PollSchedule.SweepRegisters, idx => Assert.DoesNotContain(idx, regular));
    }

    [Fact]
    public void SweepRegisters_CoverEveryOtherReadableIndex()
    {
        var covered = PollSchedule.FastRegisters
            .Concat(PollSchedule.SummaryRegisters)
            .Concat(PollSchedule.ConfigRegisters)
            .Concat(PollSchedule.SweepRegisters)
            .ToHashSet();

        Assert.Equal(RegisterCatalog.All.Count, covered.Count);
        Assert.All(RegisterCatalog.All, d => Assert.Contains(d.Index, covered));
    }

    [Fact]
    public void SweepRegisters_AreAllReadable()
        => Assert.All(PollSchedule.SweepRegisters,
                      idx => Assert.True(HvProtocol.IsValidRegister(idx)));

    [Fact]
    public void AllRegisters_IsScheduledEveryTenthTick_GivingOneHz()
    {
        Assert.Contains(PollItem.AllRegisters, PollSchedule.ItemsForTick(0));
        Assert.Contains(PollItem.AllRegisters, PollSchedule.ItemsForTick(10));
        Assert.DoesNotContain(PollItem.AllRegisters, PollSchedule.ItemsForTick(5));
    }
}
