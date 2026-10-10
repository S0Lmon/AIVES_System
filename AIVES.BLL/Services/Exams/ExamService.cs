using AIVES.BLL.Services.Operations;
using AIVES.BLL.Services.Recordings;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using System.Net.Mail;

namespace AIVES.BLL.Services.Exams;

public sealed class ExamService(
    IExamRepository exams,
    ISubjectRepository subjects,
    ITopicRepository topics,
    ISubjectAccessService access,
    IAuditService audit,
    IRecordingService recordings,
    TimeProvider clock,
    ILogger<ExamService> logger) : IExamService
{
    private static readonly char[] EmailSeparators = ['\n', '\r', ',', ';', ' ', '\t'];

    public Task<IReadOnlyList<ExamSummaryDto>> ListAsync(ExamActor actor, CancellationToken cancellationToken = default) =>
        exams.ListAsync(actor.IsAdmin ? null : actor.UserId, cancellationToken);

    public async Task<ExamDetailsDto?> GetAsync(int id, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var exam = await exams.GetAsync(id, cancellationToken);
        return exam is not null && CanManage(exam.CreatedById, actor) ? exam : null;
    }

    public async Task<ExamSaveResult> CreateAsync(ExamInput input, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var (draft, overlaps) = await BuildDraftAsync(input, actor.UserId, actor, cancellationToken);
        var id = await exams.AddAsync(draft, cancellationToken);
        logger.LogInformation("User {UserId} created exam {ExamId} with {Candidates} candidates", actor.UserId, id, draft.Candidates.Count);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamCreated, actor.UserId, actor.Email, id, null, Describe(draft)), cancellationToken);
        return new ExamSaveResult(id, overlaps);
    }

    public async Task<ExamSaveResult> UpdateAsync(int id, ExamInput input, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var existing = await GetEditableAsync(id, actor, cancellationToken);
        var (draft, overlaps) = await BuildDraftAsync(input, existing.CreatedById, actor, cancellationToken);
        await exams.UpdateAsync(id, draft, cancellationToken);
        logger.LogInformation("User {UserId} updated exam {ExamId}", actor.UserId, id);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamUpdated, actor.UserId, actor.Email, id, null, Describe(draft)), cancellationToken);
        return new ExamSaveResult(id, overlaps);
    }

    public async Task<ExamSaveResult> ReassignQuestionsAsync(int id, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var existing = await GetEditableAsync(id, actor, cancellationToken);
        if (existing.SubjectId is not int subjectId)
            throw new InvalidOperationException(L10n.T("The subject of this exam was deleted, so new questions cannot be drawn."));
        var input = new ExamInput(existing.Title, subjectId, existing.TopicId, existing.StartsAtUtc, existing.SlotMinutes,
            existing.MainQuestionCount, existing.MaxFollowUpQuestions, existing.Candidates.Select(candidate => candidate.Email).ToList(),
            existing.AnswerTimeLimitSeconds, existing.MaxFollowUpsPerQuestion, existing.Language, existing.Recording,
            existing.EndsAtUtc, existing.BufferMinutes, existing.BreakMinutes, existing.BreakEveryCount, existing.Strategy,
            existing.Term, existing.ExamType, existing.Instructions,
            // A redone schedule must not fail a window an earlier save already let run past its end.
            ScheduleOverflowAllowed: existing.ScheduleEndsAtUtc > existing.EndsAtUtc);
        var (draft, overlaps) = await BuildDraftAsync(input, existing.CreatedById, actor, cancellationToken);
        await exams.UpdateAsync(id, draft, cancellationToken);
        logger.LogInformation("User {UserId} redrew the questions of exam {ExamId}", actor.UserId, id);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamQuestionsRedrawn, actor.UserId, actor.Email, id, null, Describe(draft)), cancellationToken);
        return new ExamSaveResult(id, overlaps);
    }

    public async Task AdjustSlotAsync(int id, int order, DateTime newStartsAtUtc, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var existing = await GetEditableAsync(id, actor, cancellationToken);
        var candidate = existing.Candidates.FirstOrDefault(item => item.Order == order)
            ?? throw new KeyNotFoundException(L10n.T("That candidate is not on this exam."));
        if (newStartsAtUtc < existing.StartsAtUtc)
            throw new ArgumentException(L10n.T("A slot cannot start before the exam's start time."), nameof(newStartsAtUtc));
        var slotEndsAt = newStartsAtUtc.AddMinutes(existing.SlotMinutes);
        var overlap = existing.Candidates.FirstOrDefault(other => other.Order != order
            && newStartsAtUtc < other.EndsAtUtc && slotEndsAt > other.StartsAtUtc);
        if (overlap is not null)
            throw new ArgumentException(L10n.Format("That time overlaps candidate #{0} ({1} – {2}). Move them to a free gap instead.",
                overlap.Order, overlap.StartsAtUtc.ToString("HH:mm"), overlap.EndsAtUtc.ToString("HH:mm")), nameof(newStartsAtUtc));

        await exams.UpdateSlotAsync(id, order, newStartsAtUtc, cancellationToken);
        logger.LogInformation("User {UserId} moved exam {ExamId} candidate #{Order} to {SlotStartsAtUtc:u}", actor.UserId, id, order, newStartsAtUtc);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamSlotAdjusted, actor.UserId, actor.Email, id, null,
            $"#{order} {candidate.Email} → {newStartsAtUtc:yyyy-MM-dd HH:mm} UTC"), cancellationToken);
    }

    public async Task ResetScheduleAsync(int id, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var existing = await GetEditableAsync(id, actor, cancellationToken);
        var slots = ExamSchedule.Build(existing.StartsAtUtc, existing.SlotMinutes, existing.Candidates.Count,
            existing.BufferMinutes, existing.BreakMinutes, existing.BreakEveryCount);
        await exams.UpdateScheduleAsync(id, slots, cancellationToken);
        logger.LogInformation("User {UserId} reset the schedule of exam {ExamId}", actor.UserId, id);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamScheduleReset, actor.UserId, actor.Email, id, null,
            $"{existing.Candidates.Count} slot(s) regenerated from the exam settings"), cancellationToken);
    }

    public async Task SetCandidateStatusAsync(int id, int order, CandidateStatus? status, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var exam = await exams.GetAsync(id, cancellationToken);
        if (exam is null || !CanManage(exam.CreatedById, actor))
            throw new KeyNotFoundException(L10n.T("The exam was not found."));
        var candidate = exam.Candidates.FirstOrDefault(item => item.Order == order)
            ?? throw new KeyNotFoundException(L10n.T("That candidate is not on this exam."));
        if (status is { } overridden && overridden is not (CandidateStatus.Absent or CandidateStatus.Cancelled
            or CandidateStatus.RequiresReview or CandidateStatus.NotScheduled))
            throw new ArgumentException(L10n.T("Only absent, cancelled, requires review or not scheduled can be set by hand."), nameof(status));

        await exams.UpdateCandidateStatusAsync(id, order, status, cancellationToken);
        logger.LogInformation("User {UserId} set exam {ExamId} candidate #{Order} status to {Status}", actor.UserId, id, order, status?.ToString() ?? "derived");
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamCandidateStatusChanged, actor.UserId, actor.Email, id, null,
            $"#{order} {candidate.Email} → {status?.ToString() ?? "derived from the schedule"}"), cancellationToken);
    }

    public async Task DeleteAsync(int id, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var exam = await exams.GetAsync(id, cancellationToken);
        if (exam is null || !CanManage(exam.CreatedById, actor))
            throw new KeyNotFoundException(L10n.T("The exam was not found."));
        // A started exam is a record of what was asked; only an administrator may remove it.
        if (exam.HasStarted(clock.GetUtcNow().UtcDateTime) && !actor.IsAdmin)
            throw new InvalidOperationException(L10n.T("This exam has already started, so it can no longer be deleted."));
        // Recordings live outside the database; remove them with the exam so no evidence is orphaned.
        await recordings.DeleteForExamAsync(id, cancellationToken);
        await exams.DeleteAsync(id, cancellationToken);
        logger.LogInformation("User {UserId} deleted exam {ExamId}", actor.UserId, id);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamDeleted, actor.UserId, actor.Email, id, null,
            $"{exam.Title} ({exam.SubjectName}), {exam.Candidates.Count} candidate(s)"), cancellationToken);
    }

    public async Task<int> CountPoolAsync(int subjectId, int? topicId, CancellationToken cancellationToken = default) =>
        (await exams.GetPoolAsync(subjectId, topicId, cancellationToken)).Count;

    public Task<IReadOnlyList<StudentExamDto>> ListForCandidateAsync(string email, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(email) ? Task.FromResult<IReadOnlyList<StudentExamDto>>([]) : exams.ListForCandidateAsync(email, cancellationToken);

    public IReadOnlyList<string> ParseCandidateEmails(string? text) =>
        (text ?? string.Empty).Split(EmailSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(email => email.ToLowerInvariant())
            .Distinct()
            .ToList();

    private static bool CanManage(string createdById, ExamActor actor) => actor.IsAdmin || createdById == actor.UserId;

    private async Task<ExamDetailsDto> GetEditableAsync(int id, ExamActor actor, CancellationToken cancellationToken)
    {
        var exam = await exams.GetAsync(id, cancellationToken);
        if (exam is null || !CanManage(exam.CreatedById, actor))
            throw new KeyNotFoundException(L10n.T("The exam was not found."));
        if (exam.HasStarted(clock.GetUtcNow().UtcDateTime))
            throw new InvalidOperationException(L10n.T("This exam has already started, so its settings, candidates and questions are locked."));
        return exam;
    }

    private static string Describe(ExamDraft draft)
    {
        var schedule = $"{draft.SlotMinutes} min/candidate";
        if (draft.BufferMinutes > 0)
            schedule += $", {draft.BufferMinutes} min buffer";
        if (draft.BreakMinutes > 0 && draft.BreakEveryCount > 0)
            schedule += $", {draft.BreakMinutes} min break every {draft.BreakEveryCount}";
        var window = draft.EndsAtUtc is { } endsAt ? $" | window ends {endsAt:yyyy-MM-dd HH:mm} UTC" : string.Empty;
        var strategy = draft.Strategy == QuestionSelectionStrategy.Balanced ? string.Empty : $" | questions {draft.Strategy}";
        return $"{draft.Title} | {draft.SubjectName}{(draft.TopicName is null ? "" : " / " + draft.TopicName)} | start {draft.StartsAtUtc:yyyy-MM-dd HH:mm} UTC{window} | " +
            $"{schedule} | {draft.MainQuestionCount} main, {draft.MaxFollowUpQuestions} follow-ups | {draft.Language} | recording {draft.Recording}{strategy} | {draft.Candidates.Count} candidate(s)";
    }

    private async Task<(ExamDraft Draft, int Overlaps)> BuildDraftAsync(ExamInput input, string ownerId, ExamActor actor, CancellationToken cancellationToken)
    {
        var title = (input.Title ?? string.Empty).Trim();
        if (title.Length is < 2 or > ExamLimits.TitleMaxLength)
            throw new ArgumentException(L10n.Format("The exam title must be between 2 and {0} characters.", ExamLimits.TitleMaxLength), nameof(input));
        if (input.SlotMinutes is < ExamLimits.MinSlotMinutes or > ExamLimits.MaxSlotMinutes)
            throw new ArgumentException(L10n.Format("Each candidate's slot must be between {0} and {1} minutes.", ExamLimits.MinSlotMinutes, ExamLimits.MaxSlotMinutes), nameof(input));
        if (input.MainQuestionCount is < 1 or > ExamLimits.MaxMainQuestions)
            throw new ArgumentException(L10n.Format("The number of main questions must be between 1 and {0}.", ExamLimits.MaxMainQuestions), nameof(input));
        if (input.MaxFollowUpQuestions is < 0 or > ExamLimits.MaxFollowUpQuestions)
            throw new ArgumentException(L10n.Format("The number of follow-up questions must be between 0 and {0}.", ExamLimits.MaxFollowUpQuestions), nameof(input));
        if (input.AnswerTimeLimitSeconds is < ExamLimits.MinAnswerSeconds or > ExamLimits.MaxAnswerSeconds)
            throw new ArgumentException(L10n.Format("The answer time limit must be between {0} and {1} seconds.", ExamLimits.MinAnswerSeconds, ExamLimits.MaxAnswerSeconds), nameof(input));
        if (input.AnswerTimeLimitSeconds > input.SlotMinutes * 60)
            throw new ArgumentException(L10n.T("The answer time limit cannot be longer than a candidate's slot."), nameof(input));
        if (input.MaxFollowUpsPerQuestion is < 0 or > ExamLimits.MaxFollowUpsPerQuestionLimit)
            throw new ArgumentException(L10n.Format("Follow-ups per question must be between 0 and {0}.", ExamLimits.MaxFollowUpsPerQuestionLimit), nameof(input));
        if (!Enum.IsDefined(input.Language))
            throw new ArgumentException(L10n.T("Choose the interview language."), nameof(input));
        if (!Enum.IsDefined(input.Recording))
            throw new ArgumentException(L10n.T("Choose what is recorded."), nameof(input));
        if (input.StartsAtUtc <= clock.GetUtcNow().UtcDateTime)
            throw new ArgumentException(L10n.T("The exam must start in the future."), nameof(input));
        if (!Enum.IsDefined(input.Strategy))
            throw new ArgumentException(L10n.T("Choose how questions are selected."), nameof(input));
        if (input.BufferMinutes is < 0 or > ExamLimits.MaxBufferMinutes)
            throw new ArgumentException(L10n.Format("The buffer between candidates must be between 0 and {0} minutes.", ExamLimits.MaxBufferMinutes), nameof(input));
        if (input.BreakMinutes is < 0 or > ExamLimits.MaxBreakMinutes)
            throw new ArgumentException(L10n.Format("The break length must be between 0 and {0} minutes.", ExamLimits.MaxBreakMinutes), nameof(input));
        if (input.BreakEveryCount is < 0 or > ExamLimits.MaxBreakEveryCount)
            throw new ArgumentException(L10n.Format("A break can be scheduled after every 1 to {0} candidates.", ExamLimits.MaxBreakEveryCount), nameof(input));
        if (input.BreakMinutes > 0 && input.BreakEveryCount == 0)
            throw new ArgumentException(L10n.T("Enter after how many candidates a break happens, or set the break length to 0."), nameof(input));
        if (input.BreakEveryCount > 0 && input.BreakMinutes == 0)
            throw new ArgumentException(L10n.T("Enter the break length in minutes, or set 'break after every' to 0."), nameof(input));
        if ((input.Term ?? string.Empty).Trim().Length > ExamLimits.TermMaxLength)
            throw new ArgumentException(L10n.Format("The term must be at most {0} characters.", ExamLimits.TermMaxLength), nameof(input));
        if ((input.ExamType ?? string.Empty).Trim().Length > ExamLimits.ExamTypeMaxLength)
            throw new ArgumentException(L10n.Format("The exam type must be at most {0} characters.", ExamLimits.ExamTypeMaxLength), nameof(input));
        if ((input.Instructions ?? string.Empty).Trim().Length > ExamLimits.InstructionsMaxLength)
            throw new ArgumentException(L10n.Format("The instructions must be at most {0} characters.", ExamLimits.InstructionsMaxLength), nameof(input));
        if (input.EndsAtUtc is { } explicitEndsAt && explicitEndsAt <= input.StartsAtUtc)
            throw new ArgumentException(L10n.T("The exam must end after it starts."), nameof(input));

        var emails = input.CandidateEmails.Select(email => email.Trim().ToLowerInvariant()).Where(email => email.Length > 0).Distinct().ToList();
        if (emails.Count == 0)
            throw new ArgumentException(L10n.T("Add at least one candidate email."), nameof(input));
        if (emails.Count > ExamLimits.MaxCandidates)
            throw new ArgumentException(L10n.Format("An exam can have at most {0} candidates.", ExamLimits.MaxCandidates), nameof(input));
        var invalid = emails.Where(email => !IsEmail(email)).ToList();
        if (invalid.Count > 0)
            throw new ArgumentException(L10n.Format("These candidate emails are not valid: {0}", string.Join(", ", invalid.Take(10))), nameof(input));

        var subject = await subjects.GetByIdAsync(input.SubjectId, cancellationToken)
            ?? throw new ArgumentException(L10n.T("Pick a subject from the catalogue."), nameof(input));
        if (!await access.CanUseAsync(subject.Id, actor, cancellationToken))
            throw new ArgumentException(L10n.T("You are not assigned to this subject. Ask an administrator to assign you."), nameof(input));
        TopicDto? topic = null;
        if (input.TopicId is int topicId)
        {
            topic = await topics.GetByIdAsync(topicId, cancellationToken);
            if (topic is null || topic.SubjectId != subject.Id)
                throw new ArgumentException(L10n.T("That topic does not belong to the chosen subject."), nameof(input));
        }

        var pool = await exams.GetPoolAsync(subject.Id, topic?.Id, cancellationToken);
        if (pool.Count < input.MainQuestionCount)
            throw new ArgumentException(L10n.Format("Only {0} active questions match this subject and topic, but each candidate needs {1}. Add questions or lower the number.", pool.Count, input.MainQuestionCount), nameof(input));

        // Build the schedule (plan §6). It must fit the chosen window unless the lecturer allows the
        // schedule to run past the end time (plan §14); without an end time the schedule defines the window.
        var slots = ExamSchedule.Build(input.StartsAtUtc, input.SlotMinutes, emails.Count,
            input.BufferMinutes, input.BreakMinutes, input.BreakEveryCount);
        var scheduleEndsAt = slots[^1].AddMinutes(input.SlotMinutes);
        var windowEndsAt = input.EndsAtUtc ?? scheduleEndsAt;
        if (scheduleEndsAt > windowEndsAt && !input.ScheduleOverflowAllowed)
            throw new ArgumentException(L10n.Format(
                "The schedule does not fit the exam window: {0} candidate(s) need {1} minutes from the start (including buffer and breaks), but the window is {2} minutes. Extend the end time, reduce the buffer or breaks, or allow the schedule to run past the end time.",
                emails.Count, (int)(scheduleEndsAt - input.StartsAtUtc).TotalMinutes, (int)(windowEndsAt - input.StartsAtUtc).TotalMinutes), nameof(input));

        var allocation = ExamQuestionAllocator.Allocate(pool, emails.Count, input.MainQuestionCount, Random.Shared, input.Strategy);
        var candidates = emails.Select((email, index) => new ExamCandidateDraft(index + 1, email,
            allocation.Sets[index].Select((question, order) =>
                new ExamAssignedQuestion(order + 1, question.Id, question.Content, question.ExpectedAnswer, question.BloomLevelName, question.RubricJson)).ToList(),
            slots[index]))
            .ToList();

        var draft = new ExamDraft(title, subject.Id, subject.Name, topic?.Id, topic?.Name, input.StartsAtUtc, input.SlotMinutes,
            input.MainQuestionCount, input.MaxFollowUpQuestions, ownerId, candidates,
            input.AnswerTimeLimitSeconds, input.MaxFollowUpsPerQuestion, input.Language, input.Recording,
            windowEndsAt, input.BufferMinutes, input.BreakMinutes, input.BreakEveryCount, input.Strategy,
            input.Term?.Trim(), input.ExamType?.Trim(), input.Instructions?.Trim());
        return (draft, allocation.ConsecutiveOverlaps);
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value && address.Host.Contains('.');
}
