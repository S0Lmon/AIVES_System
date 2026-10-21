using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using AIVES.DTO;
using AIVES.WebRazor.Pages.Exams;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Reports;

public sealed class ExamModel(IGradingService grading) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }
    public ExamReportDto Report { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var report = await grading.GetReportAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (report is null)
            return NotFound();
        Report = report;
        return Page();
    }
}
