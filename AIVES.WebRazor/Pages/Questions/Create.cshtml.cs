using AIVES.BLL.Services;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Questions;

public sealed class CreateModel(IQuestionService questions, IBloomLevelService bloomLevels, IRubricService rubrics,
    ICatalogService catalog, IQuestionGeneratorRouter generator, ILiveUpdates live, ILogger<CreateModel> logger) : PageModel, IQuestionFormPage
{
    [BindProperty]
    public QuestionInput Input { get; set; } = new();

    [BindProperty]
    public AiInput Ai { get; set; } = new();

    public QuestionFormOptions Options { get; private set; } = null!;
    public bool GeminiAvailable => generator.IsProviderAvailable(AiProvider.Gemini);
    public bool OllamaAvailable => generator.IsProviderAvailable(AiProvider.Ollama);
    public AiProvider? ActiveProvider => generator.ActiveProvider;

    public async Task OnGetAsync(int? subjectId, int? topicId, CancellationToken cancellationToken)
    {
        Input.SubjectId = subjectId;
        Input.TopicId = topicId;
        Options = await QuestionFormOptions.LoadAsync(bloomLevels, rubrics, catalog, cancellationToken);
    }

    public async Task<PartialViewResult> OnPostGenerateQuickAsync(CancellationToken cancellationToken)
    {
        Ai.Results = [];
        Ai.Sources = [];
        Ai.HasResult = false;

        var subject = (await catalog.GetSubjectsAsync(cancellationToken)).FirstOrDefault(x => x.Id == Ai.SubjectId);
        var topic = Ai.TopicId is null
            ? null
            : (await catalog.GetTopicsAsync(Ai.SubjectId, cancellationToken)).FirstOrDefault(x => x.Id == Ai.TopicId);

        if (subject is null)
            ModelState.AddModelError(string.Empty, L10n.T("Pick a subject from the catalogue before generating."));
        else if (Ai.TopicId is not null && topic is null)
            ModelState.AddModelError(string.Empty, L10n.T("That topic is no longer in the catalogue."));

        if (ModelState.IsValid && subject is not null)
        {
            try
            {
                var rag = Ai.UseMaterials
                    ? await catalog.BuildRagContextAsync(topic?.Id, topic is null ? subject.Id : null,
                        $"{subject.Name} {topic?.Name} {Ai.LearningOutcomes}", cancellationToken)
                    : RagContext.Empty;
                var bloom = (await bloomLevels.GetAllAsync()).FirstOrDefault(x => x.Id == Ai.BloomLevelId)?.Name;
                var generated = await generator.GenerateAsync(new QuestionGenerationRequest(subject.Name, topic?.Name ?? "",
                    Ai.LearningOutcomes?.Trim(), 1, rag.Text, rag.Sources, bloom,
                    QuestionDifficulties.Normalize(Ai.Difficulty)), Ai.Provider, cancellationToken);

                Ai.Results = generated.Select(x => new GeneratedQuestionInput
                {
                    Content = x.Content,
                    ExpectedAnswer = x.ExpectedAnswer,
                    BloomLevel = bloom ?? x.BloomLevel,
                    Difficulty = QuestionDifficulties.Normalize(Ai.Difficulty) ?? x.Difficulty,
                    FollowUpQuestions = x.FollowUpQuestions
                }).ToList();
                Ai.SubjectName = subject.Name;
                Ai.TopicName = topic?.Name ?? "";
                Ai.Sources = rag.Sources.Select(x => x.Title).ToList();
                Ai.HasResult = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Quick question generation failed");
                ModelState.AddModelError(string.Empty, L10n.T("Could not generate questions right now. Please try again later."));
            }
        }

        Options = await QuestionFormOptions.LoadAsync(bloomLevels, rubrics, catalog, cancellationToken);
        return Partial("_AiGenerateResults", this);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var created = await questions.CreateQuestionAsync(Input.ToDto());
                await live.EntityChangedAsync(LiveEntities.Question, LiveActions.Created, created.Id, created.Content);
                TempData["Success"] = $"Đã tạo câu hỏi #{created.Id}.";
                return RedirectToPage("Index");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Question was rejected");
                ModelState.AddModelError(string.Empty, L10n.T(ex.Message));
            }
        }

        Options = await QuestionFormOptions.LoadAsync(bloomLevels, rubrics, catalog, cancellationToken);
        return Page();
    }
}
