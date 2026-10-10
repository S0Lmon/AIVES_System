using AIVES.BLL.Services.Exams;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace AIVES.WebRazor.Pages.Interviews;

/// <summary>The signed-in user's own viva slots. Times only, never the assigned questions.</summary>
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

    public bool CanEnter(StudentExamDto exam) =>
        exam.InterviewStatus == InterviewStatus.InProgress
        || (exam.InterviewStatus is null or InterviewStatus.NotStarted && NowUtc >= exam.StartsAtUtc && NowUtc < exam.EndsAtUtc);
}
