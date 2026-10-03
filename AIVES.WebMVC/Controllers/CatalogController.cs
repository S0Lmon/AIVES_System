using AIVES.BLL.Services.Catalog;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebMVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>
/// One page for the whole catalog. The <c>subject</c> tab manages subject / topic, the
/// <c>material</c> tab manages material, and both read the same counts so the header never
/// disagrees with the tables underneath it.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Staff)]
public sealed class CatalogController(ICatalogService catalog, ILogger<CatalogController> logger) : Controller
{
    private const long MaxUploadBytes = 2 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".txt", ".md", ".markdown", ".csv", ".json"];

    [HttpGet]
    public async Task<IActionResult> Index(string? tab, string? subjectFilter, string? materialFilter,
        int? subjectId, int? topicId, int? open, CancellationToken cancellationToken)
    {
        ViewData["Title"] = L10n.T("Catalog");
        return View(await BuildAsync(CatalogTabs.Normalize(tab), subjectFilter, materialFilter, subjectId, topicId, open, cancellationToken));
    }

    // ---- Subject / topic -------------------------------------------------

    /// <summary>Creating a subject opens that subject's page so topics can be added straight away.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSubject(SubjectInput input, CancellationToken cancellationToken)
    {
        SubjectDto created;
        try
        {
            created = await catalog.CreateSubjectAsync(input, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            TempData["Error"] = L10n.T(ex.Message);
            return RedirectToAction(nameof(Index), new { tab = CatalogTabs.Subject });
        }

        TempData["Success"] = L10n.T("The subject was created. Add its topics below.");
        return RedirectToAction(nameof(Index), new { tab = CatalogTabs.Subject, open = created.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSubject(int id, SubjectInput input, CancellationToken cancellationToken) =>
        await MutateAsync(() => catalog.UpdateSubjectAsync(id, input, cancellationToken), open: id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSubject(int id, CancellationToken cancellationToken) =>
        await MutateAsync(() => catalog.DeleteSubjectAsync(id, cancellationToken), CatalogTabs.Subject,
            L10n.T("The subject and everything under it was deleted."));

    /// <summary>Adding a topic keeps the subject page open so several can be added in a row.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTopic(TopicInput input, CancellationToken cancellationToken)
    {
        try
        {
            await catalog.CreateTopicAsync(input, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            TempData["Error"] = L10n.T(ex.Message);
            return RedirectToAction(nameof(Index), new { tab = CatalogTabs.Subject, open = input.SubjectId });
        }

        TempData["Success"] = L10n.T("The topic was added.");
        return RedirectToAction(nameof(Index), new { tab = CatalogTabs.Subject, open = input.SubjectId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTopic(int id, int subjectId, TopicInput input, CancellationToken cancellationToken) =>
        await MutateAsync(() => catalog.UpdateTopicAsync(id, input, cancellationToken), open: subjectId);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTopic(int id, int subjectId, CancellationToken cancellationToken) =>
        await MutateAsync(() => catalog.DeleteTopicAsync(id, cancellationToken), CatalogTabs.Subject,
            L10n.T("The topic and its material were deleted."), open: subjectId);

    // ---- Material --------------------------------------------------------

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMaterial(MaterialEditViewModel model, CancellationToken cancellationToken) =>
        await MutateMaterialAsync(model, isCreate: true, cancellationToken: cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMaterial(int id, MaterialEditViewModel model, CancellationToken cancellationToken) =>
        await MutateMaterialAsync(model, isCreate: false, id: id, cancellationToken: cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMaterial(int id, int? subjectId, int? topicId, CancellationToken cancellationToken) =>
        await MutateAsync(async () =>
        {
            await catalog.DeleteMaterialAsync(id, cancellationToken);
            logger.LogInformation("Deleted material {MaterialId}", id);
        }, CatalogTabs.Material, L10n.T("The material was deleted."), subjectId, topicId);

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> ImportMaterial(int topicId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return await FailMaterial(L10n.T("Choose a file to import."), subjectId: null, topicId: topicId);

        if (file.Length > MaxUploadBytes)
            return await FailMaterial(L10n.T("The file is larger than 2 MB."), subjectId: null, topicId: topicId);

        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            return await FailMaterial(L10n.T("Supported formats: .txt, .md, .csv, .json"), subjectId: null, topicId: topicId);

        string content;
        try
        {
            using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            content = (await reader.ReadToEndAsync(cancellationToken)).Trim();
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read the uploaded material file");
            return await FailMaterial(L10n.T("The imported file does not contain enough text."), subjectId: null, topicId: topicId);
        }

        if (content.Length < 10)
            return await FailMaterial(L10n.T("The imported file does not contain enough text."), subjectId: null, topicId: topicId);

        return await MutateAsync(async () =>
        {
            await catalog.CreateMaterialAsync(new MaterialInput(topicId, Path.GetFileNameWithoutExtension(file.FileName),
                content, Path.GetFileName(file.FileName), MaterialSourceType.ImportedFile), cancellationToken);
            logger.LogInformation("Imported {FileName} into topic {TopicId}", file.FileName, topicId);
        }, CatalogTabs.Material, L10n.T("The file was imported as material."), subjectId: null, topicId: topicId);
    }

    // ---- Shared ----------------------------------------------------------

    private async Task<IActionResult> MutateMaterialAsync(MaterialEditViewModel model, bool isCreate,
        int id = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Title) || model.Title.Trim().Length < 2)
            return await FailMaterial(L10n.T("The material title must be between 2 and 300 characters."), subjectId: null, topicId: model.TopicId);

        if (string.IsNullOrWhiteSpace(model.Content) || model.Content.Trim().Length < 10)
            return await FailMaterial(L10n.T("The material content must be at least 10 characters."), subjectId: null, topicId: model.TopicId);

        var input = new MaterialInput(model.TopicId, model.Title, model.Content, model.SourceFileName,
            model.IsImportedFile ? MaterialSourceType.ImportedFile : MaterialSourceType.Manual, model.IsActive);

        return await MutateAsync(async () =>
        {
            if (isCreate)
            {
                await catalog.CreateMaterialAsync(input, cancellationToken);
            }
            else
            {
                await catalog.UpdateMaterialAsync(id, input, cancellationToken);
            }
        }, CatalogTabs.Material,
            isCreate ? L10n.T("The material was added to the topic.") : L10n.T("The material was updated."),
            subjectId: null, topicId: model.TopicId);
    }

    private async Task<IActionResult> MutateAsync(Func<Task> action, string tab = CatalogTabs.Subject,
        string? message = null, int? subjectId = null, int? topicId = null, int? open = null)
    {
        try
        {
            await action();
            TempData["Success"] = message ?? L10n.T("The catalog was updated.");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            TempData["Error"] = L10n.T(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog mutation failed");
            TempData["Error"] = L10n.T("The catalog could not be updated. Please try again.");
        }

        return RedirectToAction(nameof(Index), new { tab, subjectId, topicId, open });
    }

    private async Task<IActionResult> FailMaterial(string message, int? subjectId, int? topicId)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Index), new { tab = CatalogTabs.Material, subjectId, topicId });
    }

    private async Task<CatalogViewModel> BuildAsync(string tab, string? subjectFilter, string? materialFilter,
        int? subjectId, int? topicId, int? open, CancellationToken cancellationToken)
    {
        var subjects = await catalog.GetSubjectsAsync(cancellationToken);
        var topics = await catalog.GetTopicsAsync(cancellationToken: cancellationToken);
        var allMaterials = await catalog.GetMaterialsAsync(cancellationToken: cancellationToken);

        var nodes = subjects.Select(subject => new SubjectNode
        {
            Id = subject.Id,
            Name = subject.Name,
            Description = subject.Description,
            MaterialCount = subject.MaterialCount,
            Topics = topics.Where(topic => topic.SubjectId == subject.Id).Select(topic => new TopicNode
            {
                Id = topic.Id,
                SubjectId = topic.SubjectId,
                Name = topic.Name,
                Description = topic.Description,
                MaterialCount = topic.MaterialCount
            }).ToList()
        }).ToList();

        var visibleTopics = subjectId is { } id ? topics.Where(topic => topic.SubjectId == id).ToList() : topics;
        var effectiveTopicId = topicId is not null && visibleTopics.Any(topic => topic.Id == topicId) ? topicId : null;

        var text = (materialFilter ?? string.Empty).Trim();
        var materials = allMaterials
            .Where(material => effectiveTopicId is null || material.TopicId == effectiveTopicId)
            .Where(material => text.Length == 0
                || material.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || material.Content.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(material => new MaterialRow
            {
                Id = material.Id,
                TopicId = material.TopicId,
                SubjectId = topics.FirstOrDefault(topic => topic.Id == material.TopicId)?.SubjectId ?? 0,
                Title = material.Title,
                SubjectName = material.SubjectName,
                TopicName = material.TopicName,
                SourceFileName = material.SourceFileName,
                IsImported = material.SourceType == MaterialSourceType.ImportedFile,
                IsActive = material.IsActive,
                ContentLength = material.ContentLength,
                Content = material.Content,
                CreatedDate = material.CreatedDate,
                ModifiedDate = material.ModifiedDate
            })
            .ToList();

        var subjectText = (subjectFilter ?? string.Empty).Trim();
        var visibleSubjects = subjectText.Length == 0
            ? nodes
            : nodes.Where(subject => subject.Name.Contains(subjectText, StringComparison.OrdinalIgnoreCase)
                || subject.Topics.Any(topic => topic.Name.Contains(subjectText, StringComparison.OrdinalIgnoreCase))).ToList();

        // Only honour the open subject if it is still on screen, otherwise the page would 404 on a
        // deleted or filtered out id.
        var openSubject = open is { } openId ? visibleSubjects.FirstOrDefault(subject => subject.Id == openId) : null;

        return new CatalogViewModel
        {
            ActiveTab = tab,
            SubjectCount = subjects.Count,
            TopicCount = topics.Count,
            MaterialCount = allMaterials.Count,
            SubjectFilter = subjectText,
            MaterialFilter = text,
            FilterSubjectId = subjectId,
            FilterTopicId = effectiveTopicId,
            OpenSubjectId = openSubject?.Id,
            OpenSubject = openSubject,
            Subjects = visibleSubjects,
            Materials = materials,
            SubjectOptions = subjects.Select(subject => new SubjectOption(subject.Id, subject.Name)).ToList(),
            TopicOptions = visibleTopics.Select(topic => new TopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList()
        };
    }
}