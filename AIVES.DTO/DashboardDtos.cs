namespace AIVES.DTO;

public sealed record StudentDashboardDto(
    IReadOnlyList<StudentExamDto> UpcomingExams,
    IReadOnlyList<StudentExamDto> PastExams,
    decimal? AverageScore,
    int CompletedCount,
    int UpcomingCount);

public sealed record LecturerDashboardDto(
    IReadOnlyList<ExamSummaryDto> MyUpcomingExams,
    IReadOnlyList<ExamSummaryDto> MyPastExams,
    QuestionBankStatsDto QuestionBank,
    RubricStatsDto Rubrics,
    SubjectStatsDto Subjects);

public sealed record AdminDashboardDto(
    int TotalUsers,
    int TotalExams,
    int TotalQuestions,
    int TotalRubrics,
    int TotalSubjects,
    int ActiveExamsNow,
    SystemHealthDto Health);

public sealed record QuestionBankStatsDto(
    int Total,
    int Active,
    int WithRubric,
    int BloomSpread,
    int BySubject,
    int ByTopic);

public sealed record RubricStatsDto(int Total, int WithCriteria);

public sealed record SubjectStatsDto(int Total, int WithTopics, int WithMaterials);

public sealed record SystemHealthDto(bool DbOk, bool GeminiOk, bool OllamaOk, bool SmtpOk);