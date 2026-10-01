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
        public async Task<IActionResult> Index()
        {
            try
            {
                var questions = await _questionService.GetAllQuestionsAsync();
                var viewModels = questions.Select(q => MapToViewModel(q)).ToList();
                await PopulateAiPanel();
                return View(viewModels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting questions");
                TempData["Error"] = L10n.T("An error occurred while retrieving questions");
                return RedirectToAction("Index", "Home");
            }
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
        public async Task<IActionResult> Create()
        {
            try
            {
                await PopulateDropdowns();
                await PopulateAiPanel();
                return View(new QuestionViewModel());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading create form");
                TempData["Error"] = L10n.T("An error occurred while loading the form");
                return RedirectToAction("Index");
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
        private async Task PopulateAiPanel()
        {
            var subjects = await _catalog.GetSubjectsAsync();
            var topics = await _catalog.GetTopicsAsync();
            ViewBag.AiSubjects = subjects.Select(subject => new SubjectOption(subject.Id, subject.Name)).ToList();
            ViewBag.AiTopics = topics.Select(topic => new TopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList();
            ViewBag.AiGeminiAvailable = _generator.IsProviderAvailable(AiProvider.Gemini);
            ViewBag.AiOllamaAvailable = _generator.IsProviderAvailable(AiProvider.Ollama);
            ViewBag.AiProvider = _generator.ActiveProvider ?? AiProvider.Gemini;
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Content,Context,BloomLevelId,RubricId,ExpectedAnswer,DisplayOrder,IsActive")] QuestionViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    await PopulateDropdowns();
                    await PopulateAiPanel();
                    return View(model);
                }

                var question = new QuestionDto
                {
                    Content = model.Content,
                    Context = model.Context ?? string.Empty,
                    BloomLevelId = model.BloomLevelId,
                    RubricId = model.RubricId,
                    ExpectedAnswer = model.ExpectedAnswer ?? string.Empty,
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive
                };

                var createdQuestion = await _questionService.CreateQuestionAsync(question);
                TempData["Success"] = L10n.Format("Question '{0}...' created successfully", model.Content[..Math.Min(50, model.Content.Length)]);
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Validation error creating question");
                ModelState.AddModelError("", ex.Message);
                await PopulateDropdowns();
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating question");
                ModelState.AddModelError("", L10n.T("An error occurred while creating the question"));
                await PopulateDropdowns();
                return View(model);
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
                await PopulateDropdowns();
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading edit form");
                return NotFound();
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Content,Context,BloomLevelId,RubricId,ExpectedAnswer,DisplayOrder,IsActive")] QuestionViewModel model)
        {
            if (id != model.Id)
                return NotFound();

            try
            {
                if (!ModelState.IsValid)
                {
                    await PopulateDropdowns();
                    return View(model);
                }

                var question = new QuestionDto
                {
                    Id = model.Id,
                    Content = model.Content,
                    Context = model.Context ?? string.Empty,
                    BloomLevelId = model.BloomLevelId,
                    RubricId = model.RubricId,
                    ExpectedAnswer = model.ExpectedAnswer ?? string.Empty,
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive
                };

                await _questionService.UpdateQuestionAsync(question);
                TempData["Success"] = L10n.T("Question updated successfully");
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Validation error updating question");
                ModelState.AddModelError("", ex.Message);
                await PopulateDropdowns();
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating question");
                ModelState.AddModelError("", L10n.T("An error occurred while updating the question"));
                await PopulateDropdowns();
                return View(model);
            }
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
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Error deleting question");
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting question");
                TempData["Error"] = L10n.T("An error occurred while deleting the question");
                return RedirectToAction(nameof(Index));
            }
        }
        public async Task<IActionResult> ByBloomLevel(int? id)
        {
            if (id == null)
                return BadRequest();

            try
            {
                var questions = await _questionService.GetQuestionsByBloomLevelAsync(id.Value);
                var viewModels = questions.Select(q => MapToViewModel(q)).ToList();
                ViewBag.BloomLevelId = id;
                return View("Index", viewModels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting questions by Bloom level");
                TempData["Error"] = L10n.T("An error occurred while retrieving questions");
                return RedirectToAction("Index");
            }
        }

        private async Task PopulateDropdowns()
        {
            var bloomLevels = await _bloomLevelService.GetAllAsync();
            ViewBag.BloomLevels = new SelectList(bloomLevels, "Id", "Name");

            var rubrics = await _rubricService.GetAllRubricsAsync();
            ViewBag.Rubrics = new SelectList(rubrics, "Id", "Name");
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
                ExpectedAnswer = question.ExpectedAnswer,
                DisplayOrder = question.DisplayOrder,
                IsActive = question.IsActive,
                CreatedDate = question.CreatedDate,
                ModifiedDate = question.ModifiedDate
            };
        }
    }
}
