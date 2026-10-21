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

public sealed class CreateModel(
    IExamService exams, ICatalogService catalog, ISubjectAccessService subjectAccess, IRubricService rubrics,
    ISystemSettingsService settings, DisplayTimeZone timeZone, ILogger<CreateModel> logger,
    ILiveUpdates liveUpdates, IUserAdminService users)
    : ExamFormPageModel(exams, catalog, subjectAccess, rubrics, settings, timeZone)
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

    public Task<IActionResult> OnGetQuestionBankAsync(int? subjectId, int? topicId, int? rubricId, CancellationToken cancellationToken) =>
        QuestionBankAsync(subjectId, topicId, rubricId, cancellationToken);

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
            await LoadStudentsAsync(cancellationToken);
            return Page();
        }
        try
        {
            var result = await Exams.CreateAsync(ToInput(), Actor, cancellationToken);
            SetResult(result, L10n.T("The exam was created and questions were assigned to every candidate."), logger);
            var created = await Exams.GetAsync(result.ExamId, Actor, cancellationToken);
            await liveUpdates.ExamChangedAsync(LiveActions.Created, result.ExamId, Input.Title, ExamLive.Emails(created));
            return RedirectToPage("/Exams/Details", new
            {
                id = result.ExamId
            });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await FillAsync(cancellationToken);
            await LoadStudentsAsync(cancellationToken);
            return Page();
        }
    }
}
