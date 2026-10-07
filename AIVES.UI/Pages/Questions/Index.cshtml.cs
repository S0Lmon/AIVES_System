using AIVES.BLL.Services;
using AIVES.BLL.Services.Catalog;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AIVES.WebRazor.Pages.Questions;

public sealed class IndexModel(IQuestionService questions, IBloomLevelService bloomLevels, ICatalogService catalog) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? BloomLevelId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SubjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ActiveOnly { get; set; }

    public IReadOnlyList<QuestionDto> Questions { get; private set; } = [];
    public int TotalCount { get; private set; }
    public SelectList BloomLevels { get; private set; } = null!;
    public SelectList Subjects { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        BloomLevels = new SelectList((await bloomLevels.GetAllAsync()).OrderBy(level => level.Order),
            nameof(BloomLevelDto.Id), nameof(BloomLevelDto.Name), BloomLevelId);
        Subjects = new SelectList(await catalog.GetSubjectsAsync(cancellationToken),
            nameof(SubjectDto.Id), nameof(SubjectDto.Name), SubjectId);
        await LoadAsync();
    }

    /// <summary>GET ?handler=Rows — the table only, fetched again when SignalR reports a change.</summary>
    public async Task<PartialViewResult> OnGetRowsAsync()
    {
        await LoadAsync();
        return Partial("_QuestionRows", this);
    }

    private async Task LoadAsync()
    {
        var all = (await questions.GetAllQuestionsAsync()).ToList();
        TotalCount = all.Count;

        IEnumerable<QuestionDto> filtered = all;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            filtered = filtered.Where(question => question.Content.Contains(term, StringComparison.OrdinalIgnoreCase)
                || question.ExpectedAnswer.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (BloomLevelId is > 0)
            filtered = filtered.Where(question => question.BloomLevelId == BloomLevelId);
        if (SubjectId is > 0)
            filtered = filtered.Where(question => question.SubjectId == SubjectId);
        if (ActiveOnly)
            filtered = filtered.Where(question => question.IsActive);

        Questions = filtered.OrderBy(question => question.DisplayOrder).ThenByDescending(question => question.ModifiedDate).ToList();
    }
}
