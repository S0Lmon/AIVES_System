using System.Security.Claims;
using AIVES.BLL.Services.Exams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>The signed-in user's own exam slots. Shows times only, never the assigned questions.</summary>
[Authorize]
public sealed class MyExamsController(IExamService exams) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;
        return View(await exams.ListForCandidateAsync(email, cancellationToken));
    }
}
