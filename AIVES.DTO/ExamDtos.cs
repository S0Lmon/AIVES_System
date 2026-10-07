namespace AIVES.DTO;

/// <summary>
/// How a candidate stands in the exam schedule. The first four are derived from the stored slot and
/// the attempt; <see cref="Absent"/>, <see cref="Cancelled"/>, <see cref="RequiresReview"/> and
/// <see cref="NotScheduled"/> are set by the lecturer as an override (see ExamCandidate.StatusOverride).
/// </summary>
public enum CandidateStatus
{
    /// <summary>No slot is assigned to the candidate.</summary>
    NotScheduled = 0,
    /// <summary>A slot is assigned and has not started yet.</summary>
    Scheduled = 1,
    /// <summary>The candidate's slot is open now, but the viva has not begun.</summary>
    Waiting = 2,
    /// <summary>The attempt is running.</summary>
    InProgress = 3,
    /// <summary>The attempt has finished.</summary>
    Completed = 4,
    /// <summary>The slot passed without an attempt (or marked by the lecturer).</summary>
    Absent = 5,
    /// <summary>The candidate was withdrawn from the exam but stays on the list for the record.</summary>
    Cancelled = 6,
    /// <summary>The exam needs a second look (flagged by the lecturer, e.g. before an appeal).</summary>
    RequiresReview = 7
}

/// <summary>How main questions are drawn for each candidate (plan §8: randomized selection).</summary>
public enum QuestionSelectionStrategy
{
    /// <summary>Default: avoid repeats between consecutive candidates, spread usage, cover Bloom levels.</summary>
    Balanced = 0,
    /// <summary>Uniformly random draw; only avoids giving one candidate the same question twice.</summary>
    Random = 1
}

/// <summary>What a lecturer submits to create or edit a viva exam session.</summary>
public sealed record ExamInput(
    string Title,
    int SubjectId,
    int? TopicId,
    DateTime StartsAtUtc,
    int SlotMinutes,
    int MainQuestionCount,
    int MaxFollowUpQuestions,
    IReadOnlyList<string> CandidateEmails,
    int AnswerTimeLimitSeconds = ExamLimits.DefaultAnswerSeconds,
    int MaxFollowUpsPerQuestion = ExamLimits.DefaultFollowUpsPerQuestion,
    AppLanguage Language = AppLanguage.Vi,
    RecordingMode Recording = RecordingMode.Audio,
    DateTime? EndsAtUtc = null,
    int BufferMinutes = 0,
    int BreakMinutes = 0,
    int BreakEveryCount = 0,
    QuestionSelectionStrategy Strategy = QuestionSelectionStrategy.Balanced,
    string? Term = null,
    string? ExamType = null,
    string? Instructions = null,
    bool ScheduleOverflowAllowed = false);

/// <summary>Limits shared by validation and the form.</summary>
public static class ExamLimits
{
    public const int TitleMaxLength = 200;
    public const int MinSlotMinutes = 5;
    public const int MaxSlotMinutes = 180;
    public const int MaxMainQuestions = 20;
    public const int MaxFollowUpQuestions = 20;
    public const int MaxCandidates = 300;
    public const int MinAnswerSeconds = 30;
    public const int MaxAnswerSeconds = 900;
    public const int DefaultAnswerSeconds = 120;
    public const int MaxFollowUpsPerQuestionLimit = 5;
    public const int DefaultFollowUpsPerQuestion = 2;
    public const int MaxBufferMinutes = 60;
    public const int MaxBreakMinutes = 120;
    public const int MaxBreakEveryCount = 50;
    public const int TermMaxLength = 60;
    public const int ExamTypeMaxLength = 60;
    public const int InstructionsMaxLength = 2000;
}

/// <summary>An active question that may be drawn for an exam.</summary>
public sealed record ExamPoolQuestion(int Id, int BloomLevelId, string BloomLevelName, string Content, string ExpectedAnswer, string? RubricJson = null);

/// <summary>A question handed to one candidate; the text is a snapshot taken when it was assigned.</summary>
public sealed record ExamAssignedQuestion(int Order, int? QuestionId, string Content, string ExpectedAnswer, string BloomLevelName, string? RubricJson = null);

/// <summary>One candidate as stored: the order fixes their time slot.</summary>
public sealed record ExamCandidateDraft(int Order, string Email, IReadOnlyList<ExamAssignedQuestion> Questions, DateTime? SlotStartsAtUtc = null);

