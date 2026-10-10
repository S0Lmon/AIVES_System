using System.Text.Json;
using AIVES.BLL.Services;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Import;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AIVES.WebRazor.Pages.Questions;

public sealed class IndexModel(IQuestionService questions, IBloomLevelService bloomLevels, IRubricService rubrics,
    ICatalogService catalog, IQuestionGeneratorRouter generator, ILiveUpdates live, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? BloomLevelId { get; set; }
    [BindProperty(SupportsGet = true)] public int? RubricId { get; set; }
    [BindProperty(SupportsGet = true)] public int? SubjectId { get; set; }
    [BindProperty(SupportsGet = true)] public int? TopicId { get; set; }
    [BindProperty(SupportsGet = true)] public bool ActiveOnly { get; set; }
    [BindProperty] public QuestionInput Editor { get; set; } = new();
    [BindProperty] public AiInput Ai { get; set; } = new();
    [BindProperty] public BulkInput Bulk { get; set; } = new();
    [BindProperty] public string? ReviewJson { get; set; }
    [BindProperty] public List<int> Pick { get; set; } = [];
    [BindProperty] public List<string> EditedContent { get; set; } = [];
    [BindProperty] public List<string> EditedAnswers { get; set; } = [];
    [BindProperty] public List<string> EditedBloom { get; set; } = [];
    [BindProperty] public List<string> EditedDifficulty { get; set; } = [];

    public IReadOnlyList<QuestionDto> Questions { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int ActiveCount { get; private set; }
    public int CoveredByRubric { get; private set; }
    public int BloomSpread { get; private set; }
    public SelectList BloomLevels { get; private set; } = null!;
    public SelectList Rubrics { get; private set; } = null!;
    public IReadOnlyList<SubjectDto> Subjects { get; private set; } = [];
    public IReadOnlyList<TopicDto> Topics { get; private set; } = [];
    public IReadOnlyList<string> ImportProblems { get; private set; } = [];
    public string ActiveTab => Tab?.ToLowerInvariant() is "create" or "ai" or "bulk" or "import" ? Tab.ToLowerInvariant() : "bank";
    public bool GeminiAvailable => generator.IsProviderAvailable(AiProvider.Gemini);
    public bool OllamaAvailable => generator.IsProviderAvailable(AiProvider.Ollama);

    public async Task OnGetAsync(CancellationToken ct) { await LoadOptionsAsync(ct); await LoadAsync(); }

    public async Task<PartialViewResult> OnGetRowsAsync(CancellationToken ct)
    {
        await LoadAsync();
        return Partial("_QuestionRows", this);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var created = await questions.CreateQuestionAsync(Editor.ToDto());
                await live.EntityChangedAsync(LiveEntities.Question, LiveActions.Created, created.Id, created.Content);
                TempData["Success"] = L10n.Format("Question '{0}...' created successfully", created.Content[..Math.Min(50, created.Content.Length)]);
                return RedirectToPage(new { tab = "bank" });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            { ModelState.AddModelError(string.Empty, L10n.T(ex.Message)); }
        }
        Tab = "create"; await LoadOptionsAsync(ct); await LoadAsync(); return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync(CancellationToken ct)
    {
        Tab = "ai";
        var subject = (await catalog.GetSubjectsAsync(ct)).FirstOrDefault(x => x.Id == Ai.SubjectId);
        var topic = Ai.TopicId is null ? null : (await catalog.GetTopicsAsync(Ai.SubjectId, ct)).FirstOrDefault(x => x.Id == Ai.TopicId);
        if (subject is null) ModelState.AddModelError(string.Empty, L10n.T("Pick a subject from the catalogue before generating."));
        else if (Ai.TopicId is not null && topic is null) ModelState.AddModelError(string.Empty, L10n.T("That topic is no longer in the catalogue."));
        if (ModelState.IsValid && subject is not null)
        {
            try
            {
                var rag = Ai.UseMaterials ? await catalog.BuildRagContextAsync(topic?.Id, topic is null ? subject.Id : null,
                    $"{subject.Name} {topic?.Name} {Ai.LearningOutcomes}", ct) : RagContext.Empty;
                var bloom = (await bloomLevels.GetAllAsync()).FirstOrDefault(x => x.Id == Ai.BloomLevelId)?.Name;
                var results = await generator.GenerateAsync(new QuestionGenerationRequest(subject.Name, topic?.Name ?? "",
                    Ai.LearningOutcomes?.Trim(), 1, rag.Text, rag.Sources, bloom,
                    QuestionDifficulties.Normalize(Ai.Difficulty)), Ai.Provider, ct);
                Ai.Results = results.Select(x => new GeneratedQuestionInput
                { Content = x.Content, ExpectedAnswer = x.ExpectedAnswer, BloomLevel = bloom ?? x.BloomLevel,
                    Difficulty = QuestionDifficulties.Normalize(Ai.Difficulty) ?? x.Difficulty }).ToList();
                Ai.SubjectName = subject.Name; Ai.TopicName = topic?.Name ?? ""; Ai.HasResult = true;
                Ai.Sources = rag.Sources.Select(x => x.Title).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Question generation failed"); ModelState.AddModelError(string.Empty, L10n.T("Could not generate questions right now. Please try again later.")); }
        }
        await LoadOptionsAsync(ct); await LoadAsync(); return Page();
    }

    public async Task<IActionResult> OnPostGenerateBulkAsync(CancellationToken ct)
    {
        Tab = "bulk";
        var subject = (await catalog.GetSubjectsAsync(ct)).FirstOrDefault(x => x.Id == Bulk.SubjectId);
        var topic = Bulk.TopicId is null ? null : (await catalog.GetTopicsAsync(Bulk.SubjectId, ct)).FirstOrDefault(x => x.Id == Bulk.TopicId);
        if (subject is null) ModelState.AddModelError(string.Empty, L10n.T("Pick a subject from the catalogue before generating."));
        if (!Bulk.TryAllocate(out var counts, out var problem)) ModelState.AddModelError(string.Empty, problem ?? L10n.T("The bulk plan could not be split."));
        if (ModelState.IsValid && subject is not null)
        {
            try
            {
                var rag = Bulk.UseMaterials ? await catalog.BuildRagContextAsync(topic?.Id, topic is null ? subject.Id : null,
                    $"{subject.Name} {topic?.Name} {Bulk.LearningOutcomes}", ct) : RagContext.Empty;
                var levels = (await bloomLevels.GetAllAsync()).OrderBy(x => x.Order).ToList();
                var result = new List<GeneratedQuestionInput>();
                var skipped = new List<string>();
                foreach (var row in Bulk.Plan)
                {
                    var count = counts.GetValueOrDefault(row.Id);
                    if (count == 0) continue;
                    var levelName = levels.FirstOrDefault(x => x.Id == row.Id)?.Name ?? row.Name;
                    try
                    {
                        var batch = await generator.GenerateAsync(new QuestionGenerationRequest(subject.Name, topic?.Name ?? "",
                            Bulk.LearningOutcomes?.Trim(), count, rag.Text, rag.Sources, levelName,
                            QuestionDifficulties.Normalize(Bulk.DifficultyFrom), QuestionDifficulties.Normalize(Bulk.DifficultyTo)), Bulk.Provider, ct);
                        result.AddRange(batch.Select(x => new GeneratedQuestionInput { Content = x.Content, ExpectedAnswer = x.ExpectedAnswer,
                            BloomLevel = levelName, Difficulty = QuestionDifficulties.Normalize(x.Difficulty) ?? "" }));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Bulk generation skipped {BloomLevel}", levelName); skipped.Add(levelName); }
                }
                if (result.Count == 0) ModelState.AddModelError(string.Empty, L10n.T("Could not generate questions right now. Please try again later."));
                if (skipped.Count > 0 && result.Count > 0) ModelState.AddModelError(string.Empty, L10n.Format("No questions came back for {0}. Everything else was generated.", string.Join(", ", skipped)));
                Bulk.SubjectName = subject.Name; Bulk.TopicName = topic?.Name ?? ""; Bulk.Results = result; Bulk.HasResult = true;
                Bulk.Sources = rag.Sources.Select(x => x.Title).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Bulk question generation failed"); ModelState.AddModelError(string.Empty, L10n.T("Could not generate questions right now. Please try again later.")); }
        }
        await LoadOptionsAsync(ct); await LoadAsync(); return Page();
    }

    public async Task<IActionResult> OnPostImportAsync(IFormFile? file, int? subjectId, int? topicId)
    {
        Tab = "import";
        if (file is null || file.Length == 0) ImportProblems = [L10n.T("Choose a file to import.")];
        else if (file.Length > 5 * 1024 * 1024) ImportProblems = [L10n.T("The file is larger than 5 MB.")];
        else
        {
            try
            {
                await using var stream = file.OpenReadStream(); using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer); buffer.Position = 0;
                var parsed = QuestionImportParser.Parse(buffer, file.FileName);
                ImportProblems = parsed.Problems;
                Ai = new AiInput { SubjectId = subjectId ?? 0, TopicId = topicId, HasResult = parsed.Questions.Count > 0,
                    SubjectName = (await catalog.GetSubjectsAsync()).FirstOrDefault(x => x.Id == subjectId)?.Name ?? "",
                    TopicName = (await catalog.GetTopicsAsync(subjectId)).FirstOrDefault(x => x.Id == topicId)?.Name ?? "",
                    Results = parsed.Questions.Select(x => new GeneratedQuestionInput { Content = x.Content, ExpectedAnswer = x.ExpectedAnswer,
                        BloomLevel = x.BloomLevel, Difficulty = x.Difficulty }).ToList() };
            }
            catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or InvalidDataException or FormatException)
            { ImportProblems = [ex is ArgumentException ? ex.Message : L10n.T("The file could not be read. Check its format against the template.")]; }
        }
        await LoadOptionsAsync(default); await LoadAsync(); return Page();
    }

    public async Task<IActionResult> OnPostSaveReviewedAsync(string? returnTab, int? rubricId, int? subjectId, int? topicId,
        CancellationToken ct)
    {
        var results = Deserialize(ReviewJson);
        var levels = (await bloomLevels.GetAllAsync()).ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var saved = 0;
        for (var i = 0; i < results.Count; i++)
        {
            if (!Pick.Contains(i) || i >= EditedContent.Count || string.IsNullOrWhiteSpace(EditedContent[i])) continue;
            var bloom = i < EditedBloom.Count ? EditedBloom[i] : results[i].BloomLevel;
            if (!levels.TryGetValue(bloom ?? "", out var bloomId)) { ModelState.AddModelError("", L10n.Format("Could not match the generated Bloom level '{0}' to a level in the bank.", bloom)); continue; }
            var subjectName = (await catalog.GetSubjectsAsync(ct)).FirstOrDefault(x => x.Id == subjectId)?.Name;
            var topicName = (await catalog.GetTopicsAsync(subjectId, ct)).FirstOrDefault(x => x.Id == topicId)?.Name;
            await questions.CreateQuestionAsync(new QuestionDto { Content = EditedContent[i].Trim(),
                ExpectedAnswer = i < EditedAnswers.Count ? EditedAnswers[i] : results[i].ExpectedAnswer,
                BloomLevelId = bloomId, Difficulty = i < EditedDifficulty.Count ? QuestionDifficulties.Normalize(EditedDifficulty[i]) : results[i].Difficulty,
                RubricId = rubricId, SubjectId = subjectId, TopicId = topicId,
                Context = string.Join(" / ", new[] { subjectName, topicName }.Where(x => !string.IsNullOrWhiteSpace(x))), IsActive = true });
            saved++;
        }
        if (saved > 0) TempData["Success"] = L10n.Format("{0} questions added to the bank", saved);
        else if (Pick.Count == 0) TempData["Error"] = L10n.T("Tick at least one question before saving.");
        return RedirectToPage(new { tab = "bank" });
    }

    private async Task LoadOptionsAsync(CancellationToken ct)
    {
        BloomLevels = new SelectList((await bloomLevels.GetAllAsync()).OrderBy(x => x.Order), "Id", "Name", BloomLevelId);
        Rubrics = new SelectList(await rubrics.GetAllRubricsAsync(), "Id", "Name", RubricId);
        Subjects = await catalog.GetSubjectsAsync(ct); Topics = await catalog.GetTopicsAsync(cancellationToken: ct);
        if (Bulk.Plan.Count == 0) Bulk.Plan = (await bloomLevels.GetAllAsync()).Select(x =>
            new BulkLevelInput { Id = x.Id, Name = x.Name, Min = 0, Max = x.Id >= 2 ? 5 : 0 }).ToList();
    }

    private async Task LoadAsync()
    {
        var all = (await questions.GetAllQuestionsAsync()).ToList(); TotalCount = all.Count;
        IEnumerable<QuestionDto> query = all;
        if (!string.IsNullOrWhiteSpace(Search)) query = query.Where(x => x.Content.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) || x.ExpectedAnswer.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase));
        if (BloomLevelId is > 0) query = query.Where(x => x.BloomLevelId == BloomLevelId);
        if (RubricId is > 0) query = query.Where(x => x.RubricId == RubricId);
        if (SubjectId is > 0) query = query.Where(x => x.SubjectId == SubjectId);
        if (TopicId is > 0) query = query.Where(x => x.TopicId == TopicId);
        if (ActiveOnly) query = query.Where(x => x.IsActive);
        Questions = query.OrderBy(x => x.DisplayOrder).ThenByDescending(x => x.ModifiedDate).ToList();
        ActiveCount = Questions.Count(x => x.IsActive); CoveredByRubric = Questions.Count(x => x.RubricId is > 0);
        BloomSpread = Questions.Select(x => x.BloomLevelId).Distinct().Count();
    }

    private static List<GeneratedQuestionInput> Deserialize(string? json)
    {
        try { return JsonSerializer.Deserialize<List<GeneratedQuestionInput>>(json ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }
}

public class AiInput
{
    public int SubjectId { get; set; } public int? TopicId { get; set; }
    public int? BloomLevelId { get; set; } public string? Difficulty { get; set; }
    public string? LearningOutcomes { get; set; } public bool UseMaterials { get; set; } = true;
    public AiProvider? Provider { get; set; } public string SubjectName { get; set; } = ""; public string TopicName { get; set; } = "";
    public bool HasResult { get; set; } public List<GeneratedQuestionInput> Results { get; set; } = [];
    public List<string> Sources { get; set; } = [];
}
public sealed class BulkInput : AiInput
{
    public int Total { get; set; } = 5; public string? DifficultyFrom { get; set; } public string? DifficultyTo { get; set; }
    public List<BulkLevelInput> Plan { get; set; } = [];
    public bool TryAllocate(out Dictionary<int, int> counts, out string? problem)
    {
        counts = []; problem = null;
        if (Plan.Count == 0) { problem = L10n.T("There are no Bloom levels to split the questions across."); return false; }
        if (Plan.Any(x => x.Min < 0 || x.Max < x.Min)) { problem = L10n.T("Each Bloom range must end at or above its minimum."); return false; }
        var min = Plan.Sum(x => x.Min); var max = Plan.Sum(x => x.Max);
        if (Total is < 1 or > 30 || Total < min || Total > max)
        { problem = L10n.Format("Ask for between {0} and {1} questions to fit your per level ranges.", min, max); return false; }
        counts = Plan.ToDictionary(x => x.Id, x => x.Min);
        var remaining = Total - min;
        foreach (var row in Plan) { var add = Math.Min(remaining, row.Max - row.Min); counts[row.Id] += add; remaining -= add; }
        return remaining == 0;
    }
}
public sealed class BulkLevelInput { public int Id { get; set; } public string Name { get; set; } = ""; public int Min { get; set; } public int Max { get; set; } = 5; }
public sealed class GeneratedQuestionInput
{
    public string Content { get; set; } = ""; public string ExpectedAnswer { get; set; } = "";
    public string BloomLevel { get; set; } = ""; public string Difficulty { get; set; } = "";
    public List<string> FollowUpQuestions { get; set; } = [];
}
