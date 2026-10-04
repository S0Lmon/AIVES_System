using System.Text.Json;

namespace AIVES.DTO;

/// <summary>Where a finished viva is in AI grading. The lecturer's decision is tracked separately (finalized).</summary>
public enum GradingStatus
{
    /// <summary>Waiting for the AI grader (or for the interview to finish).</summary>
    Pending = 0,
    /// <summary>The AI has proposed scores for every question.</summary>
    AiGraded = 1,
    /// <summary>The AI could not grade; the lecturer grades by hand or retries.</summary>
    AiFailed = 2
}

/// <summary>A rubric frozen at assignment time so later edits to the bank do not change how an exam is graded.</summary>
public sealed record RubricSnapshot(string Name, IReadOnlyList<RubricSnapshotLevel> Levels, IReadOnlyList<RubricSnapshotCriterion> Criteria)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int TotalPoints => Criteria.Sum(criterion => criterion.MaxPoints);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static RubricSnapshot? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<RubricSnapshot>(json, JsonOptions);
            return snapshot is { Criteria.Count: > 0 } && snapshot.TotalPoints > 0 ? snapshot : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record RubricSnapshotLevel(string Name, int Points);

public sealed record RubricSnapshotCriterion(string Name, string Description, int MaxPoints, IReadOnlyList<RubricSnapshotCell> Cells);

public sealed record RubricSnapshotCell(string LevelName, string Descriptor, int Points);

/// <summary>Score for one rubric criterion, with the level reached and why.</summary>
public sealed record CriterionScoreDto(string Criterion, string? Level, decimal Points, decimal MaxPoints, string? Rationale)
{
    public static string ToJson(IReadOnlyList<CriterionScoreDto> scores) => JsonSerializer.Serialize(scores, RubricSnapshot.JsonOptions);

    public static IReadOnlyList<CriterionScoreDto> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<CriterionScoreDto>>(json, RubricSnapshot.JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>What the AI grader is given for one main question.</summary>
public sealed record GradingRequest(
    AppLanguage Language,
    string SubjectName,
    string Question,
    string ExpectedAnswer,
    string BloomLevel,
    RubricSnapshot? Rubric,
    decimal MaxScore,
    IReadOnlyList<InterviewExchange> Exchanges,
    IReadOnlyList<string> Glossary);

/// <summary>The AI's proposal for one question. Never a final grade.</summary>
public sealed record GradeSuggestion(
    decimal Score,
    IReadOnlyList<CriterionScoreDto> Criteria,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> MissingPoints,
    string Summary,
    string Model);

/// <summary>
/// Secondary signals measured during the viva. They describe how the answer was given (timing,
/// fluency), are shown next to the grade, and are never folded into the score automatically.
/// </summary>
public sealed record AnswerSignals(
    int Answers,
    int Unanswered,
    int TimedOut,
    int Typed,
    int FollowUps,
    double? AverageResponseDelaySeconds,
    double? AverageAnswerSeconds,
    double? WordsPerMinute,
    int FillerWords,
    double? FillerRatio,
    double? AverageAiLatencyMs)
{
    public static readonly AnswerSignals Empty = new(0, 0, 0, 0, 0, null, null, null, 0, null, null);
}

/// <summary>One main question as the lecturer grades it: the exchange, the AI proposal and the decision.</summary>
public sealed record GradingQuestionDto(
    int Id,
    int Order,
    string Content,
    string ExpectedAnswer,
    string BloomLevelName,
    RubricSnapshot? Rubric,
    decimal MaxScore,
    decimal? AiScore,
    IReadOnlyList<CriterionScoreDto> AiCriteria,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> MissingPoints,
    string? AiSummary,
    string? AiModel,
    decimal? LecturerScore,
    string? LecturerComment,
    IReadOnlyList<InterviewTurnRecordDto> Turns,
    AnswerSignals Signals,
    int? BankQuestionId = null)
{
    /// <summary>The lecturer's score; the AI score only stands in while nobody has decided.</summary>
    public decimal? EffectiveScore => LecturerScore ?? AiScore;
}

public sealed record CandidateGradingDto(
    int CandidateId,
    int ExamId,
    string ExamTitle,
    string SubjectName,
    string CreatedById,
    int Order,
    string Email,
    string? DisplayName,
    string? StudentCode,
    InterviewRecordDto? Interview,
    GradingStatus GradingStatus,
    string? GradingError,
    DateTime? RecordingConsentAtUtc,
    DateTime? FinalizedAtUtc,
    string? FinalizedByEmail,
    decimal? FinalScore,
    string? LecturerComment,
    IReadOnlyList<GradingQuestionDto> Questions,
    AnswerSignals Signals)
{
    public decimal MaxScore => Questions.Sum(question => question.MaxScore);

    /// <summary>The AI's total on a 10-point scale; null until every question has a proposal.</summary>
    public decimal? AiScore10 => Questions.Count == 0 || Questions.Any(question => question.AiScore is null)
        ? null
        : GradeMath.ToTen(Questions.Sum(question => question.AiScore!.Value), MaxScore);
}

public sealed record QuestionGradeInput(int QuestionId, decimal Score, string? Comment);

public sealed record CandidateGradeInput(IReadOnlyList<QuestionGradeInput> Questions, string? Comment, bool Finalize);

/// <summary>One row of an exam's grading overview and grade sheet.</summary>
public sealed record GradingRowDto(
    int CandidateId,
    int Order,
    string Email,
    string? DisplayName,
    string? StudentCode,
    InterviewStatus InterviewStatus,
    GradingStatus GradingStatus,
    decimal? AiScore10,
    decimal? FinalScore,
    DateTime? FinalizedAtUtc,
    IReadOnlyList<decimal?> QuestionScores,
    IReadOnlyList<decimal> QuestionMaxScores);

public sealed record ExamGradingDto(
    int ExamId,
    string Title,
    string SubjectName,
    string? TopicName,
    DateTime StartsAtUtc,
    int MainQuestionCount,
    string CreatedById,
    IReadOnlyList<GradingRowDto> Rows);

/// <summary>What the student sees once the lecturer has confirmed the grade.</summary>
public sealed record StudentResultDto(
    int CandidateId,
    string ExamTitle,
    string SubjectName,
    DateTime FinalizedAtUtc,
    decimal FinalScore,
    string? LecturerComment,
    IReadOnlyList<StudentQuestionResultDto> Questions);

public sealed record StudentQuestionResultDto(
    int Order,
    string Content,
    decimal? Score,
    decimal MaxScore,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> MissingPoints,
    string? AiSummary,
    string? LecturerComment,
    IReadOnlyList<InterviewExchange> Exchanges);

public static class GradeMath
{
    /// <summary>Score used when a question has no rubric.</summary>
    public const decimal DefaultMaxScore = 10m;

    /// <summary>Converts to the 0–10 scale used on grade sheets, rounded to the nearest 0.25.</summary>
    public static decimal ToTen(decimal score, decimal max) =>
        max <= 0 ? 0 : Math.Round(Math.Clamp(score / max * 10m, 0m, 10m) * 4m, MidpointRounding.AwayFromZero) / 4m;
}
