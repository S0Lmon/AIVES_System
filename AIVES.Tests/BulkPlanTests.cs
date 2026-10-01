using AIVES.WebMVC.Models.ViewModels;

namespace AIVES.Tests;

/// <summary>
/// The bulk plan hands the lecturer ranges per Bloom level plus one total. Splitting that total
/// without breaking a range is the part most likely to regress, so it is pinned down here.
/// </summary>
public sealed class BulkPlanTests
{
    private static BulkGenerationViewModel Plan(int total, params (int Id, int Min, int Max)[] rows) => new()
    {
        TotalAmount = total,
        Plan = rows.Select(row => new BulkPlanRow
        {
            BloomLevelId = row.Id,
            BloomLevelName = $"Level {row.Id}",
            Min = row.Min,
            Max = row.Max
        }).ToList()
    };

    [Fact]
    public void TotalIsSplitAcrossTheRanges()
    {
        var plan = Plan(10, (1, 0, 0), (2, 2, 4), (3, 2, 4), (4, 2, 4));

        Assert.True(plan.TryAllocate(out var counts, out var problem));
        Assert.Null(problem);
        Assert.Equal(10, counts.Values.Sum());
        Assert.All(counts.Values, count => Assert.InRange(count, 0, 10));
    }

    [Fact]
    public void EveryRowStaysInsideItsOwnRange()
    {
        var plan = Plan(7, (1, 1, 2), (2, 0, 3), (3, 4, 6));

        Assert.True(plan.TryAllocate(out var counts, out _));

        Assert.InRange(counts[1], 1, 2);
        Assert.InRange(counts[2], 0, 3);
        Assert.InRange(counts[3], 4, 6);
    }

    [Fact]
    public void MinimumsAreHonouredBeforeAnySpareIsHandedOut()
    {
        var plan = Plan(4, (1, 2, 2), (2, 2, 5));

        Assert.True(plan.TryAllocate(out var counts, out _));

        // Row 1 is capped at 2, so both minimums land before the spare goes to row 2.
        Assert.Equal(2, counts[1]);
        Assert.Equal(2, counts[2]);
    }

    [Fact]
    public void ATotalBelowTheSumOfMinimumsIsRejected()
    {
        var plan = Plan(2, (1, 2, 5), (2, 2, 5));

        Assert.False(plan.TryAllocate(out _, out var problem));
        Assert.Contains("between", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATotalAboveTheSumOfMaximumsIsRejected()
    {
        var plan = Plan(12, (1, 0, 2), (2, 0, 3));

        Assert.False(plan.TryAllocate(out _, out var problem));
        Assert.Contains("between", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARangeThatEndsBelowWhereItStartsIsRejected()
    {
        var plan = Plan(5, (1, 4, 2), (2, 0, 5));

        Assert.False(plan.TryAllocate(out _, out var problem));
        // The message names the offending row so the lecturer knows which range to fix.
        Assert.Contains("Level 1", problem);
    }

    [Fact]
    public void APlanWithNoBloomLevelsIsRejected()
    {
        var plan = Plan(5);

        Assert.False(plan.TryAllocate(out _, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void LevelsExcludedWithAZeroRangeAreNeverGivenQuestions()
    {
        var plan = Plan(3, (1, 0, 0), (2, 0, 3), (3, 0, 0));

        Assert.True(plan.TryAllocate(out var counts, out _));

        Assert.Equal(0, counts[1]);
        Assert.Equal(3, counts[2]);
        Assert.Equal(0, counts[3]);
    }

    [Fact]
    public void ReportedBoundsMatchTheSummedRanges()
    {
        var plan = Plan(5, (1, 1, 2), (2, 2, 5));

        Assert.Equal(3, plan.MinTotal);
        Assert.Equal(7, plan.MaxTotalForPlan);
    }
}