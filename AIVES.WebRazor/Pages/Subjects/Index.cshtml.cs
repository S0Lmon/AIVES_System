using AIVES.BLL.Services.Catalog;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AIVES.WebRazor.Pages.Subjects;

/// <summary>Name and description, shared by the subject and topic forms.</summary>
public sealed class CatalogEntryInput
{
    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(200, ErrorMessage = "The name can be at most 200 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    [Display(Name = "Description")]
    public string? Description
    {
        get; set;
    }
}

public sealed class IndexModel(ICatalogService catalog, ILiveUpdates live) : PageModel
{
    [BindProperty]
    public CatalogEntryInput NewSubject { get; set; } = new();

    public IReadOnlyList<SubjectDto> Subjects { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Subjects = await catalog.GetSubjectsAsync(cancellationToken);

    /// <summary>GET ?handler=Rows — refreshed when SignalR reports a catalogue change.</summary>
    public async Task<PartialViewResult> OnGetRowsAsync(CancellationToken cancellationToken)
    {
        Subjects = await catalog.GetSubjectsAsync(cancellationToken);
        return Partial("_SubjectRows", this);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Subjects = await catalog.GetSubjectsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var created = await catalog.CreateSubjectAsync(new SubjectInput(NewSubject.Name.Trim(), NewSubject.Description?.Trim() ?? string.Empty), cancellationToken);
            await live.EntityChangedAsync(LiveEntities.Subject, LiveActions.Created, created.Id, created.Name);
            TempData["Success"] = L10n.Format("Subject \"{0}\" was created. Now add its topics.", created.Name);
            return RedirectToPage("Details", new
            {
                id = created.Id
            });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, L10n.T(ex.Message));
            Subjects = await catalog.GetSubjectsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var subject = (await catalog.GetSubjectsAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
        if (subject is null)
            return RedirectToPage();

        try
        {
            await catalog.DeleteSubjectAsync(id, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            TempData["Error"] = L10n.T(ex.Message);
            return RedirectToPage();
        }

        await live.EntityChangedAsync(LiveEntities.Subject, LiveActions.Deleted, id, subject.Name);
        TempData["Success"] = L10n.Format("Subject \"{0}\" and its topics were deleted.", subject.Name);
        return RedirectToPage();
    }
}
