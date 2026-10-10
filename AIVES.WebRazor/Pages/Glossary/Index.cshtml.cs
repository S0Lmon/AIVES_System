using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Operations;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Glossary;

public sealed class IndexModel(IGlossaryService glossary, ICatalogService catalog, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? SubjectId
    {
        get; set;
    }
    public IReadOnlyList<AIVES.DTO.SubjectDto> Subjects { get; private set; } = [];
    public IReadOnlyList<AIVES.DTO.GlossaryTermDto> Terms { get; private set; } = [];
    public int SelectedSubjectId
    {
        get; private set;
    }
    public string SubjectName { get; private set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Subjects = await catalog.GetSubjectsAsync(ct);
        var subject = Subjects.FirstOrDefault(x => x.Id == SubjectId) ?? Subjects.FirstOrDefault();
        if (subject is null)
            return;
        SelectedSubjectId = subject.Id;
        SubjectName = subject.Name;
        Terms = await glossary.ListAsync(subject.Id, ct);
    }

    public async Task<IActionResult> OnPostAddAsync(int subjectId, string term, string? spokenForms, CancellationToken ct)
    {
        try
        {
            await glossary.AddAsync(subjectId, term, spokenForms, ct);
            TempData["Success"] = L10n.Format("Saved the term {0}.", term.Trim());
        }
        catch (ArgumentException ex) { TempData["Error"] = L10n.T(ex.Message); }
        catch (Exception ex) { logger.LogError(ex, "Could not save glossary term for subject {SubjectId}", subjectId); TempData["Error"] = L10n.T("The term could not be saved. Please try again."); }
        return RedirectToPage(new
        {
            subjectId
        });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, int subjectId, CancellationToken ct)
    {
        try
        {
            await glossary.DeleteAsync(id, ct);
            TempData["Success"] = L10n.T("The term was removed.");
        }
        catch (Exception ex) { logger.LogError(ex, "Could not delete glossary term {TermId}", id); TempData["Error"] = L10n.T("The term could not be removed. Please try again."); }
        return RedirectToPage(new
        {
            subjectId
        });
    }
}
