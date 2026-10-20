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
