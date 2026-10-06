using AIVES.DTO;

namespace AIVES.BLL.Services.Exams;

/// <summary>
/// Viva exam sessions. Lecturers see and change only their own exams; administrators see all.
/// An exam the actor may not see is reported as not found. Settings, candidates and question sets
/// are locked once the first slot has started, so the record of what was asked stays fixed.
/// Validation problems throw <see cref="ArgumentException"/>, a locked exam
/// <see cref="InvalidOperationException"/>, a missing or foreign exam <see cref="KeyNotFoundException"/>.
/// </summary>
public interface IExamService
{
    Task<IReadOnlyList<ExamSummaryDto>> ListAsync(ExamActor actor, CancellationToken cancellationToken = default);
    Task<ExamDetailsDto?> GetAsync(int id, ExamActor actor, CancellationToken cancellationToken = default);
    Task<ExamSaveResult> CreateAsync(ExamInput input, ExamActor actor, CancellationToken cancellationToken = default);
    Task<ExamSaveResult> UpdateAsync(int id, ExamInput input, ExamActor actor, CancellationToken cancellationToken = default);
    /// <summary>Draws a fresh question set for every candidate, keeping settings and candidates.</summary>
    Task<ExamSaveResult> ReassignQuestionsAsync(int id, ExamActor actor, CancellationToken cancellationToken = default);
    /// <summary>
    /// Moves one candidate's slot to a new start time (plan §6, manual adjustment). The new slot must
    /// not overlap any other candidate's slot. Blocked once the exam has started.
    /// </summary>
    Task AdjustSlotAsync(int id, int order, DateTime newStartsAtUtc, ExamActor actor, CancellationToken cancellationToken = default);
    /// <summary>Regenerates every slot from the exam's schedule settings, discarding manual moves (plan §6).</summary>
    Task ResetScheduleAsync(int id, ExamActor actor, CancellationToken cancellationToken = default);
    /// <summary>
    /// Sets or clears a lecturer's status override for one candidate: Absent, Cancelled, RequiresReview
    /// or NotScheduled may be set by hand; the other statuses derive from the attempt and the clock (plan §5).
    /// Unlike the settings, this stays available while the exam is running.
    /// </summary>
    Task SetCandidateStatusAsync(int id, int order, CandidateStatus? status, ExamActor actor, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, ExamActor actor, CancellationToken cancellationToken = default);
    /// <summary>Active questions available for a subject (and topic), for the form's hint.</summary>
    Task<int> CountPoolAsync(int subjectId, int? topicId, CancellationToken cancellationToken = default);
    /// <summary>Exams the given email sits, with that candidate's own slot.</summary>
    Task<IReadOnlyList<StudentExamDto>> ListForCandidateAsync(string email, CancellationToken cancellationToken = default);
    /// <summary>Splits pasted text (lines, commas or semicolons) into trimmed, lower-case, de-duplicated emails.</summary>
    IReadOnlyList<string> ParseCandidateEmails(string? text);
}
