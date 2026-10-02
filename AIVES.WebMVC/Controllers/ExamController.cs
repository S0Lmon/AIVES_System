using System.Security.Claims;
using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Exams;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebMVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>Viva exam sessions for lecturers: schedule, candidates and per-candidate question sets.</summary>
[Authorize(Policy = AuthorizationPolicies.Staff)]
public sealed class ExamController(IExamService exams, ICatalogService catalog, DisplayTimeZone timeZone, ILogger<ExamController> logger) : Controller
{
    private ExamActor Actor => new(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, User.IsInRole(AppRoles.Admin));

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new ExamIndexViewModel { Exams = await exams.ListAsync(Actor, cancellationToken), ShowOwner = Actor.IsAdmin });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        // Default to the next full hour so the form opens with a valid, future start.
        var now = timeZone.ToLocal(DateTime.UtcNow);
        var model = new ExamFormViewModel { StartsAtLocal = now.Date.AddHours(now.Hour + 1) };
        return View("Form", await FillAsync(model, cancellationToken));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ExamFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Form", await FillAsync(model, cancellationToken));
        try
        {
            var result = await exams.CreateAsync(ToInput(model), Actor, cancellationToken);
            Report(result, L10n.T("The exam was created and questions were assigned to every candidate."));
            return RedirectToAction(nameof(Details), new { id = result.ExamId });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Form", await FillAsync(model, cancellationToken));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var exam = await exams.GetAsync(id, Actor, cancellationToken);
        return exam is null ? NotFound() : View(exam);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var exam = await exams.GetAsync(id, Actor, cancellationToken);
        if (exam is null)
            return NotFound();
        if (exam.HasStarted(DateTime.UtcNow))
        {
            TempData["ExamError"] = L10n.T("This exam has already started, so its settings, candidates and questions are locked.");
            return RedirectToAction(nameof(Details), new { id });
        }
        var model = new ExamFormViewModel
        {
            Id = exam.Id,
            Title = exam.Title,
            SubjectId = exam.SubjectId ?? 0,
            TopicId = exam.TopicId,
            StartsAtLocal = timeZone.ToLocal(exam.StartsAtUtc),
            SlotMinutes = exam.SlotMinutes,
            MainQuestionCount = exam.MainQuestionCount,
            MaxFollowUpQuestions = exam.MaxFollowUpQuestions,
            CandidateEmails = string.Join(Environment.NewLine, exam.Candidates.Select(candidate => candidate.Email))
        };
        return View("Form", await FillAsync(model, cancellationToken));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ExamFormViewModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        if (!ModelState.IsValid)
            return View("Form", await FillAsync(model, cancellationToken));
        try
        {
            var result = await exams.UpdateAsync(id, ToInput(model), Actor, cancellationToken);
            Report(result, L10n.T("The exam was saved and questions were assigned again."));
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Form", await FillAsync(model, cancellationToken));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reassign(int id, CancellationToken cancellationToken)
    {
        try
        {
            Report(await exams.ReassignQuestionsAsync(id, Actor, cancellationToken), L10n.T("New questions were drawn for every candidate."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["ExamError"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await exams.DeleteAsync(id, Actor, cancellationToken);
            TempData["ExamMessage"] = L10n.T("The exam was deleted.");
            return RedirectToAction(nameof(Index));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            TempData["ExamError"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    private void Report(ExamSaveResult result, string message)
    {
        TempData["ExamMessage"] = message;
        if (result.ConsecutiveOverlaps > 0)
        {
            logger.LogInformation("Exam {ExamId} has {Overlaps} questions shared by consecutive candidates", result.ExamId, result.ConsecutiveOverlaps);
            TempData["ExamWarning"] = L10n.Format("The question pool is small: consecutive candidates share {0} question(s). Add questions to the subject or lower the number of main questions, then draw again.", result.ConsecutiveOverlaps);
        }
    }

    private ExamInput ToInput(ExamFormViewModel model) => new(
        model.Title,
        model.SubjectId,
        model.TopicId,
        timeZone.ToUtc(model.StartsAtLocal!.Value),
        model.SlotMinutes,
        model.MainQuestionCount,
        model.MaxFollowUpQuestions,
        exams.ParseCandidateEmails(model.CandidateEmails));

    private async Task<ExamFormViewModel> FillAsync(ExamFormViewModel model, CancellationToken cancellationToken)
    {
        model.Subjects = (await catalog.GetSubjectsAsync(cancellationToken)).Select(subject => new SubjectOption(subject.Id, subject.Name)).ToList();
        model.Topics = (await catalog.GetTopicsAsync(cancellationToken: cancellationToken)).Select(topic => new TopicOption(topic.Id, topic.SubjectId, topic.Name)).ToList();
        model.TimeZoneLabel = timeZone.OffsetLabel;
        return model;
    }
}
