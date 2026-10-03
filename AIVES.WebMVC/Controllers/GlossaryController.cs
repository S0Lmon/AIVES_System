using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Operations;
using AIVES.DTO.Localization;
using AIVES.WebMVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>
/// Subject terms that speech recognition tends to mishear. They bias the recogniser where the
/// browser supports it, correct transcripts as whole words, and tell the AI examiner and grader
/// which technical terms to expect.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Staff)]
public sealed class GlossaryController(IGlossaryService glossary, ICatalogService catalog, ILogger<GlossaryController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? subjectId, CancellationToken cancellationToken)
    {
        var subjects = await catalog.GetSubjectsAsync(cancellationToken);
        var subject = subjects.FirstOrDefault(item => item.Id == subjectId) ?? subjects.FirstOrDefault();
        return View(new GlossaryViewModel
        {
            SubjectId = subject?.Id ?? 0,
            SubjectName = subject?.Name ?? string.Empty,
            Subjects = subjects.Select(item => new SubjectOption(item.Id, item.Name)).ToList(),
            Terms = subject is null ? [] : await glossary.ListAsync(subject.Id, cancellationToken),
            Message = TempData["GlossaryMessage"] as string,
            Error = TempData["GlossaryError"] as string
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int subjectId, string term, string? spokenForms, CancellationToken cancellationToken)
    {
        try
        {
            await glossary.AddAsync(subjectId, term, spokenForms, cancellationToken);
            TempData["GlossaryMessage"] = L10n.Format("Saved the term {0}.", term.Trim());
        }
        catch (ArgumentException ex)
        {
            TempData["GlossaryError"] = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save glossary term for subject {SubjectId}", subjectId);
            TempData["GlossaryError"] = L10n.T("The term could not be saved. Please try again.");
        }
        return RedirectToAction(nameof(Index), new { subjectId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int subjectId, CancellationToken cancellationToken)
    {
        await glossary.DeleteAsync(id, cancellationToken);
        TempData["GlossaryMessage"] = L10n.T("The term was removed.");
        return RedirectToAction(nameof(Index), new { subjectId });
    }
}
