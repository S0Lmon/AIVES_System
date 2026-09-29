using AIVES.WebMVC.Models.ViewModels;
using AIVES.WebMVC.Services.Gemini;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AIVES.WebMVC.Controllers;

[Authorize]
public sealed class AiExamRoomController : Controller
{
    private readonly IGeminiQuestionGenerator _generator;
    private readonly ILogger<AiExamRoomController> _logger;

    public AiExamRoomController(IGeminiQuestionGenerator generator, ILogger<AiExamRoomController> logger)
    {
        _generator = generator;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult QuestionGenerator()
    {
        return View(new AiQuestionGeneratorViewModel { IsGeminiConfigured = _generator.IsConfigured });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuestionGenerator(AiQuestionGeneratorViewModel model, CancellationToken cancellationToken)
    {
        model.IsGeminiConfigured = _generator.IsConfigured;
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var questions = await _generator.GenerateAsync(model.Subject, model.Topic, model.LearningOutcomes, model.Difficulty, model.QuestionCount, cancellationToken);
            model.Questions = questions.Select(question => new GeneratedQuestionViewModel
            {
                Content = question.Content,
                ExpectedAnswer = question.ExpectedAnswer,
                BloomLevel = question.BloomLevel,
                FollowUpQuestions = question.FollowUpQuestions
            }).ToList();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while generating viva questions");
            ModelState.AddModelError(string.Empty, "Đã có lỗi khi tạo câu hỏi. Vui lòng thử lại.");
        }

        return View(model);
    }
}
