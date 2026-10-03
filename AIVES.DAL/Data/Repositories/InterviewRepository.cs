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
            .Include(item => item.Attempt).ThenInclude(attempt => attempt!.Turns)
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
                .Select(question => new ExamAssignedQuestion(question.Order, question.QuestionId, question.Content, question.ExpectedAnswer, question.BloomLevelName))
                .ToList(),
            candidate.Attempt?.Id,
            candidate.Attempt is null ? null : ExamRepository.ToRecord(candidate.Attempt));
    }

    public async Task<bool> StartAsync(int candidateId, DateTime startedAtUtc, NewInterviewTurn firstTurn, CancellationToken cancellationToken = default)
    {
        if (await context.ExamAttempts.AnyAsync(attempt => attempt.ExamCandidateId == candidateId, cancellationToken))
            return false;
        var attempt = new ExamAttempt { ExamCandidateId = candidateId, StartedAtUtc = startedAtUtc, Status = InterviewStatus.InProgress };
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

    public async Task<bool> SaveAnswerAsync(int turnId, string transcript, AnswerInputMode? inputMode, DateTime answeredAtUtc, bool timedOut, CancellationToken cancellationToken = default)
    {
        var turn = await context.ExamTurns.FirstOrDefaultAsync(item => item.Id == turnId, cancellationToken);
        if (turn is null || turn.AnsweredAtUtc is not null)
            return false;
        turn.Answer = transcript;
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

    public async Task AdvanceAsync(int turnId, FollowUpReason decision, NewInterviewTurn? nextTurn, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var turn = await context.ExamTurns.Include(item => item.Attempt).ThenInclude(attempt => attempt.Turns)
            .FirstOrDefaultAsync(item => item.Id == turnId, cancellationToken)
            ?? throw new KeyNotFoundException($"Turn {turnId} was not found.");
        turn.Decision = decision;
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
