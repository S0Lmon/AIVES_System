using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Import;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Catalog;

public sealed class IndexModel(ICatalogService catalog, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Tab
    {
        get; set;
    }
    [BindProperty(SupportsGet = true)]
    public string? SubjectFilter
    {
        get; set;
    }
    [BindProperty(SupportsGet = true)]
    public string? MaterialFilter
    {
        get; set;
    }
    [BindProperty(SupportsGet = true)]
    public int? SubjectId
    {
        get; set;
    }
    [BindProperty(SupportsGet = true)]
    public int? TopicId
    {
        get; set;
    }
    [BindProperty] public CatalogForm Subject { get; set; } = new();
    [BindProperty] public CatalogForm Topic { get; set; } = new();
    [BindProperty] public MaterialForm Material { get; set; } = new();
    public IReadOnlyList<SubjectDto> Subjects { get; private set; } = [];
    public IReadOnlyList<TopicDto> Topics { get; private set; } = [];
    public IReadOnlyList<MaterialDto> Materials { get; private set; } = [];
    public string ActiveTab => string.Equals(Tab, "material", StringComparison.OrdinalIgnoreCase) ? "material" : "subject";
    private const long MaxUploadBytes = 20 * 1024 * 1024;

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);
    public async Task<IActionResult> OnPostCreateSubjectAsync(CancellationToken ct)
    {
        try
        {
            var x = await catalog.CreateSubjectAsync(new SubjectInput(Subject.Name.Trim(), Subject.Description?.Trim() ?? ""), ct);
            TempData["Success"] = L10n.T("The subject was created.");
            return RedirectToPage(new
            {
                tab = "subject",
                subjectId = x.Id
            });
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { TempData["Error"] = L10n.T(ex.Message); return RedirectToPage(new { tab = "subject" }); }
    }
    public async Task<IActionResult> OnPostUpdateSubjectAsync(int id, [Bind(Prefix = "Subject")] CatalogForm input, CancellationToken ct)
    {
        try
        {
            await catalog.UpdateSubjectAsync(id, new SubjectInput(input.Name.Trim(), input.Description?.Trim() ?? ""), ct);
            TempData["Success"] = L10n.T("The subject was updated.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { TempData["Error"] = L10n.T(ex.Message); }
        return RedirectToPage(new
        {
            tab = "subject",
            subjectId = id
        });
    }
    public async Task<IActionResult> OnPostDeleteSubjectAsync(int id, CancellationToken ct)
    {
        try
        {
            await catalog.DeleteSubjectAsync(id, ct);
            TempData["Success"] = L10n.T("The subject and everything under it was deleted.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException) { TempData["Error"] = L10n.T(ex.Message); }
        return RedirectToPage(new
        {
            tab = "subject"
        });
    }
    public async Task<IActionResult> OnPostCreateTopicAsync([Bind(Prefix = "Topic")] CatalogForm input, CancellationToken ct)
    {
        try
        {
            await catalog.CreateTopicAsync(new TopicInput(input.SubjectId, input.Name.Trim(), input.Description?.Trim() ?? ""), ct);
            TempData["Success"] = L10n.T("The topic was added.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { TempData["Error"] = L10n.T(ex.Message); }
        return RedirectToPage(new
        {
            tab = "subject",
            subjectId = input.SubjectId
        });
    }
    public async Task<IActionResult> OnPostUpdateTopicAsync(int id, int subjectId, [Bind(Prefix = "Topic")] CatalogForm input, CancellationToken ct)
    {
        try
        {
            await catalog.UpdateTopicAsync(id, new TopicInput(subjectId, input.Name.Trim(), input.Description?.Trim() ?? ""), ct);
            TempData["Success"] = L10n.T("The topic was updated.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { TempData["Error"] = L10n.T(ex.Message); }
        return RedirectToPage(new
        {
            tab = "subject",
            subjectId
        });
    }
    public async Task<IActionResult> OnPostDeleteTopicAsync(int id, int subjectId, CancellationToken ct)
    {
        try
        {
            await catalog.DeleteTopicAsync(id, ct);
            TempData["Success"] = L10n.T("The topic and its material were deleted.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException) { TempData["Error"] = L10n.T(ex.Message); }
        return RedirectToPage(new
        {
            tab = "subject",
            subjectId
        });
    }
    public async Task<IActionResult> OnPostCreateMaterialAsync([Bind(Prefix = "Material")] MaterialForm form, CancellationToken ct)
    {
        try
        {
            await catalog.CreateMaterialAsync(form.ToInput(), ct);
            TempData["Success"] = L10n.T("The material was created.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { TempData["Error"] = L10n.T(ex.Message); }
        return RedirectToPage(new
        {
            tab = "material",
            subjectId = form.SubjectId,
            topicId = form.TopicId
        });
    }
    public async Task<IActionResult> OnPostUpdateMaterialAsync(int id, [Bind(Prefix = "Material")] MaterialForm form, CancellationToken ct)
    {
        try
        {
            await catalog.UpdateMaterialAsync(id, form.ToInput(), ct);
            TempData["Success"] = L10n.T("The material was updated.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { TempData["Error"] = L10n.T(ex.Message); }
        return RedirectToPage(new
        {
            tab = "material",
            subjectId = form.SubjectId,
            topicId = form.TopicId
        });
    }
    public async Task<IActionResult> OnPostDeleteMaterialAsync(int id, int? subjectId, int? topicId, CancellationToken ct)
    {
        try
        {
            await catalog.DeleteMaterialAsync(id, ct);
            TempData["Success"] = L10n.T("The material was deleted.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { TempData["Error"] = L10n.T(ex.Message); }
        return RedirectToPage(new
        {
            tab = "material",
            subjectId,
            topicId
        });
    }
    public async Task<IActionResult> OnPostImportMaterialAsync(int topicId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = L10n.T("Choose a file to import.");
            return RedirectToPage(new
            {
                tab = "material",
                topicId
            });
        }
        if (file.Length > MaxUploadBytes)
        {
            TempData["Error"] = L10n.T("The file is larger than 20 MB.");
            return RedirectToPage(new
            {
                tab = "material",
                topicId
            });
        }
        if (!MaterialTextExtractor.Extensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
        {
            TempData["Error"] = L10n.T("Supported formats: .txt, .md, .csv, .json, .pdf, .docx, .pptx");
            return RedirectToPage(new
            {
                tab = "material",
                topicId
            });
        }
        try
        {
            await using var stream = file.OpenReadStream();
            var content = await MaterialTextExtractor.ExtractAsync(stream, file.FileName, ct);
            if (content.Length < 10)
                throw new IOException();
            await catalog.CreateMaterialAsync(new MaterialInput(topicId, Path.GetFileNameWithoutExtension(file.FileName), content,
                Path.GetFileName(file.FileName), MaterialSourceType.ImportedFile), ct);
            TempData["Success"] = L10n.T("The file was imported as material.");
        }
        catch (ArgumentException ex) { TempData["Error"] = L10n.T(ex.Message); }
        catch (IOException ex) { logger.LogWarning(ex, "Could not read catalog material"); TempData["Error"] = L10n.T("The imported file does not contain enough text."); }
        return RedirectToPage(new
        {
            tab = "material",
            topicId
        });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Subjects = await catalog.GetSubjectsAsync(ct);
        Topics = await catalog.GetTopicsAsync(cancellationToken: ct);
        var all = await catalog.GetMaterialsAsync(cancellationToken: ct);
        Materials = all.Where(x => (SubjectId is null or 0 || Topics.FirstOrDefault(t => t.Id == x.TopicId)?.SubjectId == SubjectId)
            && (TopicId is null or 0 || x.TopicId == TopicId)
            && (string.IsNullOrWhiteSpace(MaterialFilter) || x.Title.Contains(MaterialFilter.Trim(), StringComparison.OrdinalIgnoreCase)
                || x.Content.Contains(MaterialFilter.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

public sealed class CatalogForm
{
    public int SubjectId
    {
        get; set;
    }
    public string Name { get; set; } = ""; public string? Description
    {
        get; set;
    }
}
public sealed class MaterialForm
{
    public int SubjectId
    {
        get; set;
    }
    public int TopicId
    {
        get; set;
    }
    public string Title { get; set; } = "";
    public string Content { get; set; } = ""; public string? SourceFileName
    {
        get; set;
    }
    public bool IsImportedFile
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
    public MaterialInput ToInput() => new(TopicId, Title.Trim(), Content, SourceFileName, IsImportedFile ? MaterialSourceType.ImportedFile : MaterialSourceType.Manual, IsActive);
}
