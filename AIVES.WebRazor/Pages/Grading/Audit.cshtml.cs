using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using AIVES.BLL.Services.Operations;
using AIVES.BLL.Services.Recordings;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Pages.Exams;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AIVES.WebRazor.Pages.Grading;

public sealed class AuditModel(
    IExamService exams,
    IAuditService audit,
    DisplayTimeZone timeZone) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }
    public string Title { get; private set; } = string.Empty;
    public IReadOnlyList<AuditEntryDto> Entries { get; private set; } = [];
    public DisplayTimeZone TimeZone { get; } = timeZone;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (exam is null)
            return NotFound();
        Title = exam.Title;
        Entries = await audit.QueryAsync(new AuditQuery(ExamId: Id, Take: 1000), cancellationToken);
        return Page();
    }
}
