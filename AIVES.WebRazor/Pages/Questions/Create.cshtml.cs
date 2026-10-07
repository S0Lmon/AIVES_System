using AIVES.BLL.Services;
using AIVES.BLL.Services.Catalog;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Questions;

public sealed class CreateModel(IQuestionService questions, IBloomLevelService bloomLevels, IRubricService rubrics,
    ICatalogService catalog, ILiveUpdates live, ILogger<CreateModel> logger) : PageModel, IQuestionFormPage
{
    [BindProperty]
    public QuestionInput Input { get; set; } = new();

    public QuestionFormOptions Options { get; private set; } = null!;

    public async Task OnGetAsync(int? subjectId, int? topicId, CancellationToken cancellationToken)
    {
        Input.SubjectId = subjectId;
        Input.TopicId = topicId;
        Options = await QuestionFormOptions.LoadAsync(bloomLevels, rubrics, catalog, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var created = await questions.CreateQuestionAsync(Input.ToDto());
                await live.EntityChangedAsync(LiveEntities.Question, LiveActions.Created, created.Id, created.Content);
                TempData["Success"] = $"Đã tạo câu hỏi #{created.Id}.";
                return RedirectToPage("Index");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Question was rejected");
                ModelState.AddModelError(string.Empty, L10n.T(ex.Message));
            }
        }

        Options = await QuestionFormOptions.LoadAsync(bloomLevels, rubrics, catalog, cancellationToken);
        return Page();
    }
}
