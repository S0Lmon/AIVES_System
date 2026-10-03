using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Interview;

public sealed class InterviewService(
    IInterviewRepository interviews,
    IFollowUpGenerator followUps,
    IOptions<InterviewOptions> options,
    TimeProvider clock,
    ILogger<InterviewService> logger) : IInterviewService
{
    public const int MaxTranscriptLength = 5000;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private TimeSpan Grace => TimeSpan.FromSeconds(Math.Max(0, options.Value.AnswerGraceSeconds));
    private TimeSpan RecoveryDelay => TimeSpan.FromSeconds(Math.Max(1, options.Value.AiTimeoutSeconds) + 10);

    public async Task<InterviewStateDto> GetStateAsync(int candidateId, string email, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(candidateId, email, cancellationToken);
        if (context.Record is { Status: InterviewStatus.InProgress } record)
        {
            var last = record.Turns[^1];
            if (last.AnsweredAtUtc is { } answeredAt && Now - answeredAt > RecoveryDelay)
            {
                // Answered but never advanced (the request died mid-way): move on without a follow-up.
                // Waiting past the AI timeout first keeps this from racing an answer still being processed.
                await interviews.AdvanceAsync(last.TurnId, last.Decision ?? FollowUpReason.AiUnavailable, NextMainTurn(context, last.MainIndex), Now, cancellationToken);
                context = await LoadAsync(candidateId, email, cancellationToken);
            }
            else if (Now >= context.SlotEndsAtUtc + Grace)
            {
                // The slot is over: close the open question unanswered and finish.
                await interviews.SaveAnswerAsync(last.TurnId, string.Empty, null, Now, timedOut: true, cancellationToken);
                await interviews.AdvanceAsync(last.TurnId, FollowUpReason.LimitReached, null, Now, cancellationToken);
                context = await LoadAsync(candidateId, email, cancellationToken);
            }
        }
        return ToState(context, Now);
    }

    public async Task<InterviewStateDto> StartAsync(int candidateId, string email, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(candidateId, email, cancellationToken);
        if (context.Record is not null)
            return await GetStateAsync(candidateId, email, cancellationToken);
        if (Now < context.SlotStartsAtUtc)
            throw new InvalidOperationException(L10n.T("Your exam slot has not started yet."));
        if (Now >= context.SlotEndsAtUtc)
            throw new InvalidOperationException(L10n.T("Your exam slot has ended."));
        if (context.Questions.Count == 0)
            throw new InvalidOperationException(L10n.T("No questions were assigned to you. Please contact your lecturer."));

        var first = new NewInterviewTurn(TurnKind.Main, 1, 0, context.Questions[0].Content, Now);
        if (await interviews.StartAsync(candidateId, Now, first, cancellationToken))
            logger.LogInformation("Candidate {CandidateId} started the interview", candidateId);
        return await GetStateAsync(candidateId, email, cancellationToken);
    }

    public async Task<InterviewStateDto> AnswerAsync(int candidateId, string email, InterviewAnswerInput input, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(candidateId, email, cancellationToken);
        if (context.Record is not { Status: InterviewStatus.InProgress } record)
            return ToState(context, Now);
        var current = record.Turns[^1];
        if (current.TurnId != input.TurnId || current.AnsweredAtUtc is not null)
            return await GetStateAsync(candidateId, email, cancellationToken);

        var transcript = (input.Transcript ?? string.Empty).Trim();
        if (transcript.Length > MaxTranscriptLength)
            transcript = transcript[..MaxTranscriptLength];
        var now = Now;
        var timedOut = now > current.AskedAtUtc.AddSeconds(context.AnswerTimeLimitSeconds) + Grace;
        if (!await interviews.SaveAnswerAsync(current.TurnId, transcript, input.InputMode, now, timedOut, cancellationToken))
            return await GetStateAsync(candidateId, email, cancellationToken);

        var (decision, next) = await DecideAsync(context, record, current, transcript, now, cancellationToken);
        await interviews.AdvanceAsync(current.TurnId, decision, next, Now, cancellationToken);
        logger.LogInformation("Candidate {CandidateId} answered turn {TurnId}: {Decision}, next {Next}", candidateId, current.TurnId, decision,
            next is null ? "end" : next.Kind.ToString());
        return ToState(await LoadAsync(candidateId, email, cancellationToken), Now);
    }

    private async Task<(FollowUpReason Decision, NewInterviewTurn? Next)> DecideAsync(
        InterviewContext context, InterviewRecordDto record, InterviewTurnRecordDto current, string transcript, DateTime now, CancellationToken cancellationToken)
    {
        var moveOn = NextMainTurn(context, current.MainIndex);
        if (now >= context.SlotEndsAtUtc)
            return (FollowUpReason.LimitReached, null);
        if (transcript.Length == 0)
            return (FollowUpReason.NoAnswer, moveOn);

        var followUpsHere = record.Turns.Count(turn => turn.Kind == TurnKind.FollowUp && turn.MainIndex == current.MainIndex);
        var followUpsTotal = record.Turns.Count(turn => turn.Kind == TurnKind.FollowUp);
        var leftHere = context.MaxFollowUpsPerQuestion - followUpsHere;
        var leftTotal = context.MaxFollowUpQuestions - followUpsTotal;
        if (leftHere <= 0 || leftTotal <= 0)
            return (FollowUpReason.LimitReached, moveOn);
        if (!followUps.IsConfigured)
            return (FollowUpReason.AiUnavailable, moveOn);

        var main = context.Questions[current.MainIndex - 1];
        var exchanges = record.Turns.Where(turn => turn.MainIndex == current.MainIndex)
            .Select(turn => new InterviewExchange(turn.QuestionText, turn.TurnId == current.TurnId ? transcript : turn.Answer ?? string.Empty))
            .ToList();
        var request = new FollowUpRequest(context.Language, context.SubjectName, main.Content, main.ExpectedAnswer, exchanges, Math.Min(leftHere, leftTotal));

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.AiTimeoutSeconds)));
            var decision = await followUps.DecideAsync(request, timeout.Token);
            if (!decision.NeedsFollowUp || string.IsNullOrWhiteSpace(decision.FollowUpQuestion))
                return (decision.Reason, moveOn);
            return (decision.Reason, new NewInterviewTurn(TurnKind.FollowUp, current.MainIndex, followUpsHere + 1, decision.FollowUpQuestion, Now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Never stall a live exam on the AI: record it and carry on with the next main question.
            logger.LogWarning(ex, "Follow-up decision failed for candidate {CandidateId}; continuing without a follow-up", context.CandidateId);
            return (FollowUpReason.AiUnavailable, moveOn);
        }
    }

    private NewInterviewTurn? NextMainTurn(InterviewContext context, int mainIndex) =>
        mainIndex < context.Questions.Count
            ? new NewInterviewTurn(TurnKind.Main, mainIndex + 1, 0, context.Questions[mainIndex].Content, Now)
            : null;

    private async Task<InterviewContext> LoadAsync(int candidateId, string email, CancellationToken cancellationToken)
    {
        var context = await interviews.GetContextAsync(candidateId, cancellationToken);
        if (context is null || string.IsNullOrWhiteSpace(email) || !string.Equals(context.CandidateEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException(L10n.T("This exam slot was not found."));
        return context;
    }

    private static InterviewStateDto ToState(InterviewContext context, DateTime now)
    {
        var record = context.Record;
        var status = record?.Status ?? InterviewStatus.NotStarted;
        InterviewTurnDto? current = null;
        if (record is { Status: InterviewStatus.InProgress } && record.Turns[^1] is { AnsweredAtUtc: null } open)
            current = new InterviewTurnDto(open.TurnId, open.Kind, open.QuestionText, open.MainIndex, context.Questions.Count,
                open.FollowUpIndex, open.AskedAtUtc, context.AnswerTimeLimitSeconds);
        return new InterviewStateDto(context.CandidateId, context.ExamTitle, context.Language, status, context.SlotStartsAtUtc, context.SlotEndsAtUtc, current, now);
    }
}
