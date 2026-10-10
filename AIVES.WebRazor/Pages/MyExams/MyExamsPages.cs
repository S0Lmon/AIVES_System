using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace AIVES.WebRazor.Pages.MyExams;

public sealed class IndexModel(IExamService exams, TimeProvider clock) : PageModel
{
    public IReadOnlyList<StudentExamDto> Exams { get; private set; } = [];
    public DateTime NowUtc
    {
        get; private set;
    }
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NowUtc = clock.GetUtcNow().UtcDateTime;
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;
        Exams = (await exams.ListForCandidateAsync(email, cancellationToken)).OrderBy(exam => exam.StartsAtUtc).ToList();
    }
}

public sealed class ResultModel(IGradingService grading) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }
    public StudentResultDto Result { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;
        var result = await grading.GetStudentResultAsync(Id, email, cancellationToken);
        if (result is null)
            return NotFound();
        Result = result;
        return Page();
    }
}
