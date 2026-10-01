using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebMVC.Models.ViewModels;
using AIVES.BLL.Services;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;

namespace AIVES.WebMVC.Controllers
{
    [Authorize]
    public class QuestionController : Controller
    {
        private readonly IQuestionService _questionService;
        private readonly IRubricService _rubricService;
        private readonly IBloomLevelService _bloomLevelService;
        private readonly ICatalogService _catalog;
        private readonly IQuestionGeneratorRouter _generator;
        private readonly ILogger<QuestionController> _logger;

        public QuestionController(
            IQuestionService questionService,
            IRubricService rubricService,
            IBloomLevelService bloomLevelService,
            ICatalogService catalog,
            IQuestionGeneratorRouter generator,
            ILogger<QuestionController> logger)
        {
            _questionService = questionService;
            _rubricService = rubricService;
            _bloomLevelService = bloomLevelService;
            _catalog = catalog;
            _generator = generator;
            _logger = logger;
        }

        /// <summary>
        /// The question bank is one page with four tabs: the stored list, the manual editor, AI
        /// generation and bulk generation. Each tab keeps its own state in the query string.
        /// </summary>
        public async Task<IActionResult> Index(string? tab, string? search, int? bloomLevelId, int? rubricId, bool activeOnly, int? subjectId, int? topicId)
        {
            var view = new QuestionBankViewModel
            {
                ActiveTab = QuestionTabs.Normalize(tab),
                Search = search,
                BloomLevelId = bloomLevelId,
                RubricId = rubricId,
                SubjectId = subjectId,
                TopicId = topicId,
                ActiveOnly = activeOnly
            };

            try
            {
                await PopulateDropdowns(view);

                var questions = (await _questionService.GetAllQuestionsAsync()).ToList();
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim();
                    questions = questions
                        .Where(question => question.Content.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || question.ExpectedAnswer.Contains(term, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (bloomLevelId is > 0)
                    questions = questions.Where(question => question.BloomLevelId == bloomLevelId).ToList();
                if (rubricId is > 0)
                    questions = questions.Where(question => question.RubricId == rubricId).ToList();
                if (subjectId is > 0)
                    questions = questions.Where(question => question.SubjectId == subjectId).ToList();
                if (topicId is > 0)
                    questions = questions.Where(question => question.TopicId == topicId).ToList();
                if (activeOnly)
                    questions = questions.Where(question => question.IsActive).ToList();

                view.Questions = questions.Select(MapToViewModel).ToList();
                view.TotalCount = (await _questionService.GetAllQuestionsAsync()).Count();
                view.ActiveCount = view.Questions.Count(question => question.IsActive);
                view.CoveredByRubric = view.Questions.Count(question => question.RubricId is > 0);
                view.BloomSpread = view.Questions.Select(question => question.BloomLevelId).Distinct().Count();
                view.LinkedToSubject = view.Questions.Count(question => question.SubjectId is > 0);
                view.LinkedToTopic = view.Questions.Count(question => question.TopicId is > 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting questions");
                TempData["Error"] = L10n.T("An error occurred while retrieving questions");
                return RedirectToAction("Index", "Home");
            }

            // The Create tab needs the compact panel, and the Bulk tab needs a default plan seeded
            // from the Bloom levels that exist.
            if (view.ActiveTab is QuestionTabs.Create or QuestionTabs.Bulk or QuestionTabs.Ai)
                await PopulateAiPanel(view);

            return View(view);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            try
            {
                var question = await _questionService.GetQuestionByIdAsync(id.Value);
                if (question == null)
                    return NotFound();

                var viewModel = MapToViewModel(question);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting question details");
                return NotFound();
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            try
            {
                var view = new QuestionBankViewModel { ActiveTab = QuestionTabs.Create, Editor = new QuestionViewModel() };
                await PopulateDropdowns(view);
                await PopulateAiPanel(view);
                return View(nameof(Index), view);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading create form");
                TempData["Error"] = L10n.T("An error occurred while loading the form");
                return RedirectToAction(nameof(Index), new { tab = QuestionTabs.Create });
            }
        }

        /// <summary>
        /// Backs the compact AI panel on the question pages. Returns only the result fragment so
        /// the question form underneath keeps whatever the lecturer has already typed.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateQuestions(AiQuestionGeneratorViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
                return PartialView("_AiGenerateResults", BuildFailure(model));

            try
            {
                var context = model.UseMaterials
                    ? await _catalog.BuildRagContextAsync(model.TopicId,
                        model.TopicId is null ? model.SubjectId : null,
                        $"{model.Subject} {model.Topic} {model.LearningOutcomes}", cancellationToken)
                    : RagContext.Empty;

                var questions = await _generator.GenerateAsync(
                    new QuestionGenerationRequest(model.Subject.Trim(), model.Topic.Trim(),
                        model.LearningOutcomes?.Trim(), model.Difficulty, model.QuestionCount,
                        context.Text, context.Sources), model.Provider, cancellationToken);

                model.Questions = questions.Select(question => new GeneratedQuestionViewModel
                {
                    Content = question.Content,
                    ExpectedAnswer = question.ExpectedAnswer,
                    BloomLevel = question.BloomLevel,
                    FollowUpQuestions = question.FollowUpQuestions
                }).ToList();
                model.Sources = context.Sources.Select(source => new MaterialExcerptViewModel(source.MaterialId, source.Title)).ToList();
                model.ActiveProvider = _generator.ActiveProvider ?? AiProvider.Gemini;
                return PartialView("_AiGenerateResults", model);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "AI generation was rejected");
                ModelState.AddModelError(string.Empty,
                    _generator.ActiveProvider is null
                        ? L10n.T("The AI question generator is temporarily unavailable. Please try again later.")
                        : L10n.T("Could not generate questions right now. Please try again later."));
                return PartialView("_AiGenerateResults", BuildFailure(model));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI generation failed");
                ModelState.AddModelError(string.Empty, L10n.T("Something went wrong while generating questions. Please try again."));
                return PartialView("_AiGenerateResults", BuildFailure(model));
            }
        }

        private AiQuestionGeneratorViewModel BuildFailure(AiQuestionGeneratorViewModel model)
        {
            model.Questions = [];
            model.Sources = [];
            model.ActiveProvider = _generator.ActiveProvider ?? AiProvider.Gemini;
            return model;
        }

        /// <summary>Supplies the compact AI panel: catalog pickers and which engines are live.</summary>
        private async Task PopulateAiPanel(QuestionBankViewModel view)
        {
            // PopulateDropdowns already read the catalog; reuse it so both never disagree.
            var subjects = view.Subjects;
            var topics = view.Topics;
            view.Ai.Subjects = subjects.Select(subject => new SubjectOption(subject.Id, subject.Name)).ToList();
            view.Ai.Topics = topics.Select(topic => new TopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList();
            view.Ai.AllTopics = view.Ai.Topics;
            view.Ai.GeminiAvailable = _generator.IsProviderAvailable(AiProvider.Gemini);
            view.Ai.OllamaAvailable = _generator.IsProviderAvailable(AiProvider.Ollama);
            view.Ai.ActiveProvider = _generator.ActiveProvider ?? AiProvider.Gemini;
            view.Ai.RubricOptions = view.Rubrics;

            // The compact slide reads the same data from ViewBag.
            ViewBag.AiSubjects = view.Ai.Subjects;
            ViewBag.AiTopics = view.Ai.Topics;
            ViewBag.AiGeminiAvailable = view.Ai.GeminiAvailable;
            ViewBag.AiOllamaAvailable = view.Ai.OllamaAvailable;
            ViewBag.AiProvider = view.Ai.ActiveProvider;

            view.Bulk.Request.Subjects = view.Ai.Subjects;
            view.Bulk.Request.Topics = view.Ai.Topics;
            view.Bulk.Request.AllTopics = view.Ai.Topics;
            view.Bulk.Request.GeminiAvailable = view.Ai.GeminiAvailable;
            view.Bulk.Request.OllamaAvailable = view.Ai.OllamaAvailable;
            view.Bulk.Request.ActiveProvider = view.Ai.ActiveProvider;
            view.Bulk.Request.RubricOptions = view.Rubrics;
            if (view.Bulk.Plan.Count == 0)
                view.Bulk.Plan = BulkGenerationViewModel.DefaultPlan(view.BloomLevels);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Content,Context,BloomLevelId,RubricId,SubjectId,TopicId,ExpectedAnswer,DisplayOrder,IsActive")] QuestionViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var view = new QuestionBankViewModel { ActiveTab = QuestionTabs.Create, Editor = model };
                    await PopulateDropdowns(view);
                    await PopulateAiPanel(view);
                    return View(nameof(Index), view);
                }

                var question = new QuestionDto
                {
                    Content = model.Content,
                    Context = model.Context ?? string.Empty,
                    BloomLevelId = model.BloomLevelId,
                    RubricId = model.RubricId,
                    SubjectId = model.SubjectId,
                    TopicId = model.TopicId,
                    ExpectedAnswer = model.ExpectedAnswer ?? string.Empty,
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive
                };

                var createdQuestion = await _questionService.CreateQuestionAsync(question);
                TempData["Success"] = L10n.Format("Question '{0}...' created successfully", model.Content[..Math.Min(50, model.Content.Length)]);
                return RedirectToAction(nameof(Index), new { tab = QuestionTabs.Bank });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Validation error creating question");
                ModelState.AddModelError("", ex.Message);
                var view = new QuestionBankViewModel { ActiveTab = QuestionTabs.Create, Editor = model };
                await PopulateDropdowns(view);
                await PopulateAiPanel(view);
                return View(nameof(Index), view);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating question");
                ModelState.AddModelError("", L10n.T("An error occurred while creating the question"));
                var view = new QuestionBankViewModel { ActiveTab = QuestionTabs.Create, Editor = model };
                await PopulateDropdowns(view);
                await PopulateAiPanel(view);
                return View(nameof(Index), view);
            }
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            try
            {
                var question = await _questionService.GetQuestionByIdAsync(id.Value);
                if (question == null)
                    return NotFound();

                var viewModel = MapToViewModel(question);
                await PopulateEditDropdowns();
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading edit form");
                return NotFound();
            }
        }

        /// <summary>Fills the ViewBag the standalone edit view reads for its dropdowns.</summary>
        private async Task PopulateEditDropdowns()
        {
            ViewBag.BloomLevels = new SelectList(await _bloomLevelService.GetAllAsync(), "Id", "Name");
            ViewBag.Rubrics = new SelectList(await _rubricService.GetAllRubricsAsync(), "Id", "Name");
            ViewBag.Subjects = (await _catalog.GetSubjectsAsync())
                .Select(subject => new SubjectOption(subject.Id, subject.Name)).ToList();
            ViewBag.Topics = (await _catalog.GetTopicsAsync())
                .Select(topic => new TopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Content,Context,BloomLevelId,RubricId,SubjectId,TopicId,ExpectedAnswer,DisplayOrder,IsActive")] QuestionViewModel model)
        {
            if (id != model.Id)
                return NotFound();

            try
            {
                if (!ModelState.IsValid)
                    return await EditViewAsync(model);

                var question = new QuestionDto
                {
                    Id = model.Id,
                    Content = model.Content,
                    Context = model.Context ?? string.Empty,
                    BloomLevelId = model.BloomLevelId,
                    RubricId = model.RubricId,
                    SubjectId = model.SubjectId,
                    TopicId = model.TopicId,
                    ExpectedAnswer = model.ExpectedAnswer ?? string.Empty,
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive
                };

                await _questionService.UpdateQuestionAsync(question);
                TempData["Success"] = L10n.T("Question updated successfully");
                return RedirectToAction(nameof(Index), new { tab = QuestionTabs.Bank });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Validation error updating question");
                ModelState.AddModelError("", ex.Message);
                return await EditViewAsync(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating question");
                ModelState.AddModelError("", L10n.T("An error occurred while updating the question"));
                return await EditViewAsync(model);
            }
        }

        /// <summary>Re-renders the standalone edit screen with every dropdown repopulated.</summary>
        private async Task<IActionResult> EditViewAsync(QuestionViewModel model)
        {
            await PopulateEditDropdowns();
            return View(model);
        }
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            try
            {
                var question = await _questionService.GetQuestionByIdAsync(id.Value);
                if (question == null)
                    return NotFound();

                var viewModel = MapToViewModel(question);
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading delete confirmation");
                return NotFound();
            }
        }
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _questionService.DeleteQuestionAsync(id);
                TempData["Success"] = L10n.T("Question deleted successfully");
                return RedirectToAction(nameof(Index), new { tab = QuestionTabs.Bank });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Error deleting question");
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index), new { tab = QuestionTabs.Bank });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting question");
                TempData["Error"] = L10n.T("An error occurred while deleting the question");
                return RedirectToAction(nameof(Index), new { tab = QuestionTabs.Bank });
            }
        }
        public async Task<IActionResult> ByBloomLevel(int? id)
        {
            if (id == null)
                return BadRequest();

            try
            {
                var questions = await _questionService.GetQuestionsByBloomLevelAsync(id.Value);
                var blooms = await _bloomLevelService.GetAllAsync();
                var view = new QuestionBankViewModel
                {
                    ActiveTab = QuestionTabs.Bank,
                    BloomLevelId = id.Value,
                    Questions = questions.Select(MapToViewModel).ToList(),
                    BloomLevels = blooms.Select(bloom => new SelectListItemOption(bloom.Id, bloom.Name)).ToList(),
                    Rubrics = (await _rubricService.GetAllRubricsAsync()).Select(rubric => new SelectListItemOption(rubric.Id, rubric.Name)).ToList()
                };
                return View(nameof(Index), view);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting questions by Bloom level");
                TempData["Error"] = L10n.T("An error occurred while retrieving questions");
                return RedirectToAction(nameof(Index), new { tab = QuestionTabs.Bank });
            }
        }

