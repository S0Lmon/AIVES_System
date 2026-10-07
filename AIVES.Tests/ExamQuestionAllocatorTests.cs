using AIVES.BLL.Services.Exams;
using AIVES.DTO;

namespace AIVES.Tests;

public sealed class ExamQuestionAllocatorTests
{
    /// <summary>A pool spread evenly over the Bloom levels 1..levels.</summary>
    private static List<ExamPoolQuestion> Pool(int size, int levels = 4) =>
        Enumerable.Range(1, size).Select(id => new ExamPoolQuestion(id, (id - 1) % levels + 1, $"L{(id - 1) % levels + 1}", $"Question {id}", "Answer")).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(2026)]
    public void EveryCandidateGetsDistinctQuestionsAndNeighboursShareNoneWhenThePoolAllows(int seed)
    {
        var allocation = ExamQuestionAllocator.Allocate(Pool(12), 25, 4, new Random(seed));

        Assert.Equal(25, allocation.Sets.Count);
        Assert.All(allocation.Sets, set =>
        {
            Assert.Equal(4, set.Count);
            Assert.Equal(4, set.Select(question => question.Id).Distinct().Count());
        });
        Assert.Equal(0, allocation.ConsecutiveOverlaps);
        for (var i = 1; i < allocation.Sets.Count; i++)
            Assert.Empty(allocation.Sets[i].Select(q => q.Id).Intersect(allocation.Sets[i - 1].Select(q => q.Id)));
    }

    [Fact]
    public void LargerPoolsAlsoKeepSeveralRecentCandidatesApart()
    {
        // 16 questions, 4 each: the window is 3, so any 4 candidates in a row use 16 different questions.
        var sets = ExamQuestionAllocator.Allocate(Pool(16), 20, 4, new Random(7)).Sets;

        for (var i = 3; i < sets.Count; i++)
        {
            var run = sets.Skip(i - 3).Take(4).SelectMany(set => set).Select(question => question.Id).ToList();
            Assert.Equal(16, run.Distinct().Count());
        }
    }

    [Fact]
    public void UseIsSpreadEvenlyAcrossTheBank()
    {
        var sets = ExamQuestionAllocator.Allocate(Pool(10), 30, 3, new Random(3)).Sets;

        var usage = sets.SelectMany(set => set).GroupBy(question => question.Id).Select(group => group.Count()).ToList();
        Assert.Equal(10, usage.Count);              // every question is used
        Assert.True(usage.Max() - usage.Min() <= 1); // 90 draws over 10 questions: 9 each, give or take one
    }

    [Fact]
    public void AQuestionAloneInItsBloomLevelIsNotHandedToEveryone()
    {
        // 11 questions at level 1 and a single one at level 4: Bloom variety must not override even use.
        var pool = Enumerable.Range(1, 11).Select(id => new ExamPoolQuestion(id, 1, "Remember", $"Q{id}", "A"))
            .Append(new ExamPoolQuestion(12, 4, "Analyze", "Rare", "A")).ToList();

        var sets = ExamQuestionAllocator.Allocate(pool, 24, 3, new Random(4)).Sets;

        var rareUses = sets.Count(set => set.Any(question => question.Id == 12));
        Assert.True(rareUses <= 24 * 3 / 12 + 1, $"The only level-4 question went to {rareUses} of 24 candidates.");
    }

    [Fact]
    public void EachSetCoversDifferentBloomLevelsWhenAvailable()
    {
        var sets = ExamQuestionAllocator.Allocate(Pool(20, levels: 4), 15, 4, new Random(11)).Sets;

        Assert.All(sets, set => Assert.Equal(4, set.Select(question => question.BloomLevelId).Distinct().Count()));
    }

    [Fact]
    public void ASmallPoolStillWorksAndReportsTheUnavoidableOverlap()
    {
        // 5 questions, 4 each: neighbours must share at least 3.
        var allocation = ExamQuestionAllocator.Allocate(Pool(5), 4, 4, new Random(5));

        Assert.All(allocation.Sets, set => Assert.Equal(4, set.Select(question => question.Id).Distinct().Count()));
        Assert.Equal(3 * 3, allocation.ConsecutiveOverlaps);
    }

    [Fact]
    public void SameSeedGivesTheSameAllocationAndDifferentSeedsDiffer()
    {
        static string Signature(ExamAllocation allocation) =>
            string.Join("|", allocation.Sets.Select(set => string.Join(",", set.Select(question => question.Id))));

        var pool = Pool(30);
        Assert.Equal(Signature(ExamQuestionAllocator.Allocate(pool, 10, 3, new Random(9))), Signature(ExamQuestionAllocator.Allocate(pool, 10, 3, new Random(9))));
        Assert.NotEqual(Signature(ExamQuestionAllocator.Allocate(pool, 10, 3, new Random(9))), Signature(ExamQuestionAllocator.Allocate(pool, 10, 3, new Random(10))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void QuestionCountMustFitThePool(int perCandidate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExamQuestionAllocator.Allocate(Pool(5), 3, perCandidate, new Random(1)));
    }

    [Fact]
    public void TheRandomStrategyStillGivesEachCandidateDistinctQuestions()
    {
        var allocation = ExamQuestionAllocator.Allocate(Pool(12), 20, 4, new Random(13), QuestionSelectionStrategy.Random);

        Assert.Equal(20, allocation.Sets.Count);
        Assert.All(allocation.Sets, set =>
        {
            Assert.Equal(4, set.Count);
            Assert.Equal(4, set.Select(question => question.Id).Distinct().Count());
        });
    }

    [Fact]
    public void TheRandomStrategyDrawsADifferentSequenceThanTheBalancedOne()
    {
        var pool = Pool(30);
        static string Signature(ExamAllocation allocation) =>
            string.Join("|", allocation.Sets.Select(set => string.Join(",", set.Select(question => question.Id))));

        var balanced = Signature(ExamQuestionAllocator.Allocate(pool, 10, 3, new Random(9)));
        var random = Signature(ExamQuestionAllocator.Allocate(pool, 10, 3, new Random(9), QuestionSelectionStrategy.Random));

        Assert.NotEqual(balanced, random);
    }
}
