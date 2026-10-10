using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.AiExamRoom;

public sealed class QuestionGeneratorModel : PageModel
{
    public IActionResult OnGet()
    {
        TempData["Notice"] = L10n.T("The AI exam room is disabled for now. Use the AI panel on the question pages.");
        return Redirect("/Question/Create?slide=aiPanel");
    }
}
