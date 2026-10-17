using AIVES.BLL.Services;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.BLL.Services.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Questions;

public sealed class DeleteModel(IQuestionService questions, ILiveUpdates live, ILogger<DeleteModel> logger) : PageModel
{
    public QuestionDto Question { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var question = await questions.GetQuestionByIdAsync(id);
        if (question is null)
            return NotFound();
        Question = question;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var question = await questions.GetQuestionByIdAsync(id);
        if (question is null)
        {
            TempData["Error"] = L10n.Format("Question #{0} no longer exists.", id);
            return RedirectToPage("Index");
        }

        try
        {
            await questions.DeleteQuestionAsync(id);
        }
        catch (Exception ex)
        {
            // For example a question already handed to an exam candidate, which the database refuses.
            logger.LogWarning(ex, "Question {QuestionId} could not be deleted", id);
            TempData["Error"] = ex is ArgumentException or InvalidOperationException
                ? L10n.T(ex.Message)
                : L10n.Format("Question #{0} cannot be deleted because it is in use (for example in an exam).", id);
            return RedirectToPage("Index");
        }

        await live.EntityChangedAsync(LiveEntities.Question, LiveActions.Deleted, id, question.Content);
        TempData["Success"] = L10n.Format("Question #{0} was deleted.", id);
        return RedirectToPage("Index");
    }
}
