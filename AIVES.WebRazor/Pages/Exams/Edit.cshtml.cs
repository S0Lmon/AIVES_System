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
            return RedirectToPage("/Exams/Details", new
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
            await LoadStudentsAsync(cancellationToken);
            return Page();
        }
        try
        {
            // Candidates removed by this edit must hear about it too.
            var before = await Exams.GetAsync(Id, Actor, cancellationToken);
            var result = await Exams.UpdateAsync(Id, ToInput(), Actor, cancellationToken);
            SetResult(result, L10n.T("The exam was saved and questions were assigned again."), logger);
            var exam = await Exams.GetAsync(Id, Actor, cancellationToken);
            await liveUpdates.ExamChangedAsync(LiveActions.Updated, Id, exam?.Title ?? Input.Title, ExamLive.Emails(before).Concat(ExamLive.Emails(exam)));
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
            await LoadStudentsAsync(cancellationToken);
            return Page();
        }
    }
}
