using AIVES.BLL.Services;
using AIVES.BLL.Services.Catalog;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages;

public sealed class IndexModel(IQuestionService questions, ICatalogService catalog, IRubricService rubrics) : PageModel
{
    public sealed record Stats(int Questions, int ActiveQuestions, int Subjects, int Topics, int Rubrics);

    public bool IsStaff { get; private set; }
    public Stats? Numbers { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IsStaff = AivesHub.IsStaff(User);
        if (IsStaff)
            Numbers = await LoadStatsAsync(cancellationToken);
    }

    /// <summary>GET ?handler=Stats — the dashboard re-reads its counters after a live change.</summary>
    public async Task<IActionResult> OnGetStatsAsync(CancellationToken cancellationToken) =>
        AivesHub.IsStaff(User) ? new JsonResult(await LoadStatsAsync(cancellationToken)) : Forbid();

    private async Task<Stats> LoadStatsAsync(CancellationToken cancellationToken)
    {
        var all = (await questions.GetAllQuestionsAsync()).ToList();
        var subjects = await catalog.GetSubjectsAsync(cancellationToken);
        return new Stats(all.Count, all.Count(question => question.IsActive), subjects.Count,
            subjects.Sum(subject => subject.TopicCount), (await rubrics.GetAllRubricsAsync()).Count());
    }
}
