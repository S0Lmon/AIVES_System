using AIVES.BLL.Services;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Questions;

public sealed class DetailsModel(IQuestionService questions) : PageModel
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
}
