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
        var start = exam.StartsAtUtc.AddMinutes(exam.SlotMinutes * (candidate.Order - 1));
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
            exam.SubjectId);
    }

    public async Task<bool> StartAsync(int candidateId, DateTime startedAtUtc, NewInterviewTurn firstTurn, bool recordingConsent = false, CancellationToken cancellationToken = default)
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
                && attempt.Candidate.Exam.StartsAtUtc.AddMinutes(attempt.Candidate.Exam.SlotMinutes * attempt.Candidate.Order) < cutoffUtc)
            .OrderBy(attempt => attempt.Id)
            .Select(attempt => new { attempt.ExamCandidateId, attempt.Candidate.Email })
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.ExamCandidateId, row.Email)).ToList();
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
