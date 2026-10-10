using AIVES.BLL.Services;
using AIVES.BLL.Services.Catalog;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Questions;

public sealed class EditModel(IQuestionService questions, IBloomLevelService bloomLevels, IRubricService rubrics,
    ICatalogService catalog, ILiveUpdates live, ILogger<EditModel> logger) : PageModel, IQuestionFormPage
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }

    [BindProperty]
    public QuestionInput Input { get; set; } = new();

    public QuestionFormOptions Options { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var question = await questions.GetQuestionByIdAsync(Id);
        if (question is null)
            return NotFound();

        Input = QuestionInput.From(question);
        Options = await QuestionFormOptions.LoadAsync(bloomLevels, rubrics, catalog, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var updated = await questions.UpdateQuestionAsync(Input.ToDto(Id));
                await live.EntityChangedAsync(LiveEntities.Question, LiveActions.Updated, updated.Id, updated.Content);
                TempData["Success"] = $"Đã cập nhật câu hỏi #{updated.Id}.";
                return RedirectToPage("Index");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                if (await questions.GetQuestionByIdAsync(Id) is null)
                {
                    // Someone deleted it while this form was open.
                    TempData["Error"] = $"Câu hỏi #{Id} đã bị xoá nên không thể lưu.";
                    return RedirectToPage("Index");
                }
                logger.LogWarning(ex, "Question update was rejected");
                ModelState.AddModelError(string.Empty, L10n.T(ex.Message));
            }
        }

        Options = await QuestionFormOptions.LoadAsync(bloomLevels, rubrics, catalog, cancellationToken);
        return Page();
    }
}
