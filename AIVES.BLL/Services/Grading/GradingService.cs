using System.Globalization;
using System.Text;
using AIVES.BLL.Services.Operations;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Grading;

/// <summary>
/// AI-assisted, lecturer-decided grading. The AI proposes a score and feedback per main question
/// once an interview is finished; the lecturer who owns the exam (or an administrator) reviews the
/// transcript, adjusts every score and confirms the grade. Only a confirmed grade reaches the student.
/// A foreign or missing candidate is reported as <see cref="KeyNotFoundException"/>, a request that
/// does not fit the current state as <see cref="InvalidOperationException"/>, bad input as <see cref="ArgumentException"/>.
/// </summary>
public interface IGradingService
{
    /// <summary>Grades the next finished interview waiting for the AI. Returns false when there was none.</summary>
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
    Task<CandidateGradingDto?> GetCandidateAsync(int candidateId, ExamActor actor, CancellationToken cancellationToken = default);
    Task<ExamGradingDto?> GetExamAsync(int examId, ExamActor actor, CancellationToken cancellationToken = default);
    Task SaveAsync(int candidateId, CandidateGradeInput input, ExamActor actor, CancellationToken cancellationToken = default);
    Task ReopenAsync(int candidateId, ExamActor actor, CancellationToken cancellationToken = default);
    Task RequestAiGradingAsync(int candidateId, ExamActor actor, CancellationToken cancellationToken = default);
    Task<StudentResultDto?> GetStudentResultAsync(int candidateId, string email, CancellationToken cancellationToken = default);
    Task<ExamReportDto?> GetReportAsync(int examId, ExamActor actor, CancellationToken cancellationToken = default);
}