        /// <summary>Generates one reviewable batch for the AI tab.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateReview(AiReviewViewModel model, CancellationToken cancellationToken)
        {
            var view = new QuestionBankViewModel { ActiveTab = QuestionTabs.Ai, Ai = model };

            if (!ModelState.IsValid)
            {
                await PopulateDropdowns(view);
                await PopulateAiPanel(view);
                return View(nameof(Index), view);
            }

            try
            {
                var context = model.UseMaterials
                    ? await _catalog.BuildRagContextAsync(model.TopicId,
                        model.TopicId is null ? model.SubjectId : null,
                        $"{model.Subject} {model.Topic} {model.LearningOutcomes}", cancellationToken)
                    : RagContext.Empty;

                var questions = await _generator.GenerateAsync(
                    new QuestionGenerationRequest(model.Subject.Trim(), model.Topic.Trim(),
                        model.LearningOutcomes?.Trim(), model.Difficulty, model.QuestionCount,
                        context.Text, context.Sources), model.Provider, cancellationToken);

                model.Results = questions.Select(question => new GeneratedQuestionViewModel
                {
                    Content = question.Content,
                    ExpectedAnswer = question.ExpectedAnswer,
                    BloomLevel = question.BloomLevel,
                    FollowUpQuestions = question.FollowUpQuestions
                }).ToList();
                model.Sources = context.Sources.Select(source => new MaterialExcerptViewModel(source.MaterialId, source.Title)).ToList();
                model.HasResult = true;
                model.ActiveProvider = _generator.ActiveProvider ?? AiProvider.Gemini;
                view.Ai = model;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "AI generation was rejected");
                ModelState.AddModelError(string.Empty,
                    _generator.ActiveProvider is null
                        ? L10n.T("The AI question generator is temporarily unavailable. Please try again later.")
                        : L10n.T("Could not generate questions right now. Please try again later."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI generation failed");
                ModelState.AddModelError(string.Empty, L10n.T("Something went wrong while generating questions. Please try again."));
            }

            await PopulateDropdowns(view);
            await PopulateAiPanel(view);
            return View(nameof(Index), view);
        }

        /// <summary>
        /// Builds the batch described by the bulk plan: one generation request per Bloom level,
        /// each honouring the count the lecturer asked for.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateBulk(BulkGenerationViewModel model, CancellationToken cancellationToken)
        {
            var request = model.Request;
            var view = new QuestionBankViewModel { ActiveTab = QuestionTabs.Bulk, Bulk = model };
            var blooms = (await _bloomLevelService.GetAllAsync()).ToList();

            var planned = model.Plan.Where(row => row.Count > 0).ToList();
            var total = planned.Sum(row => row.Count);
            if (total is < 1 or > 30)
                ModelState.AddModelError(string.Empty, L10n.T("A bulk plan must ask for between 1 and 30 questions."));

            if (ModelState.IsValid && planned.Count > 0)
            {
                try
                {
                    var context = request.UseMaterials
                        ? await _catalog.BuildRagContextAsync(request.TopicId,
                            request.TopicId is null ? request.SubjectId : null,
                            $"{request.Subject} {request.Topic} {request.LearningOutcomes}", cancellationToken)
                        : RagContext.Empty;

                    var generated = new List<GeneratedQuestionViewModel>();
                    foreach (var row in planned)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var levelName = blooms.FirstOrDefault(bloom => bloom.Id == row.BloomLevelId)?.Name ?? row.BloomLevelName;
                        var batch = await _generator.GenerateAsync(
                            new QuestionGenerationRequest(request.Subject.Trim(), request.Topic.Trim(),
                                request.LearningOutcomes?.Trim(), request.Difficulty, row.Count,
                                context.Text, context.Sources), request.Provider, cancellationToken);

                        generated.AddRange(batch.Select(question => new GeneratedQuestionViewModel
                        {
                            Content = question.Content,
                            ExpectedAnswer = question.ExpectedAnswer,
                            // The plan asked for this level explicitly, so trust the plan over the model.
                            BloomLevel = levelName,
                            FollowUpQuestions = question.FollowUpQuestions
                        }));
                    }

                    request.Results = generated;
                    request.Sources = context.Sources.Select(source => new MaterialExcerptViewModel(source.MaterialId, source.Title)).ToList();
                    request.HasResult = true;
                    request.ActiveProvider = _generator.ActiveProvider ?? AiProvider.Gemini;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Bulk generation was rejected");
                    ModelState.AddModelError(string.Empty,
                        _generator.ActiveProvider is null
                            ? L10n.T("The AI question generator is temporarily unavailable. Please try again later.")
                            : L10n.T("Could not generate questions right now. Please try again later."));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bulk generation failed");
                    ModelState.AddModelError(string.Empty, L10n.T("Something went wrong while generating questions. Please try again."));
                }
            }

            await PopulateDropdowns(view);
            await PopulateAiPanel(view);
            return View(nameof(Index), view);
        }

