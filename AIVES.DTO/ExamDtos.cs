namespace AIVES.DTO;

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
    RecordingMode Recording = RecordingMode.Audio);

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
}

/// <summary>An active question that may be drawn for an exam.</summary>
public sealed record ExamPoolQuestion(int Id, int BloomLevelId, string BloomLevelName, string Content, string ExpectedAnswer, string? RubricJson = null);

/// <summary>A question handed to one candidate; the text is a snapshot taken when it was assigned.</summary>
public sealed record ExamAssignedQuestion(int Order, int? QuestionId, string Content, string ExpectedAnswer, string BloomLevelName, string? RubricJson = null);

/// <summary>One candidate as stored: the order fixes their time slot.</summary>
public sealed record ExamCandidateDraft(int Order, string Email, IReadOnlyList<ExamAssignedQuestion> Questions);

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
    RecordingMode Recording = RecordingMode.Audio);

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

public sealed record ExamCandidateDto(
    int Order,
    string Email,
    string? DisplayName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    IReadOnlyList<ExamAssignedQuestion> Questions,
    int CandidateId = 0,
    InterviewRecordDto? Interview = null);

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
    RecordingMode Recording = RecordingMode.Audio)
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
