using AIVES.BLL.Services;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Rubrics;

public sealed class IndexModel(IRubricService rubrics) : PageModel
{
    public IReadOnlyList<RubricDto> Rubrics { get; private set; } = [];

    public async Task OnGetAsync() =>
        Rubrics = (await rubrics.GetAllRubricsAsync()).OrderBy(rubric => rubric.Name).ToList();
}
