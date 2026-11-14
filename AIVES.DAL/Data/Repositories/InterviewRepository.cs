using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class InterviewRepository(ApplicationDbContext context) : IInterviewRepository
{
    public async Task<InterviewContext?> GetContextAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var candidate = await context.ExamCandidates.AsNoTracking()
            .Include(item => item.Exam)
            .Include(item => item.Questions)
            .Include(item => item.Attempt).ThenInclude(attempt => attempt!.Turns).ThenInclude(turn => turn.Recording)
            .AsSplitQuery()
            .FirstOrDefaultAsync(item => item.Id == candidateId, cancellationToken);
        if (candidate is null)
            return null;

        var exam = candidate.Exam;
        // Stored slot for scheduled candidates; the derived layout covers rows saved before slots were stored.
        var start = candidate.SlotStartsAtUtc ?? ExamSchedule.LegacySlotStart(exam.StartsAtUtc, exam.SlotMinutes, candidate.Order);
        return new InterviewContext(
            candidate.Id,
            exam.Title,
            exam.SubjectName,
            candidate.Email,
            start,
            start.AddMinutes(exam.SlotMinutes),
            exam.AnswerTimeLimitSeconds,
            exam.MaxFollowUpsPerQuestion,
            exam.MaxFollowUpQuestions,
            exam.Language.ToLanguage(),
            candidate.Questions.OrderBy(question => question.Order)
                .Select(question => new ExamAssignedQuestion(question.Order, question.QuestionId, question.Content, question.ExpectedAnswer, question.BloomLevelName, question.RubricJson))
                .ToList(),
            candidate.Attempt?.Id,
            candidate.Attempt is null ? null : ExamRepository.ToRecord(candidate.Attempt),
            exam.Id,
            ExamRepository.RecordingOf(exam),
            candidate.Attempt?.RecordingConsentAtUtc,
            exam.SubjectId,
            candidate.StatusOverride,
            exam.Strategy,
            candidate.IsAdminTest,
            candidate.AdminTestOwnerId);
    }

    /// <summary>Candidates this many places before or after count as sitting close by.</summary>
    private const int NeighbourWindow = 3;

    public async Task<AdaptivePool?> GetAdaptivePoolAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var candidate = await context.ExamCandidates.AsNoTracking()
            .Where(item => item.Id == candidateId)
            .Select(item => new { item.ExamId, item.Order })
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null)
            return null;
        var poolIds = context.ExamQuestionPools.Where(item => item.ExamId == candidate.ExamId).Select(item => item.QuestionId);
        var questions = await ExamRepository.LoadPoolAsync(context, context.Questions.Where(question => question.IsActive && poolIds.Contains(question.Id)), cancellationToken);
        if (questions.Count == 0)
            return null;

        var assigned = await context.ExamCandidateQuestions.AsNoTracking()
            .Where(item => item.Candidate.ExamId == candidate.ExamId && !item.Candidate.IsAdminTest
                && item.ExamCandidateId != candidateId && item.QuestionId != null)
            .Select(item => new { QuestionId = item.QuestionId!.Value, item.Candidate.Order })
            .ToListAsync(cancellationToken);
        var neighbours = assigned.Where(item => Math.Abs(item.Order - candidate.Order) <= NeighbourWindow).Select(item => item.QuestionId).ToHashSet();
        var usage = assigned.GroupBy(item => item.QuestionId).ToDictionary(group => group.Key, group => group.Count());
        var bloomOrders = await context.BloomLevels.AsNoTracking()
            .ToDictionaryAsync(level => level.Name, level => level.Order, StringComparer.OrdinalIgnoreCase, cancellationToken);
        return new AdaptivePool(questions, bloomOrders, neighbours, usage);
    }

    public async Task<bool> ReplaceQuestionAsync(int candidateId, int order, ExamPoolQuestion question, CancellationToken cancellationToken = default)
    {
        var row = await context.ExamCandidateQuestions
            .Include(item => item.Grade)
            .FirstOrDefaultAsync(item => item.ExamCandidateId == candidateId && item.Order == order, cancellationToken);
        if (row is null || row.Grade is not null)
            return false;
        // Never change a question that has already been put to the candidate.
        var asked = await context.ExamTurns.AnyAsync(turn => turn.Attempt.ExamCandidateId == candidateId && turn.MainIndex == order, cancellationToken);
        if (asked)
            return false;
        row.QuestionId = question.Id;
        row.Content = question.Content;
        row.ExpectedAnswer = question.ExpectedAnswer;
        row.BloomLevelName = question.BloomLevelName;
        row.RubricJson = question.RubricJson;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> StartAsync(int candidateId, DateTime startedAtUtc, NewInterviewTurn firstTurn, bool recordingConsent = false,
        CancellationToken cancellationToken = default)
    {
        if (await context.ExamAttempts.AnyAsync(attempt => attempt.ExamCandidateId == candidateId, cancellationToken))
            return false;
        var attempt = new ExamAttempt
        {
            ExamCandidateId = candidateId,
            StartedAtUtc = startedAtUtc,
            Status = InterviewStatus.InProgress,
            RecordingConsentAtUtc = recordingConsent ? startedAtUtc : null
        };
        attempt.Turns.Add(ToEntity(firstTurn, order: 1));
        context.ExamAttempts.Add(attempt);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost the race against another request: the unique index on ExamCandidateId kept one attempt.
            context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> SaveAnswerAsync(int turnId, string transcript, AnswerInputMode? inputMode, DateTime answeredAtUtc, bool timedOut,
        CancellationToken cancellationToken = default, AnswerMetrics? metrics = null)
    {
        var turn = await context.ExamTurns.FirstOrDefaultAsync(item => item.Id == turnId, cancellationToken);
        if (turn is null || turn.AnsweredAtUtc is not null)
            return false;
        turn.Answer = transcript;
        turn.RawAnswer = metrics?.RawTranscript;
        turn.ResponseDelayMs = metrics?.ResponseDelayMs;
        turn.SpeakingMs = metrics?.SpeakingMs;
        turn.InputMode = inputMode;
        turn.AnsweredAtUtc = answeredAtUtc;
        turn.TimedOut = timedOut;
        try
        {
            // AnsweredAtUtc is the concurrency token: the UPDATE only matches while it is still NULL.
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> AdvanceAsync(int turnId, FollowUpReason decision, NewInterviewTurn? nextTurn, DateTime nowUtc, CancellationToken cancellationToken = default, int? decisionLatencyMs = null)
    {
        var turn = await context.ExamTurns.Include(item => item.Attempt).ThenInclude(attempt => attempt.Turns)
            .FirstOrDefaultAsync(item => item.Id == turnId, cancellationToken)
            ?? throw new KeyNotFoundException($"Turn {turnId} was not found.");
        turn.Decision = decision;
        turn.DecisionLatencyMs ??= decisionLatencyMs;
        var attempt = turn.Attempt;
        // Whoever advances first wins; a second advance for the same turn only records its decision.
        var alreadyAdvanced = attempt.Status != InterviewStatus.InProgress || attempt.Turns.Any(item => item.Order > turn.Order);
        if (!alreadyAdvanced && nextTurn is null)
        {
            attempt.Status = InterviewStatus.Completed;
            attempt.CompletedAtUtc = nowUtc;
        }
        else if (!alreadyAdvanced)
        {
            attempt.Turns.Add(ToEntity(nextTurn!, attempt.Turns.Max(item => item.Order) + 1));
        }
        await context.SaveChangesAsync(cancellationToken);
        return !alreadyAdvanced && nextTurn is null;
    }

    public async Task<IReadOnlyList<(int CandidateId, string Email)>> ListOverdueAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default)
    {
        var rows = await context.ExamAttempts.AsNoTracking()
            .Where(attempt => attempt.Status == InterviewStatus.InProgress
                && !attempt.Candidate.IsAdminTest
                && (attempt.Candidate.SlotStartsAtUtc ?? attempt.Candidate.Exam.StartsAtUtc.AddMinutes(attempt.Candidate.Exam.SlotMinutes * (attempt.Candidate.Order - 1)))
                    .AddMinutes(attempt.Candidate.Exam.SlotMinutes) < cutoffUtc)
            .OrderBy(attempt => attempt.Id)
            .Select(attempt => new { attempt.ExamCandidateId, attempt.Candidate.Email })
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.ExamCandidateId, row.Email)).ToList();
    }

    public async Task<IReadOnlyList<(string Text, string Language)>> ListUpcomingQuestionsAsync(DateTime nowUtc, DateTime beforeUtc, int take,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.ExamCandidateQuestions.AsNoTracking()
            .Select(question => new
            {
                question.Content,
                question.Order,
                question.Candidate.IsAdminTest,
                question.Candidate.Exam.Language,
                question.Candidate.StatusOverride,
                Finished = question.Candidate.Attempt != null && question.Candidate.Attempt.Status == InterviewStatus.Completed,
                StartsAt = question.Candidate.SlotStartsAtUtc
                    ?? question.Candidate.Exam.StartsAtUtc.AddMinutes(question.Candidate.Exam.SlotMinutes * (question.Candidate.Order - 1)),
                question.Candidate.Exam.SlotMinutes
            })
            .Where(row => !row.IsAdminTest && !row.Finished
                && row.StatusOverride != CandidateStatus.Absent && row.StatusOverride != CandidateStatus.Cancelled
                && row.StartsAt < beforeUtc && row.StartsAt.AddMinutes(row.SlotMinutes) > nowUtc)
            .OrderBy(row => row.StartsAt).ThenBy(row => row.Order)
            .Take(take)
            .Select(row => new { row.Content, row.Language })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.Content, row.Language)).ToList();
    }

    private static ExamTurn ToEntity(NewInterviewTurn turn, int order) => new()
    {
        Order = order,
        Kind = turn.Kind,
        MainIndex = turn.MainIndex,
        FollowUpIndex = turn.FollowUpIndex,
        QuestionText = turn.QuestionText,
        AskedAtUtc = turn.AskedAtUtc
    };
}
