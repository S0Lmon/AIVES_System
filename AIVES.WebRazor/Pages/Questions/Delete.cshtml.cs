using AIVES.BLL.Services;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
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
            TempData["Error"] = $"Câu hỏi #{id} không còn tồn tại.";
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
                : $"Không thể xoá câu hỏi #{id} vì nó đang được dùng (ví dụ trong một kỳ thi).";
            return RedirectToPage("Index");
        }

        await live.EntityChangedAsync(LiveEntities.Question, LiveActions.Deleted, id, question.Content);
        TempData["Success"] = $"Đã xoá câu hỏi #{id}.";
        return RedirectToPage("Index");
    }
}
