using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class ExamRepository(ApplicationDbContext context) : IExamRepository
{
    public async Task<IReadOnlyList<ExamPoolQuestion>> GetPoolAsync(int subjectId, int? topicId, CancellationToken cancellationToken = default) =>
        await context.Questions.AsNoTracking()
            .Where(question => question.IsActive && question.SubjectId == subjectId && (topicId == null || question.TopicId == topicId))
            .OrderBy(question => question.Id)
            .Select(question => new ExamPoolQuestion(question.Id, question.BloomLevelId, question.BloomLevel.Name, question.Content, question.ExpectedAnswer))
            .ToListAsync(cancellationToken);

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
                    .Select(question => new ExamAssignedQuestion(question.Order, question.QuestionId, question.Content, question.ExpectedAnswer, question.BloomLevelName))
                    .ToList());
        }).ToList();

        return new ExamDetailsDto(exam.Id, exam.Title, exam.SubjectId, exam.SubjectName, exam.TopicId, exam.TopicName,
            exam.StartsAtUtc, exam.StartsAtUtc.AddMinutes(exam.SlotMinutes * candidates.Count), exam.SlotMinutes,
            exam.MainQuestionCount, exam.MaxFollowUpQuestions, exam.CreatedById, candidates);
    }

    public async Task<IReadOnlyList<StudentExamDto>> ListForCandidateAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var rows = await context.ExamCandidates.AsNoTracking()
            .Where(candidate => candidate.Email == normalized)
            .Select(candidate => new
            {
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
                    start, start.AddMinutes(row.SlotMinutes), row.MainQuestionCount, row.MaxFollowUpQuestions);
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
    }

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
            BloomLevelName = question.BloomLevelName
        }).ToList()
    };
}
