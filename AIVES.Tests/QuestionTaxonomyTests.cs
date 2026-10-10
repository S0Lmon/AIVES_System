using AIVES.DTO;

namespace AIVES.Tests;

/// <summary>
/// The parser is the gate that keeps invented levels and difficulties out of the bank, and it is
/// what makes "leave it blank and the AI decides" safe.
/// </summary>
public sealed class QuestionTaxonomyTests
{
    [Theory]
    [InlineData("Remember", true)]
    [InlineData("understand", true)]
    [InlineData("EVALUATE", true)]
    [InlineData("Create", true)]
    [InlineData("Invented", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BloomLevelsAreMatchedCaseInsensitivelyAndCanonicalised(string? value, bool allowed)
    {
        Assert.Equal(allowed, BloomLevels.IsAllowed(value));
        if (!allowed)
            return;

        // Canonical form is what the bank stores by name.
        Assert.Contains(BloomLevels.Normalize(value)!, BloomLevels.All);
    }

    [Fact]
    public void AllSixBloomLevelsMatchTheBank()
    {
        Assert.Equal(6, BloomLevels.All.Count);
    }

    [Theory]
    [InlineData("Basic", 0)]
    [InlineData("intermediate", 1)]
    [InlineData("Advanced", 2)]
    [InlineData("Balanced", -1)]
    [InlineData(null, -1)]
    public void DifficultyOrderingLetsARangeBeCompared(string? value, int expectedIndex)
    {
        Assert.Equal(expectedIndex, QuestionDifficulties.IndexOf(value));
    }

    [Fact]
    public void DifficultyRangesCanBeOrderedNumerically()
    {
        // The validation only compares indexes, so an inverted range is detectable.
        Assert.True(QuestionDifficulties.IndexOf("Basic") < QuestionDifficulties.IndexOf("Advanced"));
        Assert.True(QuestionDifficulties.IndexOf("Advanced") > QuestionDifficulties.IndexOf("Intermediate"));
    }
}