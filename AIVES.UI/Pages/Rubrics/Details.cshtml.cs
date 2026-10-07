using AIVES.BLL.Services;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Rubrics;

public sealed class DetailsModel(IRubricService rubrics) : PageModel
{
    public RubricDto Rubric { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var rubric = await rubrics.GetRubricByIdAsync(id);
        if (rubric is null)
            return NotFound();
        Rubric = rubric;
        return Page();
    }

    /// <summary>The cell for a row and column; cells are matched by level id, then by name.</summary>
    public static RubricCriterionLevelDto? Cell(RubricCriterionDto criterion, RubricLevelDto level) =>
        criterion.Levels.FirstOrDefault(cell => cell.RubricLevelId == level.Id && level.Id != 0)
        ?? criterion.Levels.FirstOrDefault(cell => string.Equals(cell.LevelName, level.Name, StringComparison.OrdinalIgnoreCase));
}
