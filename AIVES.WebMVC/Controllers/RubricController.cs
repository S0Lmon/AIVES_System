using AIVES.BLL.Services;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebMVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>
/// Rubric bank with its own tabs: the list of stored matrices, the editor for defining rows and
/// columns by hand, and the AI tab that proposes a whole matrix to start from.
/// </summary>
[Authorize]
public sealed class RubricController : Controller
{
    private readonly IRubricService _rubrics;
    private readonly IBloomLevelService _blooms;
    private readonly ICatalogService _catalog;
    private readonly IRubricGeneratorRouter _generator;
    private readonly ILogger<RubricController> _logger;

    public RubricController(IRubricService rubrics, IBloomLevelService blooms, ICatalogService catalog,
        IRubricGeneratorRouter generator, ILogger<RubricController> logger)
    {
        _rubrics = rubrics;
        _blooms = blooms;
        _catalog = catalog;
        _generator = generator;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? tab, string? search, string? usage)
    {
        var view = new RubricViewModel
        {
            ActiveTab = RubricTabs.Normalize(tab),
            SubjectFilter = search,
            UsageFilter = usage
        };

        try
        {
            var blooms = await _blooms.GetAllAsync();
            view.BloomLevels = blooms.Select(bloom => new SelectListItemOption(bloom.Id, bloom.Name)).ToList();
            view.Ai.Subjects = (await _catalog.GetSubjectsAsync()).Select(subject => new SubjectOption(subject.Id, subject.Name)).ToList();
            var topics = await _catalog.GetTopicsAsync();
            view.Ai.Topics = topics.Select(topic => new TopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList();
            view.Ai.AllTopics = view.Ai.Topics;
            view.Ai.GeminiAvailable = _generator.IsProviderAvailable(AiProvider.Gemini);
            view.Ai.OllamaAvailable = _generator.IsProviderAvailable(AiProvider.Ollama);
            view.Ai.ActiveProvider = _generator.ActiveProvider ?? AiProvider.Gemini;

            var stored = (await _rubrics.GetAllRubricsAsync()).ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                stored = stored
                    .Where(rubric => rubric.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || rubric.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || rubric.Criteria.Any(criterion => criterion.Criterion.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            view.Rubrics = stored.Select(rubric => new RubricRow
            {
                Id = rubric.Id,
                Name = rubric.Name,
                Description = rubric.Description,
                TotalPoints = rubric.TotalPoints,
                CriterionCount = rubric.Criteria.Count,
                LevelCount = rubric.Levels.Count,
                LevelNames = rubric.Levels.OrderBy(level => level.Points).Select(level => level.Name).ToList(),
                ModifiedDate = rubric.ModifiedDate
            }).ToList();

            view.RubricCount = stored.Count;
            view.CriteriaCount = stored.Sum(rubric => rubric.Criteria.Count);
            view.LevelCount = stored.Sum(rubric => rubric.Levels.Count);
            view.MaxPoints = stored.Count == 0 ? 0 : stored.Max(rubric => rubric.TotalPoints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the rubric bank");
            TempData["Error"] = L10n.T("An error occurred while retrieving rubrics");
            return RedirectToAction("Index", "Home");
        }

        if (view.ActiveTab == RubricTabs.Create)
            view.Matrix = RubricMatrixModel.Empty();

        return View(view);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null or <= 0)
            return NotFound();

        var rubric = await _rubrics.GetRubricByIdAsync(id.Value);
        if (rubric is null)
            return NotFound();

        var view = new RubricViewModel
        {
            ActiveTab = RubricTabs.Create,
            Matrix = ToMatrixModel(rubric)
        };
        ViewData["MatrixAction"] = "Update";
        ViewData["MatrixRouteId"] = rubric.Id;
        return View(nameof(Index), view);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RubricMatrixModel model)
    {
        return await SaveRubricAsync(model, isEdit: false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, RubricMatrixModel model)
    {
        model.Id = id;
        model.IsEdit = true;
        return await SaveRubricAsync(model, isEdit: true);
    }

    private async Task<IActionResult> SaveRubricAsync(RubricMatrixModel model, bool isEdit)
    {
        try
        {
            var saved = isEdit
                ? await _rubrics.UpdateRubricAsync(model.ToDto())
                : await _rubrics.CreateRubricAsync(model.ToDto());

            TempData["Success"] = L10n.Format("Rubric '{0}' saved successfully", saved.Name);
            return RedirectToAction(nameof(Index), new { tab = RubricTabs.Bank });
        }
        catch (ArgumentException ex)
        {
            // Bad shape: keep the grid the lecturer typed and point at what needs fixing.
            ModelState.AddModelError(string.Empty, ex.Message);
            var view = new RubricViewModel { ActiveTab = RubricTabs.Create, Matrix = model };
            await PopulateAsync(view);
            return View(nameof(Index), view);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving rubric {Id}", model.Id);
            ModelState.AddModelError(string.Empty, L10n.T("An error occurred while saving the rubric"));
            var view = new RubricViewModel { ActiveTab = RubricTabs.Create, Matrix = model };
            await PopulateAsync(view);
            return View(nameof(Index), view);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (id <= 0)
            return BadRequest();

        try
        {
            await _rubrics.DeleteRubricAsync(id);
            TempData["Success"] = L10n.T("Rubric deleted successfully");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error deleting rubric {Id}", id);
            TempData["Error"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting rubric {Id}", id);
            TempData["Error"] = L10n.T("An error occurred while deleting the rubric");
        }

        return RedirectToAction(nameof(Index), new { tab = RubricTabs.Bank });
    }

    /// <summary>Proposes a whole matrix so the editor opens with rows and columns already filled in.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(AiRubricGeneratorViewModel model, CancellationToken cancellationToken)
    {
        var view = new RubricViewModel { ActiveTab = RubricTabs.Ai, Ai = model };

        try
        {
            if (!ModelState.IsValid)
            {
                await PopulateAiAsync(view);
                return View(nameof(Index), view);
            }

            var context = model.UseMaterials
                ? await _catalog.BuildRagContextAsync(model.TopicId, model.TopicId is null ? model.SubjectId : null,
                    $"{model.Subject} {model.Topic} {model.LearningOutcomes}", cancellationToken)
                : RagContext.Empty;

            var generated = await _generator.GenerateAsync(
                new RubricGenerationRequest(model.Subject.Trim(), model.Topic.Trim(),
                    model.CriterionCount, model.LevelCount, model.LearningOutcomes?.Trim(),
                    context.Text, context.Sources), model.Provider, cancellationToken);

            model.Generated = ToMatrixModel(generated);
            model.HasResult = true;
            model.Selected = true;
            model.Sources = context.Sources.Select(source => new MaterialExcerptViewModel(source.MaterialId, source.Title)).ToList();
            view.Ai = model;
            view.ActiveTab = RubricTabs.Ai;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Rubric generation was rejected");
            ModelState.AddModelError(string.Empty,
                _generator.ActiveProvider is null
                    ? L10n.T("The AI rubric generator is temporarily unavailable. Please try again later.")
                    : L10n.T("Could not generate a rubric right now. Please try again later."));
            view.Ai = model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rubric generation failed");
            ModelState.AddModelError(string.Empty, L10n.T("Something went wrong while generating a rubric. Please try again."));
            view.Ai = model;
        }

        await PopulateAiAsync(view);
        return View(nameof(Index), view);
    }

    /// <summary>Saves a proposed matrix as a stored rubric, optionally straight into the bank list.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGenerated(RubricMatrixModel matrix, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await _rubrics.CreateRubricAsync(matrix.ToDto());
            TempData["Success"] = L10n.Format("Rubric '{0}' saved successfully", saved.Name);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var view = new RubricViewModel { ActiveTab = RubricTabs.Ai, Matrix = matrix };
            await PopulateAsync(view);
            return View(nameof(Index), view);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving a generated rubric");
            TempData["Error"] = L10n.T("An error occurred while saving the rubric");
        }

        return RedirectToAction(nameof(Index), new { tab = RubricTabs.Bank });
    }

    private static RubricMatrixModel ToMatrixModel(RubricDto rubric) => new()
    {
        Id = rubric.Id,
        IsEdit = rubric.Id > 0,
        Name = rubric.Name,
        Description = rubric.Description,
        TotalPoints = rubric.TotalPoints,
        Columns = rubric.Levels.OrderBy(level => level.Order).Select(level => new MatrixColumnInput
        {
            Name = level.Name,
            Points = level.Points,
            Description = level.Description
        }).ToList(),
        Rows = rubric.Criteria.OrderBy(criterion => criterion.Order).Select(criterion => new MatrixRowInput
        {
            Criterion = criterion.Criterion,
            Description = criterion.Description,
            IsNew = false,
            Cells = rubric.Levels.OrderBy(level => level.Order).Select(level =>
            {
                var cell = criterion.Levels.FirstOrDefault(candidate => candidate.RubricLevelId == level.Id);
                return new MatrixCellInput
                {
                    Descriptor = cell?.Descriptor ?? string.Empty,
                    Points = cell?.Points ?? level.Points
                };
            }).ToList()
        }).ToList()
    };

    private static RubricMatrixModel ToMatrixModel(GeneratedRubric generated)
    {
        var levels = generated.Levels.OrderBy(level => level.Points).ToList();
        return new RubricMatrixModel
        {
            Name = generated.Name,
            Description = generated.Description,
            Columns = levels.Select(level => new MatrixColumnInput { Name = level.Name, Points = level.Points }).ToList(),
            Rows = generated.Criteria.Select(criterion => new MatrixRowInput
            {
                Criterion = criterion.Criterion,
                IsNew = true,
                Cells = levels.Select(level =>
                {
                    var cell = criterion.Cells.FirstOrDefault(candidate =>
                        string.Equals(candidate.LevelName, level.Name, StringComparison.OrdinalIgnoreCase));
                    return new MatrixCellInput
                    {
                        Descriptor = cell?.Descriptor ?? string.Empty,
                        Points = cell?.Points ?? level.Points
                    };
                }).ToList()
            }).ToList()
        };
    }

    private async Task PopulateAsync(RubricViewModel view)
    {
        view.BloomLevels = (await _blooms.GetAllAsync()).Select(bloom => new SelectListItemOption(bloom.Id, bloom.Name)).ToList();
        await PopulateAiAsync(view);
    }

    private async Task PopulateAiAsync(RubricViewModel view)
    {
        if (view.Ai.Subjects.Count == 0)
            view.Ai.Subjects = (await _catalog.GetSubjectsAsync()).Select(subject => new SubjectOption(subject.Id, subject.Name)).ToList();
        if (view.Ai.Topics.Count == 0)
            view.Ai.Topics = (await _catalog.GetTopicsAsync()).Select(topic => new TopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList();
        view.Ai.AllTopics = view.Ai.Topics;
        view.Ai.GeminiAvailable = _generator.IsProviderAvailable(AiProvider.Gemini);
        view.Ai.OllamaAvailable = _generator.IsProviderAvailable(AiProvider.Ollama);
        view.Ai.ActiveProvider = _generator.ActiveProvider ?? AiProvider.Gemini;
    }
}