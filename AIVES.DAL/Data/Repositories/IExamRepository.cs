using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface IExamRepository
{
    /// <summary>Active questions of a subject, narrowed to one topic when given.</summary>
    Task<IReadOnlyList<ExamPoolQuestion>> GetPoolAsync(int subjectId, int? topicId, CancellationToken cancellationToken = default);
    Task<int> AddAsync(ExamDraft draft, CancellationToken cancellationToken = default);
    /// <summary>Replaces the settings and the whole candidate list (with their questions).</summary>
    Task UpdateAsync(int id, ExamDraft draft, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Exams owned by <paramref name="createdById"/>, or every exam when it is null.</summary>
    Task<IReadOnlyList<ExamSummaryDto>> ListAsync(string? createdById, CancellationToken cancellationToken = default);
    Task<ExamDetailsDto?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentExamDto>> ListForCandidateAsync(string email, CancellationToken cancellationToken = default);
    /// <summary>Moves one candidate's slot to a new start time (manual adjustment, plan §6).</summary>
    Task UpdateSlotAsync(int examId, int order, DateTime slotStartsAtUtc, CancellationToken cancellationToken = default);
    /// <summary>Regenerates every slot from the exam's schedule settings, in sitting order (index 0 is candidate #1).</summary>
    Task UpdateScheduleAsync(int id, IReadOnlyList<DateTime> slotStartsAtUtc, CancellationToken cancellationToken = default);
    /// <summary>Sets or clears the lecturer's status override for one candidate (plan §5).</summary>
    Task UpdateCandidateStatusAsync(int examId, int order, CandidateStatus? status, CancellationToken cancellationToken = default);
}
