using System.Net.Mail;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Exams;

public sealed class ExamService(
    IExamRepository exams,
    ISubjectRepository subjects,
    ITopicRepository topics,
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
        var (draft, overlaps) = await BuildDraftAsync(input, actor.UserId, cancellationToken);
        var id = await exams.AddAsync(draft, cancellationToken);
        logger.LogInformation("User {UserId} created exam {ExamId} with {Candidates} candidates", actor.UserId, id, draft.Candidates.Count);
        return new ExamSaveResult(id, overlaps);
    }

    public async Task<ExamSaveResult> UpdateAsync(int id, ExamInput input, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var existing = await GetEditableAsync(id, actor, cancellationToken);
        var (draft, overlaps) = await BuildDraftAsync(input, existing.CreatedById, cancellationToken);
        await exams.UpdateAsync(id, draft, cancellationToken);
        logger.LogInformation("User {UserId} updated exam {ExamId}", actor.UserId, id);
        return new ExamSaveResult(id, overlaps);
    }

    public async Task<ExamSaveResult> ReassignQuestionsAsync(int id, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var existing = await GetEditableAsync(id, actor, cancellationToken);
        if (existing.SubjectId is not int subjectId)
            throw new InvalidOperationException(L10n.T("The subject of this exam was deleted, so new questions cannot be drawn."));
        var input = new ExamInput(existing.Title, subjectId, existing.TopicId, existing.StartsAtUtc, existing.SlotMinutes,
            existing.MainQuestionCount, existing.MaxFollowUpQuestions, existing.Candidates.Select(candidate => candidate.Email).ToList());
        var (draft, overlaps) = await BuildDraftAsync(input, existing.CreatedById, cancellationToken);
        await exams.UpdateAsync(id, draft, cancellationToken);
        logger.LogInformation("User {UserId} redrew the questions of exam {ExamId}", actor.UserId, id);
        return new ExamSaveResult(id, overlaps);
    }

    public async Task DeleteAsync(int id, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var exam = await exams.GetAsync(id, cancellationToken);
        if (exam is null || !CanManage(exam.CreatedById, actor))
            throw new KeyNotFoundException(L10n.T("The exam was not found."));
        // A started exam is a record of what was asked; only an administrator may remove it.
        if (exam.HasStarted(clock.GetUtcNow().UtcDateTime) && !actor.IsAdmin)
            throw new InvalidOperationException(L10n.T("This exam has already started, so it can no longer be deleted."));
        await exams.DeleteAsync(id, cancellationToken);
        logger.LogInformation("User {UserId} deleted exam {ExamId}", actor.UserId, id);
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

    private async Task<(ExamDraft Draft, int Overlaps)> BuildDraftAsync(ExamInput input, string ownerId, CancellationToken cancellationToken)
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
        if (input.StartsAtUtc <= clock.GetUtcNow().UtcDateTime)
            throw new ArgumentException(L10n.T("The exam must start in the future."), nameof(input));

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

        var allocation = ExamQuestionAllocator.Allocate(pool, emails.Count, input.MainQuestionCount, Random.Shared);
        var candidates = emails.Select((email, index) => new ExamCandidateDraft(index + 1, email,
            allocation.Sets[index].Select((question, order) =>
                new ExamAssignedQuestion(order + 1, question.Id, question.Content, question.ExpectedAnswer, question.BloomLevelName)).ToList()))
            .ToList();

        var draft = new ExamDraft(title, subject.Id, subject.Name, topic?.Id, topic?.Name, input.StartsAtUtc, input.SlotMinutes,
            input.MainQuestionCount, input.MaxFollowUpQuestions, ownerId, candidates);
        return (draft, allocation.ConsecutiveOverlaps);
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value && address.Host.Contains('.');
}
