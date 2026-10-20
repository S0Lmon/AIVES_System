using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using AIVES.DTO;
using AIVES.WebRazor.Pages.Exams;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Reports;

public sealed class IndexModel(IExamService exams) : PageModel
{
    public IReadOnlyList<ExamSummaryDto> Exams { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Exams = await exams.ListAsync(ExamPageActors.From(User), cancellationToken);
}
