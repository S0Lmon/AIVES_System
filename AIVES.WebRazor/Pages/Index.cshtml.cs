using AIVES.BLL.Services;
using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Dashboard;
using AIVES.BLL.Services.Exams;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace AIVES.WebRazor.Pages;

public sealed class IndexModel(
    IQuestionService questions,
    ICatalogService catalog,
    IRubricService rubrics,
    IDashboardService dashboard,
    IExamService exams) : PageModel
{
    public sealed record Stats(int Questions, int ActiveQuestions, int Subjects, int Topics, int Rubrics);

    public bool IsStaff { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsLecturer { get; private set; }
    public Stats? Numbers { get; private set; }
    public AdminDashboardDto? AdminDashboard { get; private set; }
    public LecturerDashboardDto? LecturerDashboard { get; private set; }
    public StudentDashboardDto? StudentDashboard { get; private set; }
    public IReadOnlyList<ExamSummaryDto> UpcomingLecturerExams { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IsStaff = AivesHub.IsStaff(User);
        if (User.Identity?.IsAuthenticated == true)
        {
            IsAdmin = User.IsInRole(AppRoles.Admin);
            IsLecturer = !IsAdmin && User.IsInRole(AppRoles.Lecturer);
            if (IsAdmin)
            {
                AdminDashboard = await dashboard.GetAdminDashboardAsync(cancellationToken);
            }
            else if (IsLecturer)
            {
                var actor = new ExamActor(
                    User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                    false,
                    User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name);
                LecturerDashboard = await dashboard.GetLecturerDashboardAsync(actor, cancellationToken);
                var now = DateTime.UtcNow;
                UpcomingLecturerExams = (await exams.ListAsync(actor, cancellationToken))
                    .Where(exam => exam.StartsAtUtc > now && exam.StartsAtUtc <= now.AddDays(7))
                    .OrderBy(exam => exam.StartsAtUtc)
                    .ToList();
            }
            else
            {
                var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;
                StudentDashboard = await dashboard.GetStudentDashboardAsync(email, cancellationToken);
            }
        }

        if (IsStaff)
            Numbers = await LoadStatsAsync(cancellationToken);
    }

    /// <summary>GET ?handler=Stats — the dashboard re-reads its counters after a live change.</summary>
    public async Task<IActionResult> OnGetStatsAsync(CancellationToken cancellationToken) =>
        AivesHub.IsStaff(User) ? new JsonResult(await LoadStatsAsync(cancellationToken)) : Forbid();

    public IActionResult OnPostSetLanguage(AppLanguage language, string? returnUrl)
    {
        Response.Cookies.Append(RazorPresentation.LanguageCookieName, language.ToCultureCode(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            HttpOnly = false,
            SameSite = SameSiteMode.Lax
        });

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Page("/Index")!);
    }

    private async Task<Stats> LoadStatsAsync(CancellationToken cancellationToken)
    {
        var all = (await questions.GetAllQuestionsAsync()).ToList();
        var subjects = await catalog.GetSubjectsAsync(cancellationToken);
        return new Stats(all.Count, all.Count(question => question.IsActive), subjects.Count,
            subjects.Sum(subject => subject.TopicCount), (await rubrics.GetAllRubricsAsync()).Count());
    }
}