        /// <summary>
        /// Saves the ticked rows of a review list. The generated batch travels back in a hidden
        /// field, so a save never depends on the previous request still being around.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InsertReviewed(string returnTab, string? resultJson, List<int>? pick, int? rubricId, int? subjectId, int? topicId, CancellationToken cancellationToken)
        {
            var tab = returnTab == QuestionTabs.Bulk ? QuestionTabs.Bulk : QuestionTabs.Ai;
            var generated = GeneratedQuestionJson.Deserialize(resultJson);
            var chosen = pick ?? [];
            var blooms = (await _bloomLevelService.GetAllAsync()).ToList();
            var levelByName = blooms.ToDictionary(bloom => bloom.Name, bloom => bloom.Id, StringComparer.OrdinalIgnoreCase);

            var subjects = await _catalog.GetSubjectsAsync();
            var topics = await _catalog.GetTopicsAsync();
            var subjectName = subjects.FirstOrDefault(subject => subject.Id == subjectId)?.Name;
            var topicName = topics.FirstOrDefault(topic => topic.Id == topicId)?.Name;

            var saved = 0;
            // Keep the human readable label in step with the ids so the legacy Context search
            // still finds these questions.
            var contextLabel = string.Join(" / ", new[] { subjectName, topicName }.Where(part => !string.IsNullOrWhiteSpace(part)));
            for (var index = 0; index < generated.Count; index++)
            {
                var candidate = generated[index];
                if (!chosen.Contains(index))
                    continue;
                if (string.IsNullOrWhiteSpace(candidate.Content))
                    continue;

                if (!levelByName.TryGetValue(candidate.BloomLevel ?? string.Empty, out var bloomLevelId))
                {
                    ModelState.AddModelError(string.Empty,
                        L10n.Format("Could not match the generated Bloom level '{0}' to a level in the bank.", candidate.BloomLevel));
                    continue;
                }

                try
                {
                    await _questionService.CreateQuestionAsync(new QuestionDto
                    {
                        Content = candidate.Content,
                        ExpectedAnswer = candidate.ExpectedAnswer ?? string.Empty,
                        BloomLevelId = bloomLevelId,
                        // The catalogue picks come from the tab the batch was generated on, so the
                        // saved questions stay reachable by subject and topic.
                        RubricId = rubricId,
                        SubjectId = subjectId,
                        TopicId = topicId,
                        Context = contextLabel,
                        IsActive = true
                    });
                    saved++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving a reviewed question");
                    ModelState.AddModelError(string.Empty, L10n.T("An error occurred while saving a question."));
                }
            }

            if (saved > 0)
                TempData["Success"] = L10n.Format("{0} questions added to the bank", saved);
            else if (chosen.Count == 0)
                TempData["Error"] = L10n.T("Tick at least one question before saving.");

            if (ModelState.IsValid)
                return RedirectToAction(nameof(Index), new { tab = QuestionTabs.Bank });

            var view = new QuestionBankViewModel
            {
                ActiveTab = tab,
                Ai = new AiReviewViewModel { HasResult = true, ResultJson = resultJson },
                Bulk = new BulkGenerationViewModel
                {
                    HasResult = true,
                    Request = new AiReviewViewModel { HasResult = true, ResultJson = resultJson }
                }
            };
            view.Ai.Results = generated.Select(item => item.ToViewModel()).ToList();
            view.Bulk.Request.Results = view.Ai.Results;
            await PopulateDropdowns(view);
            await PopulateAiPanel(view);
            return View(nameof(Index), view);
        }

