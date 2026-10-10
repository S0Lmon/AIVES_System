using AIVES.BLL.Services.Exams;
using AIVES.DTO;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Interviews;

/// <summary>
/// Lecturers watch an exam's vivas live: each question asked, the candidate's words as they speak,
/// the AI's follow-ups and completion. Without an id it lists the exams the lecturer may watch.
/// </summary>
public sealed class MonitorModel(IExamService exams) : PageModel
{
    public IReadOnlyList<ExamSummaryDto> Exams { get; private set; } = [];
    public ExamDetailsDto? Exam
    {
        get; private set;
    }

    public async Task<IActionResult> OnGetAsync(int? examId, CancellationToken cancellationToken)
    {
        var actor = new ExamActor(AivesHub.UserId(User), User.IsInRole(AppRoles.Admin), User.Identity?.Name);
        if (examId is null)
        {
            Exams = (await exams.ListAsync(actor, cancellationToken)).OrderByDescending(exam => exam.StartsAtUtc).ToList();
            return Page();
        }

        Exam = await exams.GetAsync(examId.Value, actor, cancellationToken);
        return Exam is null ? NotFound() : Page();
    }
}
