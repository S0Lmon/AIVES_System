using AIVES.DTO;

namespace AIVES.BLL.Services.Exams;

/// <summary>
/// Picks the main questions for each candidate, in sitting order. Candidates who sit one after the
/// other are the ones most likely to pass questions on, so for each pick the allocator prefers, in order:
/// <list type="number">
/// <item>a question none of the recent candidates had (the window grows with the pool size, up to
/// <see cref="MaxAvoidWindow"/> candidates back);</item>
/// <item>the question used least so far, which spreads use evenly across the bank;</item>
/// <item>a Bloom level this candidate does not have yet, so each set covers several levels.
/// This only chooses among equally used questions: ranking it above usage would hand the few
/// questions of a rare level to nearly every candidate;</item>
/// <item>random order among the rest.</item>
/// </list>
/// The first rule is a preference, not a guarantee: with too small a pool some overlap is unavoidable,
/// and <see cref="ExamAllocation.ConsecutiveOverlaps"/> reports it. With
/// <see cref="QuestionSelectionStrategy.Random"/> the first three rules are skipped: every candidate's
/// set is a uniform random draw (a candidate still never gets the same question twice).
/// </summary>
public static class ExamQuestionAllocator
{
    public const int MaxAvoidWindow = 3;

    public static ExamAllocation Allocate(IReadOnlyList<ExamPoolQuestion> pool, int candidateCount, int questionsPerCandidate, Random random,
        QuestionSelectionStrategy strategy = QuestionSelectionStrategy.Balanced)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(random);
        if (candidateCount < 0)
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        if (questionsPerCandidate < 1 || questionsPerCandidate > pool.Count)
            throw new ArgumentOutOfRangeException(nameof(questionsPerCandidate), "Each candidate needs between 1 and pool size questions.");

        // A pool of k * n questions lets k - 1 previous candidates be avoided completely.
        var window = Math.Clamp(pool.Count / questionsPerCandidate - 1, 1, MaxAvoidWindow);
        var usage = pool.ToDictionary(question => question.Id, _ => 0);
        var sets = new List<IReadOnlyList<ExamPoolQuestion>>(candidateCount);
        var overlaps = 0;

        for (var candidate = 0; candidate < candidateCount; candidate++)
        {
            var recent = sets.Skip(Math.Max(0, sets.Count - window)).SelectMany(set => set).Select(question => question.Id).ToHashSet();
            // One random rank per question for this candidate, so equal candidates are drawn in random order.
            var tieBreak = pool.ToDictionary(question => question.Id, _ => random.Next());
            var chosen = new List<ExamPoolQuestion>(questionsPerCandidate);
            var levels = new HashSet<int>();

            while (chosen.Count < questionsPerCandidate)
            {
                var next = pool
                    .Where(question => !chosen.Contains(question))
                    .OrderBy(question => strategy == QuestionSelectionStrategy.Random ? 0 : recent.Contains(question.Id) ? 1 : 0)
                    .ThenBy(question => strategy == QuestionSelectionStrategy.Random ? 0 : usage[question.Id])
                    .ThenBy(question => strategy == QuestionSelectionStrategy.Random ? 0 : levels.Contains(question.BloomLevelId) ? 1 : 0)
                    .ThenBy(question => tieBreak[question.Id])
                    .First();
                chosen.Add(next);
                levels.Add(next.BloomLevelId);
                usage[next.Id]++;
            }

            if (sets.Count > 0)
            {
                var previous = sets[^1].Select(question => question.Id).ToHashSet();
                overlaps += chosen.Count(question => previous.Contains(question.Id));
            }
            sets.Add(chosen);
        }

        return new ExamAllocation(sets, overlaps);
    }
}

/// <summary>
/// Question sets in candidate order. <see cref="ConsecutiveOverlaps"/> counts questions a candidate
/// shares with the one directly before them; zero means no two neighbours got the same question.
/// </summary>
public sealed record ExamAllocation(IReadOnlyList<IReadOnlyList<ExamPoolQuestion>> Sets, int ConsecutiveOverlaps);
