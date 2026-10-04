using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class ExamRepository(ApplicationDbContext context) : IExamRepository
{
    public async Task<IReadOnlyList<ExamPoolQuestion>> GetPoolAsync(int subjectId, int? topicId, CancellationToken cancellationToken = default)
    {
        var questions = await context.Questions.AsNoTracking()
            .Where(question => question.IsActive && question.SubjectId == subjectId && (topicId == null || question.TopicId == topicId))
            .OrderBy(question => question.Id)
            .Select(question => new { question.Id, question.BloomLevelId, BloomName = question.BloomLevel.Name, question.Content, question.ExpectedAnswer, question.RubricId })
            .ToListAsync(cancellationToken);
        var rubricIds = questions.Where(question => question.RubricId != null).Select(question => question.RubricId!.Value).Distinct().ToList();
        var rubrics = rubricIds.Count == 0
            ? new Dictionary<int, string?>()
            : (await context.Rubrics.AsNoTracking()
                .Include(rubric => rubric.Levels)
                .Include(rubric => rubric.Criteria).ThenInclude(criterion => criterion.Levels)
                .AsSplitQuery()
                .Where(rubric => rubricIds.Contains(rubric.Id))
                .ToListAsync(cancellationToken))
                .ToDictionary(rubric => rubric.Id, rubric => Snapshot(rubric)?.ToJson());
        return questions.Select(question => new ExamPoolQuestion(question.Id, question.BloomLevelId, question.BloomName, question.Content, question.ExpectedAnswer,
            question.RubricId is { } rubricId ? rubrics.GetValueOrDefault(rubricId) : null)).ToList();
    }

    /// <summary>Freezes a rubric matrix as it is now; null when it has nothing to grade against.</summary>
    internal static RubricSnapshot? Snapshot(Rubric rubric)
    {
        var levels = rubric.Levels.OrderBy(level => level.Order).ToList();
        var criteria = rubric.Criteria.OrderBy(criterion => criterion.Order).ToList();
        if (levels.Count == 0 || criteria.Count == 0)
            return null;
        var snapshot = new RubricSnapshot(
            rubric.Name,
            levels.Select(level => new RubricSnapshotLevel(level.Name, level.Points)).ToList(),
            criteria.Select(criterion => new RubricSnapshotCriterion(
                criterion.Criterion,
                criterion.Description,
                criterion.MaxPoints,
                levels.Select(level =>
                {
                    var cell = criterion.Levels.FirstOrDefault(item => item.RubricLevelId == level.Id);
                    return new RubricSnapshotCell(level.Name, cell?.Descriptor ?? string.Empty, cell?.Points ?? level.Points);
                }).ToList())).ToList());
        return snapshot.TotalPoints > 0 ? snapshot : null;
    }

    public async Task<int> AddAsync(ExamDraft draft, CancellationToken cancellationToken = default)
    {
        var exam = new Exam { CreatedById = draft.CreatedById };
        Apply(draft, exam);
        foreach (var candidate in draft.Candidates)
            exam.Candidates.Add(ToEntity(candidate));
        context.Exams.Add(exam);
        await context.SaveChangesAsync(cancellationToken);
        return exam.Id;
    }

    public async Task UpdateAsync(int id, ExamDraft draft, CancellationToken cancellationToken = default)
    {
        var exam = await context.Exams.Include(item => item.Candidates).ThenInclude(candidate => candidate.Questions)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Exam {id} was not found.");

        // Two saves in one transaction: the old rows must be gone before the new ones reuse the
        // unique (ExamId, Order) and (ExamId, Email) values.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.ExamCandidates.RemoveRange(exam.Candidates);
        Apply(draft, exam);
        exam.ModifiedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        foreach (var candidate in draft.Candidates)
            exam.Candidates.Add(ToEntity(candidate));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var exam = await context.Exams.FindAsync([id], cancellationToken);
        if (exam is null)
            return;
        context.Exams.Remove(exam);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExamSummaryDto>> ListAsync(string? createdById, CancellationToken cancellationToken = default)
    {
        var rows = await context.Exams.AsNoTracking()
            .Where(exam => createdById == null || exam.CreatedById == createdById)
            .OrderByDescending(exam => exam.StartsAtUtc)
            .Select(exam => new { exam.Id, exam.Title, exam.SubjectName, exam.TopicName, exam.StartsAtUtc, exam.SlotMinutes, exam.CreatedById, Count = exam.Candidates.Count })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new ExamSummaryDto(row.Id, row.Title, row.SubjectName, row.TopicName, row.StartsAtUtc,
            row.StartsAtUtc.AddMinutes(row.SlotMinutes * row.Count), row.SlotMinutes, row.Count, row.CreatedById)).ToList();
    }

    public async Task<ExamDetailsDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var exam = await context.Exams.AsNoTracking()
            .Include(item => item.Candidates).ThenInclude(candidate => candidate.Questions)
            .Include(item => item.Candidates).ThenInclude(candidate => candidate.Attempt).ThenInclude(attempt => attempt!.Turns).ThenInclude(turn => turn.Recording)
            .AsSplitQuery()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (exam is null)
            return null;

        var normalized = exam.Candidates.Select(candidate => candidate.Email.ToUpperInvariant()).ToList();
        var names = await context.Users.AsNoTracking()
            .Where(user => user.NormalizedEmail != null && normalized.Contains(user.NormalizedEmail))
            .ToDictionaryAsync(user => user.NormalizedEmail!, user => user.DisplayName, cancellationToken);

        var candidates = exam.Candidates.OrderBy(candidate => candidate.Order).Select(candidate =>
        {
            var start = SlotStart(exam, candidate.Order);
            return new ExamCandidateDto(candidate.Order, candidate.Email,
                names.GetValueOrDefault(candidate.Email.ToUpperInvariant()),
                start, start.AddMinutes(exam.SlotMinutes),
                candidate.Questions.OrderBy(question => question.Order)
                    .Select(question => new ExamAssignedQuestion(question.Order, question.QuestionId, question.Content, question.ExpectedAnswer, question.BloomLevelName, question.RubricJson))
                    .ToList(),
                candidate.Id,
                candidate.Attempt is null ? null : ToRecord(candidate.Attempt));
        }).ToList();

        return new ExamDetailsDto(exam.Id, exam.Title, exam.SubjectId, exam.SubjectName, exam.TopicId, exam.TopicName,
            exam.StartsAtUtc, exam.StartsAtUtc.AddMinutes(exam.SlotMinutes * candidates.Count), exam.SlotMinutes,
            exam.MainQuestionCount, exam.MaxFollowUpQuestions, exam.CreatedById, candidates,
            exam.AnswerTimeLimitSeconds, exam.MaxFollowUpsPerQuestion, exam.Language.ToLanguage(), RecordingOf(exam));
    }

    internal static RecordingMode RecordingOf(Exam exam) =>
        exam.RecordVideo ? RecordingMode.AudioVideo : exam.RecordAudio ? RecordingMode.Audio : RecordingMode.None;

    public async Task<IReadOnlyList<StudentExamDto>> ListForCandidateAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var rows = await context.ExamCandidates.AsNoTracking()
            .Where(candidate => candidate.Email == normalized)
            .Select(candidate => new
            {
                CandidateId = candidate.Id,
                Status = candidate.Attempt == null ? (InterviewStatus?)null : candidate.Attempt.Status,
                FinalizedAtUtc = candidate.Attempt == null ? null : candidate.Attempt.FinalizedAtUtc,
                FinalScore = candidate.Attempt == null ? null : candidate.Attempt.FinalScore,
                candidate.Order,
                candidate.Exam.Id,
                candidate.Exam.Title,
                candidate.Exam.SubjectName,
                candidate.Exam.TopicName,
                candidate.Exam.StartsAtUtc,
                candidate.Exam.SlotMinutes,
                candidate.Exam.MainQuestionCount,
                candidate.Exam.MaxFollowUpQuestions,
                Count = candidate.Exam.Candidates.Count
            })
            .ToListAsync(cancellationToken);
        return rows.Select(row =>
            {
                var start = row.StartsAtUtc.AddMinutes(row.SlotMinutes * (row.Order - 1));
                return new StudentExamDto(row.Id, row.Title, row.SubjectName, row.TopicName, row.Order, row.Count,
                    start, start.AddMinutes(row.SlotMinutes), row.MainQuestionCount, row.MaxFollowUpQuestions,
                    row.CandidateId, row.Status ?? InterviewStatus.NotStarted, row.FinalizedAtUtc != null, row.FinalizedAtUtc != null ? row.FinalScore : null);
            })
            .OrderBy(exam => exam.StartsAtUtc)
            .ToList();
    }

    private static DateTime SlotStart(Exam exam, int order) => exam.StartsAtUtc.AddMinutes(exam.SlotMinutes * (order - 1));

    private static void Apply(ExamDraft draft, Exam exam)
    {
        exam.Title = draft.Title;
        exam.SubjectId = draft.SubjectId;
        exam.SubjectName = draft.SubjectName;
        exam.TopicId = draft.TopicId;
        exam.TopicName = draft.TopicName;
        exam.StartsAtUtc = draft.StartsAtUtc;
        exam.SlotMinutes = draft.SlotMinutes;
        exam.MainQuestionCount = draft.MainQuestionCount;
        exam.MaxFollowUpQuestions = draft.MaxFollowUpQuestions;
        exam.AnswerTimeLimitSeconds = draft.AnswerTimeLimitSeconds;
        exam.MaxFollowUpsPerQuestion = draft.MaxFollowUpsPerQuestion;
        exam.Language = draft.Language.ToSpeechLocale();
        exam.RecordAudio = draft.Recording != RecordingMode.None;
        exam.RecordVideo = draft.Recording == RecordingMode.AudioVideo;
    }

    internal static InterviewRecordDto ToRecord(ExamAttempt attempt) => new(attempt.Status, attempt.StartedAtUtc, attempt.CompletedAtUtc,
        attempt.Turns.OrderBy(turn => turn.Order).Select(turn => new InterviewTurnRecordDto(turn.Order, turn.Kind, turn.MainIndex,
            turn.QuestionText, turn.Answer, turn.InputMode, turn.AskedAtUtc, turn.AnsweredAtUtc, turn.TimedOut, turn.Decision,
            turn.Id, turn.FollowUpIndex, turn.RawAnswer, turn.ResponseDelayMs, turn.SpeakingMs, turn.DecisionLatencyMs,
            turn.Recording?.Id, turn.Recording?.HasVideo ?? false)).ToList());

    private static ExamCandidate ToEntity(ExamCandidateDraft draft) => new()
    {
        Order = draft.Order,
        Email = draft.Email,
        Questions = draft.Questions.Select(question => new ExamCandidateQuestion
        {
            Order = question.Order,
            QuestionId = question.QuestionId,
            Content = question.Content,
            ExpectedAnswer = question.ExpectedAnswer,
            BloomLevelName = question.BloomLevelName,
            RubricJson = question.RubricJson
        }).ToList()
    };
}
