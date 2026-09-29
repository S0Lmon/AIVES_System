using AIVES.WebMVC.Models.Entities;
using AIVES.WebMVC.Models.ViewModels;
using AIVES.WebMVC.Services;
using AIVES.WebMVC.Data.Repositories;
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
        private readonly IRepository<BloomLevel> _bloomLevelRepository;
        private readonly ILogger<QuestionController> _logger;

        public QuestionController(
            IQuestionService questionService,
            IRubricService rubricService,
            IRepository<BloomLevel> bloomLevelRepository,
            ILogger<QuestionController> logger)
        {
            _questionService = questionService;
            _rubricService = rubricService;
            _bloomLevelRepository = bloomLevelRepository;
            _logger = logger;
        }
        public async Task<IActionResult> Index()
        {
            try
            {
                var questions = await _questionService.GetAllQuestionsAsync();
                var viewModels = questions.Select(q => MapToViewModel(q)).ToList();
                return View(viewModels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting questions");
                TempData["Error"] = "An error occurred while retrieving questions";
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
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading create form");
                TempData["Error"] = "An error occurred while loading the form";
                return RedirectToAction("Index");
            }
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
                    return View(model);
                }

                var question = new Question
                {
                    Content = model.Content,
                    Context = model.Context,
                    BloomLevelId = model.BloomLevelId,
                    RubricId = model.RubricId,
                    ExpectedAnswer = model.ExpectedAnswer,
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive
                };

                var createdQuestion = await _questionService.CreateQuestionAsync(question);
                TempData["Success"] = $"Question '{model.Content.Substring(0, Math.Min(50, model.Content.Length))}...' created successfully";
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
                ModelState.AddModelError("", "An error occurred while creating the question");
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

                var question = new Question
                {
                    Id = model.Id,
                    Content = model.Content,
                    Context = model.Context,
                    BloomLevelId = model.BloomLevelId,
                    RubricId = model.RubricId,
                    ExpectedAnswer = model.ExpectedAnswer,
                    DisplayOrder = model.DisplayOrder,
                    IsActive = model.IsActive
                };

                await _questionService.UpdateQuestionAsync(question);
                TempData["Success"] = "Question updated successfully";
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
                ModelState.AddModelError("", "An error occurred while updating the question");
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
                TempData["Success"] = "Question deleted successfully";
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
                TempData["Error"] = "An error occurred while deleting the question";
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
                TempData["Error"] = "An error occurred while retrieving questions";
                return RedirectToAction("Index");
            }
        }

        private async Task PopulateDropdowns()
        {
            var bloomLevels = await _bloomLevelRepository.GetAllAsync();
            ViewBag.BloomLevels = new SelectList(bloomLevels, "Id", "Name");

            var rubrics = await _rubricService.GetAllRubricsAsync();
            ViewBag.Rubrics = new SelectList(rubrics, "Id", "Name");
        }

        private QuestionViewModel MapToViewModel(Question question)
        {
            return new QuestionViewModel
            {
                Id = question.Id,
                Content = question.Content,
                Context = question.Context,
                BloomLevelId = question.BloomLevelId,
                BloomLevelName = question.BloomLevel?.Name,
                RubricId = question.RubricId,
                RubricName = question.Rubric?.Name,
                ExpectedAnswer = question.ExpectedAnswer,
                DisplayOrder = question.DisplayOrder,
                IsActive = question.IsActive,
                CreatedDate = question.CreatedDate,
                ModifiedDate = question.ModifiedDate
            };
        }
    }
}
