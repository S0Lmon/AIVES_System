namespace AIVES.DTO;

/// <summary>Action names written to the audit log. Stored as text so old entries stay readable.</summary>
public static class AuditActions
{
    public const string ExamCreated = "exam.created";
    public const string ExamUpdated = "exam.updated";
    public const string ExamQuestionsRedrawn = "exam.questions-redrawn";
    public const string ExamDeleted = "exam.deleted";
    public const string InterviewStarted = "interview.started";
    public const string InterviewCompleted = "interview.completed";
    public const string RecordingConsent = "recording.consent";
    public const string RecordingStored = "recording.stored";
    public const string RecordingViewed = "recording.viewed";
    public const string RecordingPurged = "recording.purged";
    public const string AiGraded = "grade.ai-suggested";
    public const string AiGradingFailed = "grade.ai-failed";
    public const string AiGradingRequested = "grade.ai-requested";
    public const string GradeSaved = "grade.saved";
    public const string GradeFinalized = "grade.finalized";
    public const string GradeReopened = "grade.reopened";
    public const string GradeSheetExported = "report.gradesheet-exported";
    public const string UserRoleChanged = "user.role-changed";
    public const string UserLocked = "user.locked";
    public const string UserUnlocked = "user.unlocked";
    public const string SubjectLecturersChanged = "subject.lecturers-changed";
    public const string SettingsChanged = "settings.changed";
}

public sealed record AuditEntryInput(string Action, string? ActorId, string? ActorEmail, int? ExamId = null, int? CandidateId = null, string? Details = null);

public sealed record AuditEntryDto(long Id, DateTime AtUtc, string? ActorEmail, string Action, int? ExamId, int? CandidateId, string? Details);

public sealed record AuditQuery(int? ExamId = null, int? CandidateId = null, string? Action = null, string? Actor = null, int Take = 200);

/// <summary>Speech settings the administrator manages; the interview page reads them.</summary>
public sealed record SpeechSettingsDto(
    AppLanguage DefaultLanguage,
    IReadOnlyList<AppLanguage> EnabledLanguages,
    double SpeechRate,
    string? VietnameseVoice,
    string? EnglishVoice,
    int RecordingRetentionDays)
{
    public static readonly SpeechSettingsDto Default = new(AppLanguage.Vi, [AppLanguage.Vi, AppLanguage.En], 0.95, null, null, 365);

    public string? VoiceFor(AppLanguage language) => language == AppLanguage.Vi ? VietnameseVoice : EnglishVoice;
}

public sealed record GlossaryTermDto(int Id, int SubjectId, string Term, IReadOnlyList<string> SpokenForms);

public sealed record SubjectAssignmentDto(int SubjectId, string SubjectName, IReadOnlyList<UserRefDto> Lecturers);

public sealed record UserRefDto(string Id, string Email, string DisplayName);

/// <summary>Class-level statistics of one exam.</summary>
public sealed record ExamReportDto(
    int ExamId,
    string Title,
    string SubjectName,
    string? TopicName,
    DateTime StartsAtUtc,
    string CreatedById,
    int CandidateCount,
    int CompletedCount,
    int AiGradedCount,
    int FinalizedCount,
    decimal? Average,
    decimal? Median,
    decimal? Minimum,
    decimal? Maximum,
    decimal? PassRate,
    IReadOnlyList<ScoreBucketDto> Distribution,
    IReadOnlyList<QuestionStatDto> Questions,
    IReadOnlyList<BloomStatDto> BloomLevels,
    double? AverageAiLatencyMs,
    double? P95AiLatencyMs,
    double? FollowUpsPerAnswer);

/// <summary>Candidates whose final grade falls in [From, To) (the last bucket includes 10).</summary>
public sealed record ScoreBucketDto(decimal From, decimal To, int Count);

/// <summary>
/// How one bank question fared. Percent is the share of the question's points earned, using the
/// lecturer's score where set and the AI proposal otherwise.
/// </summary>
public sealed record QuestionStatDto(
    int? QuestionId,
    string Content,
    string BloomLevelName,
    int TimesAsked,
    decimal? AveragePercent,
    decimal? GoodAnswerRate,
    double FollowUpsPerAsk,
    int SufficientFirstTime);

public sealed record BloomStatDto(string BloomLevelName, int TimesAsked, decimal? AveragePercent);
