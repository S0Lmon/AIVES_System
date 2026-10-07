using AIVES.BLL.Services.Diagnostics;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Catalog;
using AIVES.DTO;

namespace AIVES.BLL.Services.Dashboard;

public sealed class DashboardService(
    IExamService exams,
    IQuestionService questions,
    IRubricService rubrics,
    ICatalogService catalog,
    ISystemCheckService systemCheck) : IDashboardService
{
    public async Task<StudentDashboardDto> GetStudentDashboardAsync(string email, CancellationToken cancellationToken = default)
    {
        var allExams = await exams.ListForCandidateAsync(email, cancellationToken);
        var now = DateTime.UtcNow;

        var upcomingExams = allExams
            .Where(e => e.StartsAtUtc > now)
            .OrderBy(e => e.StartsAtUtc)
            .ToList();

        var pastExams = allExams
            .Where(e => e.StartsAtUtc <= now)
            .OrderByDescending(e => e.StartsAtUtc)
            .ToList();

        var completedExams = pastExams.Where(e => e.ResultAvailable).ToList();
        var completedCount = completedExams.Count;
        var upcomingCount = upcomingExams.Count;

        decimal? averageScore = null;
        if (completedCount > 0)
        {
            var totalScore = completedExams.Sum(e => e.FinalScore ?? 0m);
            averageScore = Math.Round(totalScore / completedCount, 2);
        }

        return new StudentDashboardDto(upcomingExams, pastExams, averageScore, completedCount, upcomingCount);
    }

    public async Task<LecturerDashboardDto> GetLecturerDashboardAsync(ExamActor actor, CancellationToken cancellationToken = default)
    {
        var allExams = await exams.ListAsync(actor, cancellationToken);
        var now = DateTime.UtcNow;

        var myUpcomingExams = allExams
            .Where(e => e.StartsAtUtc > now)
            .OrderBy(e => e.StartsAtUtc)
            .ToList();

        var myPastExams = allExams
            .Where(e => e.StartsAtUtc <= now)
            .OrderByDescending(e => e.StartsAtUtc)
            .ToList();

        // Question bank stats
        var allQuestions = (await questions.GetAllQuestionsAsync()).ToList();
        var activeQuestions = allQuestions.Where(q => q.IsActive).ToList();
        var questionsWithRubric = activeQuestions.Count(q => q.RubricId is > 0);
        var bloomSpread = activeQuestions.Select(q => q.BloomLevelId).Distinct().Count();
        var bySubject = activeQuestions.Count(q => q.SubjectId is > 0);
        var byTopic = activeQuestions.Count(q => q.TopicId is > 0);

        var questionBankStats = new QuestionBankStatsDto(
            allQuestions.Count,
            activeQuestions.Count,
            questionsWithRubric,
            bloomSpread,
            bySubject,
            byTopic);

        // Rubric stats
        var allRubrics = (await rubrics.GetAllRubricsAsync()).ToList();
        var rubricsWithCriteria = allRubrics.Count(r => r.Criteria.Count > 0);
        var rubricStats = new RubricStatsDto(allRubrics.Count, rubricsWithCriteria);

        // Subject stats
        var allSubjects = await catalog.GetSubjectsAsync(cancellationToken);
        var subjectsWithTopics = allSubjects.Count(s => s.TopicCount > 0);
        var subjectsWithMaterials = allSubjects.Count(s => s.MaterialCount > 0);
        var subjectStats = new SubjectStatsDto(allSubjects.Count, subjectsWithTopics, subjectsWithMaterials);

        return new LecturerDashboardDto(myUpcomingExams, myPastExams, questionBankStats, rubricStats, subjectStats);
    }

    public async Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
    {
        var adminActor = new ExamActor("admin", true);
        var allExams = await exams.ListAsync(adminActor, cancellationToken);
        var allQuestions = (await questions.GetAllQuestionsAsync()).ToList();
        var allRubrics = (await rubrics.GetAllRubricsAsync()).ToList();
        var allSubjects = await catalog.GetSubjectsAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var activeExamsNow = allExams.Count(e => e.StartsAtUtc <= now && e.EndsAtUtc > now);

        var report = await systemCheck.BuildReportAsync(cancellationToken);
        var health = new SystemHealthDto(
            report.Database.IsReachable,
            report.Checks.Any(c => c.Key == "Gemini" && c.Status == SystemCheckStatus.Ok),
            report.Checks.Any(c => c.Key == "Ollama" && c.Status == SystemCheckStatus.Ok),
            report.Checks.Any(c => c.Key == "SMTP" && c.Status == SystemCheckStatus.Ok));

        return new AdminDashboardDto(
            report.Database.UserCount,
            allExams.Count,
            allQuestions.Count,
            allRubrics.Count,
            allSubjects.Count,
            activeExamsNow,
            health);
    }
}