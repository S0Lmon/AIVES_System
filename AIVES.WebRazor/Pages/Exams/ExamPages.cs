using AIVES.BLL.Services.Accounts;
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
    ISystemSettingsService settings,
    DisplayTimeZone timeZone) : PageModel
{
    [BindProperty]
    public ExamInputModel Input { get; set; } = new();
    public IReadOnlyList<ExamSubjectOption> Subjects { get; private set; } = [];
    public IReadOnlyList<ExamTopicOption> Topics { get; private set; } = [];
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
        Input.ScheduleOverflowAllowed);

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

public sealed class IndexModel(IExamService exams, DisplayTimeZone timeZone, TimeProvider clock) : PageModel
{
    public IReadOnlyList<ExamSummaryDto> Exams { get; private set; } = [];
    public IReadOnlyList<ExamSummaryDto> MonthExams { get; private set; } = [];
    public IReadOnlyList<ExamSummaryDto> Upcoming { get; private set; } = [];
    public DateTime TargetMonth { get; private set; }
    public DateTime TodayLocal { get; private set; }
    public bool IsStudent { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? year, int? month, CancellationToken cancellationToken)
    {
        try
        {
            TargetMonth = new DateTime(year ?? timeZone.ToLocal(clock.GetUtcNow().UtcDateTime).Year,
                month ?? timeZone.ToLocal(clock.GetUtcNow().UtcDateTime).Month, 1);
        }
        catch (ArgumentOutOfRangeException) { return BadRequest(); }
        var now = clock.GetUtcNow().UtcDateTime;
        TodayLocal = timeZone.ToLocal(now).Date;
        IsStudent = User.IsInRole(AppRoles.Student) && !User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Lecturer);
        if (IsStudent)
        {
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;
            var studentExams = (await exams.ListForCandidateAsync(email, cancellationToken)).OrderBy(exam => exam.StartsAtUtc).ToList();
            var all = studentExams.Select(e => new ExamSummaryDto(e.ExamId, e.Title, e.SubjectName, e.TopicName, e.StartsAtUtc, e.EndsAtUtc, (int)(e.EndsAtUtc - e.StartsAtUtc).TotalMinutes, e.CandidateCount, string.Empty)).ToList();
            Exams = all;
            MonthExams = all.Where(exam =>
            {
                var local = timeZone.ToLocal(exam.StartsAtUtc);
                return local.Year == TargetMonth.Year && local.Month == TargetMonth.Month;
            }).ToList();
            Upcoming = all.Where(exam => exam.StartsAtUtc > now && exam.StartsAtUtc <= now.AddDays(7))
                .OrderBy(exam => exam.StartsAtUtc).ToList();
            return Page();
        }
        var actor = ExamPageActors.From(User);
        var allExams = await exams.ListAsync(actor, cancellationToken);
        Exams = allExams;
        MonthExams = allExams.Where(exam =>
        {
            var local = timeZone.ToLocal(exam.StartsAtUtc);
            return local.Year == TargetMonth.Year && local.Month == TargetMonth.Month;
        }).ToList();
        Upcoming = allExams.Where(exam => exam.StartsAtUtc > now && exam.StartsAtUtc <= now.AddDays(7))
            .OrderBy(exam => exam.StartsAtUtc).ToList();
        return Page();
    }
}