public sealed class GradingService(
    IGradingRepository grading,
    IExamRepository exams,
    IAnswerGrader grader,
    IGlossaryService glossary,
    IAuditService audit,
    IOptions<GradingOptions> options,
    TimeProvider clock,
    ILogger<GradingService> logger) : IGradingService
{
    public const int MaxCommentLength = 2000;
    /// <summary>A claim older than this is treated as abandoned (the instance stopped mid-way).</summary>
    private static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(15);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var candidateId = await grading.ClaimNextAsync(Now, Now - ClaimLifetime, cancellationToken);
        if (candidateId is null)
            return false;
        await GradeCandidateAsync(candidateId.Value, cancellationToken);
        return true;
    }

    private async Task GradeCandidateAsync(int candidateId, CancellationToken cancellationToken)
    {
        var candidate = await grading.GetCandidateAsync(candidateId, cancellationToken);
        var settings = await grading.GetExamSettingsAsync(candidateId, cancellationToken);
        if (candidate is null || settings is null)
            return;
        var terms = settings.Value.SubjectId is { } subjectId
            ? (await glossary.ListAsync(subjectId, cancellationToken)).Select(term => term.Term).ToList()
            : [];

        var failures = new List<string>();
        foreach (var question in candidate.Questions)
        {
            var exchanges = question.Turns
                .Where(turn => turn.AnsweredAtUtc is not null)
                .Select(turn => new InterviewExchange(turn.QuestionText, turn.Answer ?? string.Empty))
                .ToList();
            try
            {
                var suggestion = await SuggestAsync(question, exchanges, settings.Value.Language, candidate.SubjectName, terms, cancellationToken);
                await grading.SaveAiSuggestionAsync(question.Id, question.MaxScore, suggestion, Now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "AI grading failed for question {QuestionId} of candidate {CandidateId}", question.Id, candidateId);
                failures.Add(L10n.Format("Question {0}: {1}", question.Order, ex is InvalidOperationException ? ex.Message : L10n.T("the AI response could not be used")));
            }
        }

        var status = failures.Count == 0 ? GradingStatus.AiGraded : GradingStatus.AiFailed;
        await grading.CompleteAiPassAsync(candidateId, status, failures.Count == 0 ? null : string.Join("; ", failures), cancellationToken);
        var graded = await grading.GetCandidateAsync(candidateId, cancellationToken);
        await audit.WriteAsync(new AuditEntryInput(
            status == GradingStatus.AiGraded ? AuditActions.AiGraded : AuditActions.AiGradingFailed,
            null, "AI", candidate.ExamId, candidateId,
            status == GradingStatus.AiGraded
                ? $"{string.Join(", ", graded?.Questions.Select(item => item.AiModel).OfType<string>().Distinct() ?? [])}: {Format(graded?.AiScore10)}/10 ({string.Join(", ", graded?.Questions.Select(item => $"Q{item.Order} {Format(item.AiScore)}/{Format(item.MaxScore)}") ?? [])})"
                : string.Join("; ", failures)), cancellationToken);
        logger.LogInformation("AI grading of candidate {CandidateId} finished: {Status}", candidateId, status);
    }

    private async Task<GradeSuggestion> SuggestAsync(GradingQuestionDto question, IReadOnlyList<InterviewExchange> exchanges, AppLanguage language,
        string subjectName, IReadOnlyList<string> terms, CancellationToken cancellationToken)
    {
        // Nothing said: no need to ask a model to confirm a zero.
        if (exchanges.All(exchange => string.IsNullOrWhiteSpace(exchange.Answer)))
        {
            var zeroes = question.Rubric?.Criteria.Select(criterion => new CriterionScoreDto(criterion.Name, null, 0, criterion.MaxPoints, L10n.T("No answer was given."))).ToList() ?? [];
            return new GradeSuggestion(0, zeroes, [], [L10n.T("No answer was given.")], [], L10n.T("No answer was given."), "rule");
        }
        if (!grader.IsConfigured)
            throw new InvalidOperationException(L10n.T("AI grading is not configured (Gemini API key missing)."));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, options.Value.TimeoutSeconds)));
        return await grader.GradeAsync(new GradingRequest(language, subjectName, question.Content, question.ExpectedAnswer, question.BloomLevelName,
            question.Rubric, question.MaxScore, exchanges, terms), timeout.Token);
    }

    public async Task<CandidateGradingDto?> GetCandidateAsync(int candidateId, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var candidate = await grading.GetCandidateAsync(candidateId, cancellationToken);
        return candidate is not null && CanManage(candidate.CreatedById, actor) ? Enrich(candidate) : null;
    }

    public async Task<ExamGradingDto?> GetExamAsync(int examId, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var exam = await exams.GetAsync(examId, cancellationToken);
        if (exam is null || !CanManage(exam.CreatedById, actor))
            return null;
        var candidates = (await grading.GetExamCandidatesAsync(examId, cancellationToken)).Select(Enrich).ToList();
        return new ExamGradingDto(exam.Id, exam.Title, exam.SubjectName, exam.TopicName, exam.StartsAtUtc, exam.MainQuestionCount, exam.CreatedById,
            candidates.Select(candidate => new GradingRowDto(
                candidate.CandidateId,
                candidate.Order,
                candidate.Email,
                candidate.DisplayName,
                candidate.StudentCode,
                candidate.Interview?.Status ?? InterviewStatus.NotStarted,
                candidate.GradingStatus,
                candidate.AiScore10,
                candidate.FinalizedAtUtc is null ? null : candidate.FinalScore,
                candidate.FinalizedAtUtc,
                candidate.Questions.Select(question => candidate.FinalizedAtUtc is null ? null : question.LecturerScore).ToList(),
                candidate.Questions.Select(question => question.MaxScore).ToList())).ToList());
    }

    public async Task SaveAsync(int candidateId, CandidateGradeInput input, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var candidate = await GetCandidateAsync(candidateId, actor, cancellationToken)
            ?? throw new KeyNotFoundException(L10n.T("The candidate was not found."));
        if (candidate.Interview is null)
            throw new InvalidOperationException(L10n.T("This candidate has not taken the viva, so there is nothing to grade."));
        if (candidate.FinalizedAtUtc is not null)
            throw new InvalidOperationException(L10n.T("This grade is already confirmed. Reopen it before changing it."));
        if (input.Finalize && candidate.Interview.Status != InterviewStatus.Completed)
            throw new InvalidOperationException(L10n.T("The viva is still in progress; the grade can be confirmed once it is finished."));
        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (comment?.Length > MaxCommentLength)
            throw new ArgumentException(L10n.Format("A comment cannot exceed {0} characters.", MaxCommentLength));

        var byId = input.Questions.GroupBy(item => item.QuestionId).ToDictionary(group => group.Key, group => group.Last());
        var grades = new List<(int, decimal, decimal, string?)>();
        var changes = new List<string>();
        foreach (var question in candidate.Questions)
        {
            if (!byId.TryGetValue(question.Id, out var entry))
            {
                if (input.Finalize)
                    throw new ArgumentException(L10n.Format("Give a score for question {0} before confirming.", question.Order));
                continue;
            }
            if (entry.Score < 0 || entry.Score > question.MaxScore)
                throw new ArgumentException(L10n.Format("The score for question {0} must be between 0 and {1}.", question.Order, Format(question.MaxScore)));
            var score = Math.Round(entry.Score * 4, MidpointRounding.AwayFromZero) / 4;
            var questionComment = string.IsNullOrWhiteSpace(entry.Comment) ? null : entry.Comment.Trim();
            if (questionComment?.Length > MaxCommentLength)
                throw new ArgumentException(L10n.Format("A comment cannot exceed {0} characters.", MaxCommentLength));
            grades.Add((question.Id, question.MaxScore, score, questionComment));
            if (question.LecturerScore != score)
                changes.Add($"Q{question.Order}: {Format(question.LecturerScore ?? question.AiScore)} → {Format(score)}/{Format(question.MaxScore)}" +
                    (question.AiScore is { } ai && ai != score ? $" (AI {Format(ai)})" : string.Empty));
        }

        decimal? final = input.Finalize ? GradeMath.ToTen(grades.Sum(grade => grade.Item3), candidate.MaxScore) : null;
        await grading.SaveLecturerGradesAsync(candidateId, grades, comment, actor.UserId, Now, final, cancellationToken);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeSaved, actor.UserId, actor.Email, candidate.ExamId, candidateId,
            changes.Count == 0 ? "No score changed" : string.Join("; ", changes)), cancellationToken);
        if (final is { } finalScore)
            await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeFinalized, actor.UserId, actor.Email, candidate.ExamId, candidateId,
                $"Final {Format(finalScore)}/10; AI proposed {Format(candidate.AiScore10)}/10"), cancellationToken);
    }

    public async Task ReopenAsync(int candidateId, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var candidate = await GetCandidateAsync(candidateId, actor, cancellationToken)
            ?? throw new KeyNotFoundException(L10n.T("The candidate was not found."));
        if (candidate.FinalizedAtUtc is null)
            return;
        var grades = candidate.Questions.Where(question => question.LecturerScore is not null)
            .Select(question => (question.Id, question.MaxScore, question.LecturerScore!.Value, question.LecturerComment)).ToList();
        await grading.SaveLecturerGradesAsync(candidateId, grades, candidate.LecturerComment, actor.UserId, Now, null, cancellationToken);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeReopened, actor.UserId, actor.Email, candidate.ExamId, candidateId,
            $"Was {Format(candidate.FinalScore)}/10"), cancellationToken);
    }

    public async Task RequestAiGradingAsync(int candidateId, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var candidate = await GetCandidateAsync(candidateId, actor, cancellationToken)
            ?? throw new KeyNotFoundException(L10n.T("The candidate was not found."));
        if (candidate.Interview?.Status != InterviewStatus.Completed)
            throw new InvalidOperationException(L10n.T("The viva is not finished yet, so the AI cannot grade it."));
        if (!await grading.RequeueAsync(candidateId, cancellationToken))
            throw new InvalidOperationException(L10n.T("The viva is not finished yet, so the AI cannot grade it."));
        await audit.WriteAsync(new AuditEntryInput(AuditActions.AiGradingRequested, actor.UserId, actor.Email, candidate.ExamId, candidateId), cancellationToken);
    }

    public async Task<StudentResultDto?> GetStudentResultAsync(int candidateId, string email, CancellationToken cancellationToken = default)
    {
        var candidate = await grading.GetCandidateAsync(candidateId, cancellationToken);
        if (candidate is null || string.IsNullOrWhiteSpace(email) || !string.Equals(candidate.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;
        if (candidate.FinalizedAtUtc is not { } finalizedAt || candidate.FinalScore is not { } finalScore)
            return null;
        return new StudentResultDto(candidate.CandidateId, candidate.ExamTitle, candidate.SubjectName, finalizedAt, finalScore, candidate.LecturerComment,
            candidate.Questions.Select(question => new StudentQuestionResultDto(
                question.Order,
                question.Content,
                question.LecturerScore,
                question.MaxScore,
                question.Strengths,
                question.Weaknesses,
                question.MissingPoints,
                question.AiSummary,
                question.LecturerComment,
                question.Turns.Where(turn => turn.AnsweredAtUtc is not null)
                    .Select(turn => new InterviewExchange(turn.QuestionText, turn.Answer ?? string.Empty)).ToList())).ToList());
    }

    public async Task<ExamReportDto?> GetReportAsync(int examId, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var exam = await exams.GetAsync(examId, cancellationToken);
        if (exam is null || !CanManage(exam.CreatedById, actor))
            return null;
        var candidates = (await grading.GetExamCandidatesAsync(examId, cancellationToken)).Select(Enrich).ToList();
        return ExamReportBuilder.Build(exam, candidates);
    }

    private static CandidateGradingDto Enrich(CandidateGradingDto candidate) => candidate with
    {
        StudentCode = StudentCodes.FromEmail(candidate.Email),
        Signals = AnswerSignalCalculator.Compute(candidate.Interview?.Turns ?? []),
        Questions = candidate.Questions.Select(question => question with { Signals = AnswerSignalCalculator.Compute(question.Turns) }).ToList()
    };

    private static bool CanManage(string createdById, ExamActor actor) => actor.IsAdmin || createdById == actor.UserId;

    private static string Format(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
}

/// <summary>Class statistics for the lecturer's report.</summary>
public static class ExamReportBuilder
{
    /// <summary>A question answered with at least this share of its points counts as answered well.</summary>
    public const decimal GoodAnswerThreshold = 0.7m;
    public const decimal PassMark = 5m;

    public static ExamReportDto Build(ExamDetailsDto exam, IReadOnlyList<CandidateGradingDto> candidates)
    {
        var finals = candidates.Where(candidate => candidate.FinalizedAtUtc is not null && candidate.FinalScore is not null)
            .Select(candidate => candidate.FinalScore!.Value).OrderBy(score => score).ToList();
        var distribution = Enumerable.Range(0, 10).Select(bucket => new ScoreBucketDto(bucket, bucket + 1,
            finals.Count(score => score >= bucket && (score < bucket + 1 || (bucket == 9 && score <= 10))))).ToList();

        var asked = candidates
            .Where(candidate => candidate.Interview is not null)
            .SelectMany(candidate => candidate.Questions.Where(question => question.Turns.Count > 0))
            .ToList();
        var questions = asked
            .GroupBy(question => question.BankQuestionId?.ToString(CultureInfo.InvariantCulture) ?? "text:" + question.Content.Trim())
            .Select(group =>
            {
                var first = group.First();
                var percents = group.Where(question => question.EffectiveScore is not null && question.MaxScore > 0)
                    .Select(question => question.EffectiveScore!.Value / question.MaxScore).ToList();
                return new QuestionStatDto(
                    first.BankQuestionId,
                    first.Content,
                    first.BloomLevelName,
                    group.Count(),
                    percents.Count == 0 ? null : Math.Round(percents.Average() * 100, 1),
                    percents.Count == 0 ? null : Math.Round((decimal)percents.Count(percent => percent >= GoodAnswerThreshold) / percents.Count * 100, 1),
                    Math.Round(group.Average(question => (double)question.Turns.Count(turn => turn.Kind == TurnKind.FollowUp)), 2),
                    group.Count(question => question.Turns.FirstOrDefault(turn => turn.Kind == TurnKind.Main)?.Decision == FollowUpReason.Sufficient));
            })
            .OrderBy(stat => stat.AveragePercent ?? decimal.MaxValue)
            .ThenByDescending(stat => stat.FollowUpsPerAsk)
            .ToList();
        var blooms = asked.GroupBy(question => question.BloomLevelName)
            .Select(group =>
            {
                var percents = group.Where(question => question.EffectiveScore is not null && question.MaxScore > 0)
                    .Select(question => question.EffectiveScore!.Value / question.MaxScore).ToList();
                return new BloomStatDto(group.Key, group.Count(), percents.Count == 0 ? null : Math.Round(percents.Average() * 100, 1));
            })
            .OrderBy(stat => stat.BloomLevelName)
            .ToList();

        var turns = candidates.SelectMany(candidate => candidate.Interview?.Turns ?? []).ToList();
        var latencies = turns.Where(turn => turn.DecisionLatencyMs is > 0).Select(turn => (double)turn.DecisionLatencyMs!.Value).OrderBy(value => value).ToList();
        var mains = turns.Count(turn => turn.Kind == TurnKind.Main && turn.AnsweredAtUtc is not null);

        return new ExamReportDto(
            exam.Id,
            exam.Title,
            exam.SubjectName,
            exam.TopicName,
            exam.StartsAtUtc,
            exam.CreatedById,
            candidates.Count,
            candidates.Count(candidate => candidate.Interview?.Status == InterviewStatus.Completed),
            candidates.Count(candidate => candidate.GradingStatus == GradingStatus.AiGraded),
            finals.Count,
            finals.Count == 0 ? null : Math.Round(finals.Average(), 2),
            finals.Count == 0 ? null : Median(finals),
            finals.Count == 0 ? null : finals[0],
            finals.Count == 0 ? null : finals[^1],
            finals.Count == 0 ? null : Math.Round((decimal)finals.Count(score => score >= PassMark) / finals.Count * 100, 1),
            distribution,
            questions,
            blooms,
            latencies.Count == 0 ? null : Math.Round(latencies.Average()),
            latencies.Count == 0 ? null : latencies[(int)Math.Min(latencies.Count - 1, Math.Ceiling(latencies.Count * 0.95) - 1)],
            mains == 0 ? null : Math.Round((double)turns.Count(turn => turn.Kind == TurnKind.FollowUp) / mains, 2));
    }

    private static decimal Median(IReadOnlyList<decimal> sorted) => sorted.Count % 2 == 1
        ? sorted[sorted.Count / 2]
        : Math.Round((sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2, 2);
}