/// <summary>A fully allocated exam ready to be written.</summary>
public sealed record ExamDraft(
    string Title,
    int SubjectId,
    string SubjectName,
    int? TopicId,
    string? TopicName,
    DateTime StartsAtUtc,
    int SlotMinutes,
    int MainQuestionCount,
    int MaxFollowUpQuestions,
    string CreatedById,
    IReadOnlyList<ExamCandidateDraft> Candidates,
    int AnswerTimeLimitSeconds = ExamLimits.DefaultAnswerSeconds,
    int MaxFollowUpsPerQuestion = ExamLimits.DefaultFollowUpsPerQuestion,
    AppLanguage Language = AppLanguage.Vi,
    RecordingMode Recording = RecordingMode.Audio,
    DateTime? EndsAtUtc = null,
    int BufferMinutes = 0,
    int BreakMinutes = 0,
    int BreakEveryCount = 0,
    QuestionSelectionStrategy Strategy = QuestionSelectionStrategy.Balanced,
    string? Term = null,
    string? ExamType = null,
    string? Instructions = null);

public sealed record ExamSummaryDto(
    int Id,
    string Title,
    string SubjectName,
    string? TopicName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int SlotMinutes,
    int CandidateCount,
    string CreatedById);

/// <summary>One candidate on the schedule, with the facts a status derives from.</summary>
public sealed record ExamCandidateDto(
    int Order,
    string Email,
    string? DisplayName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    IReadOnlyList<ExamAssignedQuestion> Questions,
    int CandidateId = 0,
    InterviewRecordDto? Interview = null,
    CandidateStatus? StatusOverride = null,
    decimal? FinalScore = null)
{
    /// <summary>
    /// Where the candidate stands at <paramref name="nowUtc"/> (plan §5). A lecturer's override wins,
    /// then the attempt, then the clock: their slot open means Waiting, a passed slot without an
    /// attempt means Absent, anything earlier means Scheduled.
    /// </summary>
    public CandidateStatus StatusAt(DateTime nowUtc)
    {
        if (StatusOverride is { } overridden)
            return overridden;
        if (Interview is { Status: InterviewStatus.InProgress })
            return CandidateStatus.InProgress;
        if (Interview is { Status: InterviewStatus.Completed })
            return CandidateStatus.Completed;
        if (nowUtc >= EndsAtUtc)
            return CandidateStatus.Absent;
        if (nowUtc >= StartsAtUtc)
            return CandidateStatus.Waiting;
        return CandidateStatus.Scheduled;
    }
}

public sealed record ExamDetailsDto(
    int Id,
    string Title,
    int? SubjectId,
    string SubjectName,
    int? TopicId,
    string? TopicName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int SlotMinutes,
    int MainQuestionCount,
    int MaxFollowUpQuestions,
    string CreatedById,
    IReadOnlyList<ExamCandidateDto> Candidates,
    int AnswerTimeLimitSeconds = ExamLimits.DefaultAnswerSeconds,
    int MaxFollowUpsPerQuestion = ExamLimits.DefaultFollowUpsPerQuestion,
    AppLanguage Language = AppLanguage.Vi,
    RecordingMode Recording = RecordingMode.Audio,
    int BufferMinutes = 0,
    int BreakMinutes = 0,
    int BreakEveryCount = 0,
    QuestionSelectionStrategy Strategy = QuestionSelectionStrategy.Balanced,
    string? Term = null,
    string? ExamType = null,
    string? Instructions = null,
    DateTime? ScheduleEndsAtUtc = null)
{
    public bool HasStarted(DateTime nowUtc) => nowUtc >= StartsAtUtc;
}

/// <summary>What a student sees about an exam they sit: their own slot, never the questions.</summary>
public sealed record StudentExamDto(
    int ExamId,
    string Title,
    string SubjectName,
    string? TopicName,
    int Order,
    int CandidateCount,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int MainQuestionCount,
    int MaxFollowUpQuestions,
    int CandidateId = 0,
    InterviewStatus? InterviewStatus = null,
    bool ResultAvailable = false,
    decimal? FinalScore = null);

/// <summary>Who is acting on an exam: lecturers manage their own exams, administrators every exam.</summary>
public sealed record ExamActor(string UserId, bool IsAdmin, string? Email = null);

/// <summary>Outcome of saving an exam: its id plus how often consecutive candidates still share a question.</summary>
public sealed record ExamSaveResult(int ExamId, int ConsecutiveOverlaps);
