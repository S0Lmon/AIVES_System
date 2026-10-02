using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>
/// The standalone AI exam room is disabled while its flow is being reworked. The working
/// generator lives in the compact panel on the question pages, so any old link is sent there
/// with the panel already open.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Staff)]
public sealed class AiExamRoomController : Controller
{
    [HttpGet]
    public IActionResult QuestionGenerator()
    {
        TempData["Notice"] = L10n.T("The AI exam room is disabled for now. Use the AI panel on the question pages.");
        return RedirectToAction("Create", "Question", new { slide = "aiPanel" });
    }
}