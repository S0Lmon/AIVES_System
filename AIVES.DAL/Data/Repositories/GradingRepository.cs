using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public interface IGradingRepository
{
    /// <summary>
    /// Claims one finished interview that still waits for AI grading, so that only one web instance
    /// grades it. A claim older than <paramref name="staleBeforeUtc"/> is considered abandoned.
    /// Returns the candidate id, or null when there is nothing to do.
    /// </summary>
    Task<int?> ClaimNextAsync(DateTime nowUtc, DateTime staleBeforeUtc, CancellationToken cancellationToken = default);

    Task<CandidateGradingDto?> GetCandidateAsync(int candidateId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CandidateGradingDto>> GetExamCandidatesAsync(int examId, CancellationToken cancellationToken = default);

    /// <summary>Exam language, subject and the candidate's email, for the grader and access checks.</summary>
    Task<(AppLanguage Language, int? SubjectId)?> GetExamSettingsAsync(int candidateId, CancellationToken cancellationToken = default);

    Task SaveAiSuggestionAsync(int examCandidateQuestionId, decimal maxScore, GradeSuggestion suggestion, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Ends the AI pass over an attempt and releases the claim.</summary>
    Task CompleteAiPassAsync(int candidateId, GradingStatus status, string? error, CancellationToken cancellationToken = default);

    /// <summary>Puts a finished interview back in the AI grading queue (keeps lecturer scores).</summary>
    Task<bool> RequeueAsync(int candidateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the lecturer's scores and comments. With <paramref name="finalScore"/> set the grade is
    /// confirmed and the student can see it; null leaves (or puts) it back in draft.
    /// </summary>
    Task SaveLecturerGradesAsync(int candidateId, IReadOnlyList<(int QuestionId, decimal MaxScore, decimal Score, string? Comment)> grades,
        string? comment, string userId, DateTime nowUtc, decimal? finalScore, CancellationToken cancellationToken = default);
}

public sealed class GradingRepository(ApplicationDbContext context) : IGradingRepository
{
    public async Task<int?> ClaimNextAsync(DateTime nowUtc, DateTime staleBeforeUtc, CancellationToken cancellationToken = default)
    {
        var candidates = await context.ExamAttempts
            .Where(attempt => attempt.Status == InterviewStatus.Completed && attempt.GradingStatus == GradingStatus.Pending
                && (attempt.GradingClaimedAtUtc == null || attempt.GradingClaimedAtUtc < staleBeforeUtc))
            .OrderBy(attempt => attempt.CompletedAtUtc)
            .Take(3)
            .ToListAsync(cancellationToken);
        foreach (var attempt in candidates)
        {
            attempt.GradingClaimedAtUtc = nowUtc;
            try
            {
                // GradingClaimedAtUtc is a concurrency token: another instance that claimed it first wins.
                await context.SaveChangesAsync(cancellationToken);
                return attempt.ExamCandidateId;
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
            }
        }
        return null;
    }

    public async Task<CandidateGradingDto?> GetCandidateAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var candidate = await Candidates()
            .FirstOrDefaultAsync(item => item.Id == candidateId, cancellationToken);
        if (candidate is null)
            return null;
        var names = await NamesAsync([candidate.Email, .. FinalizerIds([candidate])], cancellationToken);
        return ToDto(candidate, names);
    }

    public async Task<IReadOnlyList<CandidateGradingDto>> GetExamCandidatesAsync(int examId, CancellationToken cancellationToken = default)
    {
        var candidates = await Candidates()
            .Where(item => item.ExamId == examId)
            .OrderBy(item => item.Order)
            .ToListAsync(cancellationToken);
        var names = await NamesAsync([.. candidates.Select(candidate => candidate.Email), .. FinalizerIds(candidates)], cancellationToken);
        return candidates.Select(candidate => ToDto(candidate, names)).ToList();
    }

    public async Task<(AppLanguage Language, int? SubjectId)?> GetExamSettingsAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var row = await context.ExamCandidates.AsNoTracking()
            .Where(candidate => candidate.Id == candidateId)
            .Select(candidate => new { candidate.Exam.Language, candidate.Exam.SubjectId })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Language.ToLanguage(), row.SubjectId);
    }

    public async Task SaveAiSuggestionAsync(int examCandidateQuestionId, decimal maxScore, GradeSuggestion suggestion, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var grade = await context.QuestionGrades.FirstOrDefaultAsync(item => item.ExamCandidateQuestionId == examCandidateQuestionId, cancellationToken);
        if (grade is null)
        {
            grade = new QuestionGrade { ExamCandidateQuestionId = examCandidateQuestionId };
            context.QuestionGrades.Add(grade);
        }
        grade.MaxScore = maxScore;
        grade.AiScore = suggestion.Score;
        grade.AiCriteriaJson = CriterionScoreDto.ToJson(suggestion.Criteria);
        grade.AiStrengths = JoinLines(suggestion.Strengths);
        grade.AiWeaknesses = JoinLines(suggestion.Weaknesses);
        grade.AiMissingPoints = JoinLines(suggestion.MissingPoints);
        grade.AiSummary = suggestion.Summary;
        grade.AiModel = suggestion.Model.Length > 100 ? suggestion.Model[..100] : suggestion.Model;
        grade.AiGradedAtUtc = nowUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAiPassAsync(int candidateId, GradingStatus status, string? error, CancellationToken cancellationToken = default)
    {
        var attempt = await context.ExamAttempts.FirstOrDefaultAsync(item => item.ExamCandidateId == candidateId, cancellationToken);
        if (attempt is null)
            return;
        attempt.GradingStatus = status;
        attempt.GradingError = error is { Length: > 1000 } ? error[..1000] : error;
        attempt.GradingClaimedAtUtc = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RequeueAsync(int candidateId, CancellationToken cancellationToken = default)
    {
        var attempt = await context.ExamAttempts.FirstOrDefaultAsync(item => item.ExamCandidateId == candidateId, cancellationToken);
        if (attempt is null || attempt.Status != InterviewStatus.Completed)
            return false;
        attempt.GradingStatus = GradingStatus.Pending;
        attempt.GradingError = null;
        attempt.GradingClaimedAtUtc = null;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task SaveLecturerGradesAsync(int candidateId, IReadOnlyList<(int QuestionId, decimal MaxScore, decimal Score, string? Comment)> grades,
        string? comment, string userId, DateTime nowUtc, decimal? finalScore, CancellationToken cancellationToken = default)
    {
        var candidate = await context.ExamCandidates
            .Include(item => item.Attempt)
            .Include(item => item.Questions).ThenInclude(question => question.Grade)
            .AsSplitQuery()
            .FirstOrDefaultAsync(item => item.Id == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException($"Candidate {candidateId} was not found.");
        var attempt = candidate.Attempt ?? throw new InvalidOperationException("The candidate has no interview to grade.");
        foreach (var (questionId, maxScore, score, questionComment) in grades)
        {
            var question = candidate.Questions.FirstOrDefault(item => item.Id == questionId);
            if (question is null)
                continue;
            question.Grade ??= new QuestionGrade { MaxScore = maxScore };
            question.Grade.LecturerScore = score;
            question.Grade.LecturerComment = questionComment;
            question.Grade.UpdatedById = userId;
            question.Grade.UpdatedAtUtc = nowUtc;
        }
        attempt.LecturerComment = comment;
        attempt.FinalScore = finalScore;
        attempt.FinalizedAtUtc = finalScore is null ? null : nowUtc;
        attempt.FinalizedById = finalScore is null ? null : userId;
        await context.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<ExamCandidate> Candidates() => context.ExamCandidates.AsNoTracking()
        .Include(item => item.Exam)
        .Include(item => item.Questions).ThenInclude(question => question.Grade)
        .Include(item => item.Attempt).ThenInclude(attempt => attempt!.Turns).ThenInclude(turn => turn.Recording)
        .AsSplitQuery();

    private static IEnumerable<string> FinalizerIds(IEnumerable<ExamCandidate> candidates) =>
        candidates.Select(candidate => candidate.Attempt?.FinalizedById).OfType<string>().Distinct();

    /// <summary>Display names by normalised email and emails by user id, in one lookup.</summary>
    private async Task<Dictionary<string, string>> NamesAsync(IReadOnlyCollection<string> emailsOrIds, CancellationToken cancellationToken)
    {
        var normalized = emailsOrIds.Select(value => value.ToUpperInvariant()).ToList();
        var users = await context.Users.AsNoTracking()
            .Where(user => (user.NormalizedEmail != null && normalized.Contains(user.NormalizedEmail)) || emailsOrIds.Contains(user.Id))
            .Select(user => new { user.Id, user.NormalizedEmail, user.Email, user.DisplayName })
            .ToListAsync(cancellationToken);
        var names = new Dictionary<string, string>();
        foreach (var user in users)
        {
            if (user.NormalizedEmail is not null)
                names[user.NormalizedEmail] = user.DisplayName;
            names["id:" + user.Id] = user.Email ?? user.Id;
        }
        return names;
    }

    private static CandidateGradingDto ToDto(ExamCandidate candidate, Dictionary<string, string> names)
    {
        var exam = candidate.Exam;
        var attempt = candidate.Attempt;
        var record = attempt is null ? null : ExamRepository.ToRecord(attempt);
        var turns = record?.Turns ?? [];
        var questions = candidate.Questions.OrderBy(question => question.Order).Select(question =>
        {
            var rubric = RubricSnapshot.FromJson(question.RubricJson);
            var grade = question.Grade;
            var questionTurns = turns.Where(turn => turn.MainIndex == question.Order).ToList();
            return new GradingQuestionDto(
                question.Id,
                question.Order,
                question.Content,
                question.ExpectedAnswer,
                question.BloomLevelName,
                rubric,
                grade?.MaxScore is > 0 ? grade.MaxScore : rubric?.TotalPoints ?? GradeMath.DefaultMaxScore,
                grade?.AiScore,
                CriterionScoreDto.FromJson(grade?.AiCriteriaJson),
                SplitLines(grade?.AiStrengths),
                SplitLines(grade?.AiWeaknesses),
                SplitLines(grade?.AiMissingPoints),
                grade?.AiSummary,
                grade?.AiModel,
                grade?.LecturerScore,
                grade?.LecturerComment,
                questionTurns,
                AnswerSignals.Empty,
                question.QuestionId);
        }).ToList();

        return new CandidateGradingDto(
            candidate.Id,
            exam.Id,
            exam.Title,
            exam.SubjectName,
            exam.CreatedById,
            candidate.Order,
            candidate.Email,
            names.GetValueOrDefault(candidate.Email.ToUpperInvariant()),
            null,
            record,
            attempt?.GradingStatus ?? GradingStatus.Pending,
            attempt?.GradingError,
            attempt?.RecordingConsentAtUtc,
            attempt?.FinalizedAtUtc,
            attempt?.FinalizedById is { } id ? names.GetValueOrDefault("id:" + id, id) : null,
            attempt?.FinalScore,
            attempt?.LecturerComment,
            questions,
            AnswerSignals.Empty);
    }

    private static string? JoinLines(IReadOnlyList<string> items) => items.Count == 0 ? null : string.Join("\n", items);

    private static IReadOnlyList<string> SplitLines(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
