using System.Security.Claims;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>
/// The signed-in user's own exam slots. Shows times only, never the assigned questions, until the
/// lecturer confirms the grade; then the student can read their result report.
/// </summary>
[Authorize]
public sealed class MyExamsController(IExamService exams, IGradingService grading) : Controller
{
    private string Email => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await exams.ListForCandidateAsync(Email, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Result(int id, CancellationToken cancellationToken)
    {
        var result = await grading.GetStudentResultAsync(id, Email, cancellationToken);
        return result is null ? NotFound() : View(result);
    }
}