        private async Task PopulateDropdowns(QuestionBankViewModel view)
        {
            var bloomLevels = await _bloomLevelService.GetAllAsync();
            view.BloomLevels = bloomLevels.Select(bloom => new SelectListItemOption(bloom.Id, bloom.Name)).ToList();

            var rubrics = await _rubricService.GetAllRubricsAsync();
            view.Rubrics = rubrics.Select(rubric => new SelectListItemOption(rubric.Id, rubric.Name)).ToList();

            // Subject and topic power the custom dropdowns on every question screen.
            var subjects = await _catalog.GetSubjectsAsync();
            var topics = await _catalog.GetTopicsAsync();
            view.Subjects = subjects.Select(subject => new SubjectOption(subject.Id, subject.Name)).ToList();
            view.Topics = topics.Select(topic => new TopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList();
        }

        private QuestionViewModel MapToViewModel(QuestionDto question)
        {
            return new QuestionViewModel
            {
                Id = question.Id,
                Content = question.Content,
                Context = question.Context,
                BloomLevelId = question.BloomLevelId,
                BloomLevelName = question.BloomLevelName,
                RubricId = question.RubricId,
                RubricName = question.RubricName,
                SubjectId = question.SubjectId,
                SubjectName = question.SubjectName,
                TopicId = question.TopicId,
                TopicName = question.TopicName,
                ExpectedAnswer = question.ExpectedAnswer,
                DisplayOrder = question.DisplayOrder,
                IsActive = question.IsActive,
                CreatedDate = question.CreatedDate,
                ModifiedDate = question.ModifiedDate
            };
        }
    }
}