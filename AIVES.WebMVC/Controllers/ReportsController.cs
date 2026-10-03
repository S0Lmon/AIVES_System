using AIVES.BLL.Services.Exams;
using AIVES.WebMVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>Entry point to the class reports and grade sheets; the reports themselves live on <see cref="GradingController"/>.</summary>
[Authorize(Policy = AuthorizationPolicies.Staff)]
public sealed class ReportsController(IExamService exams) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var actor = User.ToExamActor();
        ViewData["Mode"] = "reports";
        return View("~/Views/Grading/Index.cshtml", new ExamIndexViewModel { Exams = await exams.ListAsync(actor, cancellationToken), ShowOwner = actor.IsAdmin });
    }
}
