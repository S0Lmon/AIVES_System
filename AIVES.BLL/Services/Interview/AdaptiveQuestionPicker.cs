using AIVES.DTO;

namespace AIVES.BLL.Services.Interview;

/// <summary>Which way the next main question should move on the Bloom scale.</summary>
public enum AdaptiveStep
{
    Easier = -1,
    Same = 0,
    Harder = 1
}

/// <summary>
/// Adaptive selection for <see cref="QuestionSelectionStrategy.Adaptive"/> exams. After each main
/// question the examiner's decisions say how it went, and the next main question is swapped for one
/// a Bloom level higher or lower. Among the questions at the target level it prefers, in order:
/// one the candidates sitting close by did not get, the one used least in the exam, then random.
/// </summary>
public static class AdaptiveQuestionPicker
{
    /// <summary>
    /// How the candidate did on one main question.
    /// <list type="bullet">
    /// <item>Harder: the first answer was already sufficient.</item>
    /// <item>Easier: nothing useful was said (no answer, off topic, timed out), or two or more
    /// follow-ups still did not get a sufficient answer.</item>
    /// <item>Same: everything else, including when the AI could not decide.</item>
    /// </list>
    /// </summary>
    /// <param name="firstDecision">The examiner's decision on the answer to the main question itself.</param>
    /// <param name="mainTimedOut">Whether the answer to the main question ran out of time.</param>
    /// <param name="followUpsAsked">Follow-ups asked about this main question.</param>
    /// <param name="lastDecision">The decision on the last answer about this main question.</param>
    public static AdaptiveStep Assess(FollowUpReason? firstDecision, bool mainTimedOut, int followUpsAsked, FollowUpReason? lastDecision)
    {
        if (firstDecision == FollowUpReason.Sufficient && !mainTimedOut)
            return AdaptiveStep.Harder;
        if (mainTimedOut || firstDecision is FollowUpReason.NoAnswer or FollowUpReason.OffTopic)
            return AdaptiveStep.Easier;
        if (followUpsAsked > 0 && lastDecision == FollowUpReason.Sufficient)
            return AdaptiveStep.Same;
        if (followUpsAsked >= 2 || lastDecision is FollowUpReason.NoAnswer or FollowUpReason.OffTopic)
            return AdaptiveStep.Easier;
        return AdaptiveStep.Same;
    }

    /// <summary>
    /// The question to ask next instead of <paramref name="planned"/>, or null to keep the planned one.
    /// </summary>
    /// <param name="currentOrder">Bloom order of the main question just finished.</param>
    /// <param name="plannedOrder">Bloom order of the question planned next; 0 when unknown.</param>
    /// <param name="assignedIds">Every bank question already in this candidate's set, asked or still to come.</param>
    public static ExamPoolQuestion? Pick(AdaptivePool pool, AdaptiveStep step, int currentOrder, int plannedOrder,
        IReadOnlyCollection<int> assignedIds, Random random)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(random);
        if (step == AdaptiveStep.Same || currentOrder <= 0)
            return null;
        // The planned question already moves the right way: keep it, and the balanced draw with it.
        if (plannedOrder > 0 && (step == AdaptiveStep.Harder ? plannedOrder > currentOrder : plannedOrder < currentOrder))
            return null;

        var available = pool.Questions
            .Where(question => question.BloomOrder > 0 && !assignedIds.Contains(question.Id))
            .Where(question => step == AdaptiveStep.Harder ? question.BloomOrder > currentOrder : question.BloomOrder < currentOrder)
            .ToList();
        if (available.Count == 0)
            return null;

        // The nearest level in the right direction: one step at a time, not straight to the extremes.
        var target = step == AdaptiveStep.Harder ? available.Min(question => question.BloomOrder) : available.Max(question => question.BloomOrder);
        return available
            .Where(question => question.BloomOrder == target)
            .Select(question => (Question: question, TieBreak: random.Next()))
            .OrderBy(item => pool.NeighbourQuestionIds.Contains(item.Question.Id) ? 1 : 0)
            .ThenBy(item => pool.Usage.GetValueOrDefault(item.Question.Id))
            .ThenBy(item => item.TieBreak)
            .First().Question;
    }
}
