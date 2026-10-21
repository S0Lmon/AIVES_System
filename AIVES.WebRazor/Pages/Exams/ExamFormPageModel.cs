using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services;
using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Operations;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace AIVES.WebRazor.Pages.Exams;

public static class ExamPageActors
{
    public static ExamActor From(ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        user.IsInRole(AppRoles.Admin),
        user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name);
}

public sealed class ExamInputModel
{
    public int? Id
    {
        get; set;
    }
    [Required, StringLength(ExamLimits.TitleMaxLength, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;
    [Range(1, int.MaxValue)]
    public int SubjectId
    {
        get; set;
    }
    public int? TopicId
    {
        get; set;
    }
    public int? RubricId
    {
        get; set;
    }
    public List<int> SelectedQuestionIds { get; set; } = [];
    [Required]
    public DateTime? StartsAtLocal
    {
        get; set;
    }
    [Range(ExamLimits.MinSlotMinutes, ExamLimits.MaxSlotMinutes)]
    public int SlotMinutes { get; set; } = 15;
    [Range(1, ExamLimits.MaxMainQuestions)]
    public int MainQuestionCount { get; set; } = 3;
    [Range(0, ExamLimits.MaxFollowUpQuestions)]
    public int MaxFollowUpQuestions { get; set; } = 2;
    [Range(0, ExamLimits.MaxFollowUpsPerQuestionLimit)]
    public int MaxFollowUpsPerQuestion { get; set; } = ExamLimits.DefaultFollowUpsPerQuestion;
    [Range(ExamLimits.MinAnswerSeconds, ExamLimits.MaxAnswerSeconds)]
    public int AnswerTimeLimitSeconds { get; set; } = ExamLimits.DefaultAnswerSeconds;
    public AppLanguage Language { get; set; } = AppLanguage.Vi;
    public RecordingMode Recording { get; set; } = RecordingMode.Audio;
    public DateTime? EndsAtLocal
    {
        get; set;
    }
    [Range(0, ExamLimits.MaxBufferMinutes)]
    public int BufferMinutes
    {
        get; set;
    }
    [Range(0, ExamLimits.MaxBreakMinutes)]
    public int BreakMinutes
    {
        get; set;
    }
    [Range(0, ExamLimits.MaxBreakEveryCount)]
    public int BreakEveryCount
    {
        get; set;
    }
    public QuestionSelectionStrategy Strategy { get; set; } = QuestionSelectionStrategy.Balanced;
    [StringLength(ExamLimits.TermMaxLength)]
    public string? Term
    {
        get; set;
    }
    [StringLength(ExamLimits.ExamTypeMaxLength)]
    public string? ExamType
    {
        get; set;
    }
    [StringLength(ExamLimits.InstructionsMaxLength)]
    public string? Instructions
    {
        get; set;
    }
    public bool ScheduleOverflowAllowed
    {
        get; set;
    }
    public string CandidateEmails { get; set; } = string.Empty;
}

public sealed record ExamSubjectOption(int Id, string Name);
public sealed record ExamTopicOption(int Id, int SubjectId, string Name);

public abstract class ExamFormPageModel(
    IExamService exams,
    ICatalogService catalog,
    ISubjectAccessService subjectAccess,
    IRubricService rubrics,
    ISystemSettingsService settings,
    DisplayTimeZone timeZone) : PageModel
{
    [BindProperty]
    public ExamInputModel Input { get; set; } = new();
    public IReadOnlyList<ExamSubjectOption> Subjects { get; private set; } = [];
    public IReadOnlyList<ExamTopicOption> Topics { get; private set; } = [];
    public IReadOnlyList<RubricDto> Rubrics { get; private set; } = [];
    public IReadOnlyList<AppLanguage> Languages { get; private set; } = [];
    public IReadOnlyList<UserSummaryDto> Students { get; protected set; } = [];
    public string TimeZoneLabel => timeZone.OffsetLabel;
    protected IExamService Exams => exams;
    protected ExamActor Actor => ExamPageActors.From(User);
    protected ISystemSettingsService Settings => settings;
    protected DisplayTimeZone TimeZone => timeZone;

    protected async Task FillAsync(CancellationToken cancellationToken)
    {
        var visibleSubjects = await subjectAccess.FilterAsync(
            await catalog.GetSubjectsAsync(cancellationToken), subject => subject.Id, Actor, cancellationToken);
        Subjects = visibleSubjects.Select(subject => new ExamSubjectOption(subject.Id, subject.Name)).ToList();
        Topics = (await catalog.GetTopicsAsync(cancellationToken: cancellationToken))
            .Select(topic => new ExamTopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList();
        Rubrics = (await rubrics.GetAllRubricsAsync()).OrderBy(rubric => rubric.Name).ToList();
        var speech = await settings.GetSpeechAsync(cancellationToken);
        Languages = speech.EnabledLanguages.Contains(Input.Language)
            ? speech.EnabledLanguages
            : [.. speech.EnabledLanguages, Input.Language];
    }

    protected virtual Task LoadStudentsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected ExamInput ToInput() => new(
        Input.Title,
        Input.SubjectId,
        Input.TopicId,
        timeZone.ToUtc(Input.StartsAtLocal!.Value),
        Input.SlotMinutes,
        Input.MainQuestionCount,
        Input.MaxFollowUpQuestions,
        exams.ParseCandidateEmails(Input.CandidateEmails),
        Input.AnswerTimeLimitSeconds,
        Input.MaxFollowUpsPerQuestion,
        Input.Language,
        Input.Recording,
        Input.EndsAtLocal is { } end ? timeZone.ToUtc(end) : null,
        Input.BufferMinutes,
        Input.BreakMinutes,
        Input.BreakEveryCount,
        Input.Strategy,
        Input.Term,
        Input.ExamType,
        Input.Instructions,
        Input.ScheduleOverflowAllowed,
        Input.RubricId,
        Input.SelectedQuestionIds);

    protected async Task<IActionResult> QuestionBankAsync(
        int? subjectId, int? topicId, int? rubricId, CancellationToken cancellationToken)
    {
        if (subjectId is not int selectedSubjectId)
            return new JsonResult(Array.Empty<object>());
        try
        {
            var questions = await Exams.GetQuestionBankAsync(selectedSubjectId, topicId, rubricId, Actor, cancellationToken);
            return new JsonResult(questions.Select(question => new
            {
                question.Id,
                question.Content,
                question.BloomLevelName
            }));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    protected void SetResult(ExamSaveResult result, string message, ILogger logger)
    {
        TempData["ExamMessage"] = message;
        if (result.ConsecutiveOverlaps > 0)
        {
            logger.LogInformation("Exam {ExamId} has {Overlaps} questions shared by consecutive candidates", result.ExamId, result.ConsecutiveOverlaps);
            TempData["ExamWarning"] = L10n.Format(
                "The question pool is small: consecutive candidates share {0} question(s). Add questions to the subject or lower the number of main questions, then draw again.",
                result.ConsecutiveOverlaps);
        }
    }

    protected static ExamInputModel FromDetails(ExamDetailsDto exam, DisplayTimeZone timeZone) => new()
    {
        Id = exam.Id,
        Title = exam.Title,
        SubjectId = exam.SubjectId ?? 0,
        TopicId = exam.TopicId,
        RubricId = exam.RubricId,
        SelectedQuestionIds = exam.SelectedQuestionIds?.ToList() ?? [],
        StartsAtLocal = timeZone.ToLocal(exam.StartsAtUtc),
        EndsAtLocal = timeZone.ToLocal(exam.EndsAtUtc),
        SlotMinutes = exam.SlotMinutes,
        MainQuestionCount = exam.MainQuestionCount,
        MaxFollowUpQuestions = exam.MaxFollowUpQuestions,
        MaxFollowUpsPerQuestion = exam.MaxFollowUpsPerQuestion,
        AnswerTimeLimitSeconds = exam.AnswerTimeLimitSeconds,
        Language = exam.Language,
        Recording = exam.Recording,
        BufferMinutes = exam.BufferMinutes,
        BreakMinutes = exam.BreakMinutes,
        BreakEveryCount = exam.BreakEveryCount,
        Strategy = exam.Strategy,
        Term = exam.Term,
        ExamType = exam.ExamType,
        Instructions = exam.Instructions,
        ScheduleOverflowAllowed = exam.ScheduleEndsAtUtc > exam.EndsAtUtc,
        CandidateEmails = string.Join(Environment.NewLine, exam.Candidates.Select(candidate => candidate.Email))
    };
}
