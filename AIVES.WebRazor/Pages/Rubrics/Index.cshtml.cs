using AIVES.BLL.Services;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Rubrics;

public sealed class IndexModel(IRubricService rubrics, ICatalogService catalog,
    IRubricGeneratorRouter generator, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty] public RubricMatrixInput Matrix { get; set; } = RubricMatrixInput.Default();
    [BindProperty] public RubricAiInput Ai { get; set; } = new();
    public IReadOnlyList<RubricDto> Rubrics { get; private set; } = [];
    public bool GeminiAvailable => generator.IsProviderAvailable(AiProvider.Gemini);
    public bool OllamaAvailable => generator.IsProviderAvailable(AiProvider.Ollama);
    public string ActiveTab => Tab?.ToLowerInvariant() is "create" or "ai" ? Tab.ToLowerInvariant() : "bank";

    public async Task OnGetAsync(int? id)
    {
        if (id is > 0)
        {
            var item = await rubrics.GetRubricByIdAsync(id.Value);
            if (item is not null) { Matrix = RubricMatrixInput.From(item); Tab = "create"; }
        }
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        try
        {
            var dto = Matrix.ToDto();
            var saved = dto.Id > 0 ? await rubrics.UpdateRubricAsync(dto) : await rubrics.CreateRubricAsync(dto);
            TempData["Success"] = L10n.Format("Rubric '{0}' saved successfully", saved.Name);
            return RedirectToPage(new { tab = "bank" });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, L10n.T(ex.Message)); Tab = "create"; await LoadAsync(); return Page();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error saving rubric");
            ModelState.AddModelError(string.Empty, L10n.T("An error occurred while saving the rubric")); Tab = "create"; await LoadAsync(); return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        try { await rubrics.DeleteRubricAsync(id); TempData["Success"] = L10n.T("Rubric deleted successfully"); }
        catch (Exception ex) { logger.LogError(ex, "Error deleting rubric {Id}", id); TempData["Error"] = L10n.T("An error occurred while deleting the rubric"); }
        return RedirectToPage(new { tab = "bank" });
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken ct)
    {
        Tab = "ai";
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        try
        {
            var subjects = await catalog.GetSubjectsAsync(ct);
            var subjectName = subjects.FirstOrDefault(x => x.Id == Ai.SubjectId)?.Name;
            var topics = await catalog.GetTopicsAsync(Ai.SubjectId, ct);
            var topicName = Ai.TopicId is null ? Ai.Topic : topics.FirstOrDefault(x => x.Id == Ai.TopicId)?.Name;
            if (string.IsNullOrWhiteSpace(subjectName) || string.IsNullOrWhiteSpace(topicName))
                ModelState.AddModelError(string.Empty, L10n.T("Choose a subject and topic from the catalogue."));
            else
            {
                var rag = Ai.UseMaterials ? await catalog.BuildRagContextAsync(Ai.TopicId, Ai.TopicId is null ? Ai.SubjectId : null,
                    $"{subjectName} {topicName} {Ai.LearningOutcomes}", ct) : RagContext.Empty;
                var generated = await generator.GenerateAsync(new RubricGenerationRequest(subjectName, topicName,
                    Ai.CriterionCount, Ai.LevelCount, Ai.LearningOutcomes, rag.Text, rag.Sources), Ai.Provider, ct);
                Matrix = RubricMatrixInput.From(generated);
                Ai.HasResult = true; Ai.Sources = rag.Sources.Select(x => x.Title).ToList();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Rubric generation failed"); ModelState.AddModelError(string.Empty, L10n.T("Something went wrong while generating a rubric. Please try again.")); }
        await LoadAsync(); return Page();
    }

    private async Task LoadAsync()
    {
        var all = (await rubrics.GetAllRubricsAsync()).OrderBy(x => x.Name);
        Rubrics = string.IsNullOrWhiteSpace(Search) ? all.ToList() : all.Where(x =>
            x.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
            || x.Description.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
            || x.Criteria.Any(c => c.Criterion.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

public sealed class RubricAiInput
{
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    public int SubjectId { get; set; }
    public int? TopicId { get; set; }
    public string? LearningOutcomes { get; set; }
    public int CriterionCount { get; set; } = 4;
    public int LevelCount { get; set; } = 4;
    public bool UseMaterials { get; set; } = true;
    public AiProvider? Provider { get; set; }
    public bool HasResult { get; set; }
    public List<string> Sources { get; set; } = [];
}

public sealed class RubricMatrixInput
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<RubricColumnInput> Columns { get; set; } = [];
    public List<RubricRowInput> Rows { get; set; } = [];
    public static RubricMatrixInput Default() => new()
    {
        Columns = [new() { Name = "Below", Points = 2 }, new() { Name = "Meets", Points = 4 }, new() { Name = "Exceeds", Points = 6 }],
        Rows = [new() { Cells = [new(), new(), new()] }]
    };
    public static RubricMatrixInput From(RubricDto d) => new()
    {
        Id = d.Id, Name = d.Name, Description = d.Description,
        Columns = d.Levels.OrderBy(x => x.Order).Select(x => new RubricColumnInput { Name = x.Name, Points = x.Points }).ToList(),
        Rows = d.Criteria.OrderBy(x => x.Order).Select(c => new RubricRowInput
        {
            Criterion = c.Criterion, Description = c.Description,
            Cells = d.Levels.OrderBy(x => x.Order).Select(level =>
            {
                var cell = c.Levels.FirstOrDefault(x => x.RubricLevelId == level.Id);
                return new RubricCellInput { Descriptor = cell?.Descriptor ?? "", Points = cell?.Points ?? level.Points };
            }).ToList()
        }).ToList()
    };
    public static RubricMatrixInput From(GeneratedRubric d) => new()
    {
        Name = d.Name, Description = d.Description,
        Columns = d.Levels.Select(x => new RubricColumnInput { Name = x.Name, Points = x.Points }).ToList(),
        Rows = d.Criteria.Select(c => new RubricRowInput
        {
            Criterion = c.Criterion,
            Cells = d.Levels.Select(level =>
            {
                var cell = c.Cells.FirstOrDefault(x => string.Equals(x.LevelName, level.Name, StringComparison.OrdinalIgnoreCase));
                return new RubricCellInput { Descriptor = cell?.Descriptor ?? "", Points = cell?.Points ?? level.Points };
            }).ToList()
        }).ToList()
    };
    public RubricDto ToDto()
    {
        var columns = Columns.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Take(6).ToList();
        var dto = new RubricDto { Id = Id, Name = Name.Trim(), Description = Description.Trim(),
            Levels = columns.Select((x, i) => new RubricLevelDto { Name = x.Name.Trim(), Points = x.Points, Order = i }).ToList() };
        for (var r = 0; r < Rows.Count && r < 10; r++)
        {
            var row = Rows[r]; if (string.IsNullOrWhiteSpace(row.Criterion)) continue;
            var criterion = new RubricCriterionDto { Criterion = row.Criterion.Trim(), Description = row.Description?.Trim() ?? "", Order = dto.Criteria.Count };
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = c < row.Cells.Count ? row.Cells[c] : new RubricCellInput();
                criterion.Levels.Add(new RubricCriterionLevelDto { LevelName = columns[c].Name, Descriptor = cell.Descriptor ?? "", Points = cell.Points });
            }
            dto.Criteria.Add(criterion);
        }
        return dto;
    }
}
public sealed class RubricColumnInput { public string Name { get; set; } = ""; public int Points { get; set; } public string? Description { get; set; } }
public sealed class RubricRowInput { public string Criterion { get; set; } = ""; public string? Description { get; set; } public List<RubricCellInput> Cells { get; set; } = []; }
public sealed class RubricCellInput { public string? Descriptor { get; set; } public int Points { get; set; } }