public sealed class CreateModel(
    IExamService exams, ICatalogService catalog, ISubjectAccessService subjectAccess,
    ISystemSettingsService settings, DisplayTimeZone timeZone, ILogger<CreateModel> logger,
    ILiveUpdates liveUpdates, IUserAdminService users)
    : ExamFormPageModel(exams, catalog, subjectAccess, settings, timeZone)
{
    public async Task OnGetAsync(DateTime? startsAt, CancellationToken cancellationToken)
    {
        var localNow = TimeZone.ToLocal(DateTime.UtcNow);
        var start = startsAt ?? localNow.Date.AddHours(localNow.Hour + 1);
        Input.StartsAtLocal = start;
        Input.EndsAtLocal = start.AddHours(4);
        Input.Language = (await Settings.GetSpeechAsync(cancellationToken)).DefaultLanguage;
        await FillAsync(cancellationToken);
        await LoadStudentsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetStudentsAsync(string? q, CancellationToken cancellationToken)
    {
        var all = await users.ListUsersAsync();
        var students = all.Where(u => u.Roles.Contains(AppRoles.Student) && !u.IsDisabled).ToList();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var query = q.Trim().ToLowerInvariant();
            students = students.Where(u =>
                u.Email.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (u.DisplayName is not null && u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        return new JsonResult(students.Select(u => new { id = u.Email, text = $"{u.DisplayName} <{u.Email}>" }));
    }

    protected override async Task LoadStudentsAsync(CancellationToken cancellationToken)
    {
        var all = await users.ListUsersAsync();
        Students = all.Where(u => u.Roles.Contains(AppRoles.Student) && !u.IsDisabled).ToList();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await FillAsync(cancellationToken);
            return Page();
        }
        try
        {
            var result = await Exams.CreateAsync(ToInput(), Actor, cancellationToken);
            SetResult(result, L10n.T("The exam was created and questions were assigned to every candidate."), logger);
            await liveUpdates.EntityChangedAsync(LiveEntities.Exam, LiveActions.Created, result.ExamId, Input.Title);
            return RedirectToPage("Details", new
            {
                id = result.ExamId
            });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await FillAsync(cancellationToken);
            return Page();
        }
    }
}

public sealed class EditModel(
    IExamService exams, ICatalogService catalog, ISubjectAccessService subjectAccess,
    ISystemSettingsService settings, DisplayTimeZone timeZone, ILogger<EditModel> logger,
    ILiveUpdates liveUpdates, IUserAdminService users)
    : ExamFormPageModel(exams, catalog, subjectAccess, settings, timeZone)
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var exam = await Exams.GetAsync(Id, Actor, cancellationToken);
        if (exam is null)
            return NotFound();
        if (exam.HasStarted(DateTime.UtcNow))
        {
            TempData["ExamError"] = L10n.T("This exam has already started, so its settings, candidates and questions are locked.");
            return RedirectToPage("Details", new
            {
                id = Id
            });
        }
        Input = FromDetails(exam, TimeZone);
        await FillAsync(cancellationToken);
        await LoadStudentsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetStudentsAsync(string? q, CancellationToken cancellationToken)
    {
        var all = await users.ListUsersAsync();
        var students = all.Where(u => u.Roles.Contains(AppRoles.Student) && !u.IsDisabled).ToList();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var query = q.Trim().ToLowerInvariant();
            students = students.Where(u =>
                u.Email.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (u.DisplayName is not null && u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        return new JsonResult(students.Select(u => new { id = u.Email, text = $"{u.DisplayName} <{u.Email}>" }));
    }

    protected override async Task LoadStudentsAsync(CancellationToken cancellationToken)
    {
        var all = await users.ListUsersAsync();
        Students = all.Where(u => u.Roles.Contains(AppRoles.Student) && !u.IsDisabled).ToList();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Input.Id = Id;
        if (!ModelState.IsValid)
        {
            await FillAsync(cancellationToken);
            return Page();
        }
        try
        {
            var result = await Exams.UpdateAsync(Id, ToInput(), Actor, cancellationToken);
            SetResult(result, L10n.T("The exam was saved and questions were assigned again."), logger);
            var exam = await Exams.GetAsync(Id, Actor, cancellationToken);
            await liveUpdates.EntityChangedAsync(LiveEntities.Exam, LiveActions.Updated, Id, exam?.Title ?? Input.Title);
            return RedirectToPage("Details", new
            {
                id = Id
            });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await FillAsync(cancellationToken);
            return Page();
        }
    }
}

public sealed class DetailsModel(IExamService exams, DisplayTimeZone timeZone, TimeProvider clock, ILiveUpdates liveUpdates) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }
    public ExamDetailsDto Exam { get; private set; } = null!;
    public DateTime NowUtc
    {
        get; private set;
    }
    public bool Started => Exam.HasStarted(NowUtc);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostReassignAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await exams.ReassignQuestionsAsync(Id, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.T("New questions were drawn for every candidate.");
            if (result.ConsecutiveOverlaps > 0)
                TempData["ExamWarning"] = L10n.Format("The question pool is small: consecutive candidates share {0} question(s). Add questions to the subject or lower the number of main questions, then draw again.", result.ConsecutiveOverlaps);
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.EntityChangedAsync(LiveEntities.Exam, LiveActions.Updated, Id, exam?.Title ?? string.Empty);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnPostResetScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await exams.ResetScheduleAsync(Id, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.T("The schedule was regenerated from the exam settings.");
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.EntityChangedAsync(LiveEntities.Exam, LiveActions.Updated, Id, exam?.Title ?? string.Empty);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            var title = exam?.Title ?? string.Empty;
            await exams.DeleteAsync(Id, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.T("The exam was deleted.");
            await liveUpdates.EntityChangedAsync(LiveEntities.Exam, LiveActions.Deleted, Id, title);
            return RedirectToPage("./Index");
        }
        catch (KeyNotFoundException) { return RedirectToPage("./Index"); }
        catch (InvalidOperationException ex) { TempData["ExamError"] = ex.Message; return RedirectToPage(new { id = Id }); }
    }

    public async Task<IActionResult> OnPostAdjustSlotAsync(int order, DateTime slotStartLocal, CancellationToken cancellationToken)
    {
        try
        {
            await exams.AdjustSlotAsync(Id, order, timeZone.ToUtc(slotStartLocal), ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.Format("Candidate #{0} was moved to a new time.", order);
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.EntityChangedAsync(LiveEntities.Exam, LiveActions.Updated, Id, exam?.Title ?? string.Empty);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnPostCandidateStatusAsync(int order, string? status, CancellationToken cancellationToken)
    {
        CandidateStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<CandidateStatus>(status, out var value))
            {
                TempData["ExamError"] = L10n.T("That status does not exist.");
                return RedirectToPage(new
                {
                    id = Id
                });
            }
            parsed = value;
        }
        try
        {
            await exams.SetCandidateStatusAsync(Id, order, parsed, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = parsed is null ? L10n.Format("Candidate #{0} follows the schedule again.", order) : L10n.Format("Candidate #{0} was marked {1}.", order, ViewText.Status(parsed.Value));
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.EntityChangedAsync(LiveEntities.Exam, LiveActions.Updated, Id, exam?.Title ?? string.Empty);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        NowUtc = clock.GetUtcNow().UtcDateTime;
        var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (exam is null)
            return false;
        Exam = exam;
        return true;
    }
}
