using AIVES.WebMVC.Models.ViewModels;
using AIVES.BLL.Services.Gemini;
using AIVES.DTO.Localization;
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
    public IActionResult QuestionGenerator() => View(new AiQuestionGeneratorViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuestionGenerator(AiQuestionGeneratorViewModel model, CancellationToken cancellationToken)
    {
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
            _logger.LogWarning(ex, "Gemini question generation was rejected");
            ModelState.AddModelError(string.Empty, _generator.IsConfigured
                ? L10n.T("Could not generate questions right now. Please try again later.")
                : L10n.T("The AI question generator is temporarily unavailable. Please try again later."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while generating viva questions");
            ModelState.AddModelError(string.Empty, L10n.T("Something went wrong while generating questions. Please try again."));
        }

        return View(model);
    }
}
