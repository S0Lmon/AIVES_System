using System.Diagnostics;
using AIVES.BLL.Services.Operations;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Interview;

public sealed class InterviewService(
    IInterviewRepository interviews,
    IFollowUpGenerator followUps,
    IGlossaryService glossary,
    ISystemSettingsService settings,
    IAuditService audit,
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
                if (await interviews.AdvanceAsync(last.TurnId, FollowUpReason.LimitReached, null, Now, cancellationToken))
                    await AuditCompletedAsync(context, "slot ended", cancellationToken);
                context = await LoadAsync(candidateId, email, cancellationToken);
            }
        }
        return await ToStateAsync(context, cancellationToken);
    }

    public async Task<InterviewStateDto> StartAsync(int candidateId, string email, bool recordingConsent = false, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(candidateId, email, cancellationToken);
        if (context.Record is not null)
            return await GetStateAsync(candidateId, email, cancellationToken);
        // A lecturer-set status (plan §5) decides before the clock does; "requires review" does not block.
        if (context.StatusOverride == CandidateStatus.NotScheduled)
            throw new InvalidOperationException(L10n.T("You have no slot for this exam. Please contact your lecturer."));
        if (context.StatusOverride == CandidateStatus.Absent)
            throw new InvalidOperationException(L10n.T("You have been marked absent for this exam. Please contact your lecturer."));
        if (context.StatusOverride == CandidateStatus.Cancelled)
            throw new InvalidOperationException(L10n.T("Your participation in this exam was cancelled. Please contact your lecturer."));
        if (Now < context.SlotStartsAtUtc)
            throw new InvalidOperationException(L10n.T("Your exam slot has not started yet."));
        if (Now >= context.SlotEndsAtUtc)
            throw new InvalidOperationException(L10n.T("Your exam slot has ended."));
        if (context.Questions.Count == 0)
            throw new InvalidOperationException(L10n.T("No questions were assigned to you. Please contact your lecturer."));
        if (context.Recording != RecordingMode.None && !recordingConsent)
            throw new InvalidOperationException(L10n.T("This viva is recorded. Please agree to the recording to start, or contact your lecturer."));

        var first = new NewInterviewTurn(TurnKind.Main, 1, 0, context.Questions[0].Content, Now);
        var consent = context.Recording != RecordingMode.None && recordingConsent;
        if (await interviews.StartAsync(candidateId, Now, first, consent, cancellationToken))
        {
            logger.LogInformation("Candidate {CandidateId} started the interview", candidateId);
            await audit.WriteAsync(new AuditEntryInput(AuditActions.InterviewStarted, null, context.CandidateEmail, context.ExamId, candidateId,
                consent ? $"Agreed to {context.Recording} recording" : null), cancellationToken);
        }
        return await GetStateAsync(candidateId, email, cancellationToken);
    }

    public async Task<InterviewStateDto> AnswerAsync(int candidateId, string email, InterviewAnswerInput input, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(candidateId, email, cancellationToken);
        if (context.Record is not { Status: InterviewStatus.InProgress } record)
            return await ToStateAsync(context, cancellationToken);
        var current = record.Turns[^1];
        if (current.TurnId != input.TurnId || current.AnsweredAtUtc is not null)
            return await GetStateAsync(candidateId, email, cancellationToken);

        var raw = (input.Transcript ?? string.Empty).Trim();
        if (raw.Length > MaxTranscriptLength)
            raw = raw[..MaxTranscriptLength];
        var terms = await GlossaryAsync(context, cancellationToken);
        var transcript = TranscriptNormalizer.Normalize(raw, terms);
        if (transcript.Length > MaxTranscriptLength)
            transcript = transcript[..MaxTranscriptLength];
        var metrics = new AnswerMetrics(
            transcript == raw ? null : raw,
            input.ResponseDelayMs is >= 0 and < 3_600_000 ? input.ResponseDelayMs : null,
            input.SpeakingMs is >= 0 and < 3_600_000 ? input.SpeakingMs : null);
        var now = Now;
        var timedOut = now > current.AskedAtUtc.AddSeconds(context.AnswerTimeLimitSeconds) + Grace;
        if (!await interviews.SaveAnswerAsync(current.TurnId, transcript, input.InputMode, now, timedOut, cancellationToken, metrics))
            return await GetStateAsync(candidateId, email, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var (decision, next) = await DecideAsync(context, record, current, transcript, now, terms, cancellationToken);
        var latency = (int)stopwatch.ElapsedMilliseconds;
        if (await interviews.AdvanceAsync(current.TurnId, decision, next, Now, cancellationToken, latency))
            await AuditCompletedAsync(context, "all questions asked", cancellationToken);
        logger.LogInformation("Candidate {CandidateId} answered turn {TurnId}: {Decision} in {Latency} ms, next {Next}", candidateId, current.TurnId, decision,
            latency, next is null ? "end" : next.Kind.ToString());
        return await ToStateAsync(await LoadAsync(candidateId, email, cancellationToken), cancellationToken);
    }

    private Task AuditCompletedAsync(InterviewContext context, string reason, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditEntryInput(AuditActions.InterviewCompleted, null, context.CandidateEmail, context.ExamId, context.CandidateId, reason), cancellationToken);

    private async Task<IReadOnlyList<GlossaryTermDto>> GlossaryAsync(InterviewContext context, CancellationToken cancellationToken) =>
        context.SubjectId is { } subjectId ? await glossary.ListAsync(subjectId, cancellationToken) : [];

    private async Task<(FollowUpReason Decision, NewInterviewTurn? Next)> DecideAsync(
        InterviewContext context, InterviewRecordDto record, InterviewTurnRecordDto current, string transcript, DateTime now,
        IReadOnlyList<GlossaryTermDto> terms, CancellationToken cancellationToken)
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
        var request = new FollowUpRequest(context.Language, context.SubjectName, main.Content, main.ExpectedAnswer, exchanges, Math.Min(leftHere, leftTotal),
            terms.Select(term => term.Term).ToList());

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

    private async Task<InterviewStateDto> ToStateAsync(InterviewContext context, CancellationToken cancellationToken)
    {
        var state = ToState(context, Now);
        if (state.Status == InterviewStatus.Completed)
            return state;
        var speech = await settings.GetSpeechAsync(cancellationToken);
        var phrases = TranscriptNormalizer.Phrases(await GlossaryAsync(context, cancellationToken));
        return state with { Speech = speech, Phrases = phrases };
    }

    private static InterviewStateDto ToState(InterviewContext context, DateTime now)
    {
        var record = context.Record;
        var status = record?.Status ?? InterviewStatus.NotStarted;
        InterviewTurnDto? current = null;
        if (record is { Status: InterviewStatus.InProgress } && record.Turns[^1] is { AnsweredAtUtc: null } open)
            current = new InterviewTurnDto(open.TurnId, open.Kind, open.QuestionText, open.MainIndex, context.Questions.Count,
                open.FollowUpIndex, open.AskedAtUtc, context.AnswerTimeLimitSeconds);
        return new InterviewStateDto(context.CandidateId, context.ExamTitle, context.Language, status, context.SlotStartsAtUtc, context.SlotEndsAtUtc, current, now,
            context.Recording, context.RecordingConsentAtUtc is not null);
    }
}
