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

public sealed class ExamModel(IGradingService grading) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }
    public ExamGradingDto Exam { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var exam = await grading.GetExamAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (exam is null)
            return NotFound();
        Exam = exam;
        return Page();
    }
}
