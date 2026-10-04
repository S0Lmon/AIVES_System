using System.Globalization;
using System.Security.Claims;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using AIVES.BLL.Services.Operations;
using AIVES.BLL.Services.Recordings;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebMVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AIVES.WebMVC.Controllers;

/// <summary>
/// Human-in-the-loop grading: the lecturer reads the transcript, sees the AI's proposal and decides
/// every score. Also class reports, the grade sheet export, recordings and the exam's audit log.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Staff)]
public sealed class GradingController(
    IGradingService grading,
    IExamService exams,
    IRecordingService recordings,
    IAuditService audit,
    IOptions<ReportOptions> reportOptions,
    DisplayTimeZone timeZone,
    ILogger<GradingController> logger) : Controller
{
    private ExamActor Actor => User.ToExamActor();

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new ExamIndexViewModel { Exams = await exams.ListAsync(Actor, cancellationToken), ShowOwner = Actor.IsAdmin });

    [HttpGet]
    public async Task<IActionResult> Exam(int id, CancellationToken cancellationToken)
    {
        var exam = await grading.GetExamAsync(id, Actor, cancellationToken);
        return exam is null ? NotFound() : View(exam);
    }

    [HttpGet]
    public async Task<IActionResult> Candidate(int id, CancellationToken cancellationToken)
    {
        var candidate = await grading.GetCandidateAsync(id, Actor, cancellationToken);
        return candidate is null ? NotFound() : View(candidate);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Candidate(int id, GradeFormModel form, CancellationToken cancellationToken)
    {
        try
        {
            var questions = form.Questions
                .Where(question => !string.IsNullOrWhiteSpace(question.Score))
                .Select(question => new QuestionGradeInput(question.Id, ParseScore(question.Score!), question.Comment))
                .ToList();
            await grading.SaveAsync(id, new CandidateGradeInput(questions, form.Comment, form.Finalize), Actor, cancellationToken);
            TempData["GradeMessage"] = form.Finalize
                ? L10n.T("The grade was confirmed. The student can now see the result.")
                : L10n.T("The draft grade was saved.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            TempData["GradeError"] = ex is FormatException ? L10n.T("Scores must be numbers, for example 7.5.") : ex.Message;
        }
        return RedirectToAction(nameof(Candidate), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, CancellationToken cancellationToken)
    {
        try
        {
            await grading.ReopenAsync(id, Actor, cancellationToken);
            TempData["GradeMessage"] = L10n.T("The grade was reopened. The student no longer sees it until you confirm again.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Candidate), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestAi(int id, CancellationToken cancellationToken)
    {
        try
        {
            await grading.RequestAiGradingAsync(id, Actor, cancellationToken);
            TempData["GradeMessage"] = L10n.T("The AI will grade this viva again within a minute. Your own scores are kept.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            TempData["GradeError"] = ex.Message;
        }
        return RedirectToAction(nameof(Candidate), new { id });
    }

    /// <summary>Streams a decrypted recording to the exam's lecturer or an administrator. Every playback is audited.</summary>
    [HttpGet]
    public async Task<IActionResult> Recording(int id, CancellationToken cancellationToken)
    {
        var recording = await recordings.OpenAsync(id, Actor, cancellationToken);
        if (recording is null)
            return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(recording.Data, recording.ContentType, enableRangeProcessing: true);
    }

    [HttpGet]
    public async Task<IActionResult> Report(int id, CancellationToken cancellationToken)
    {
        var report = await grading.GetReportAsync(id, Actor, cancellationToken);
        return report is null ? NotFound() : View(report);
    }

    /// <summary>The grade sheet in the school's layout, as an Excel workbook.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(int id, CancellationToken cancellationToken)
    {
        var exam = await grading.GetExamAsync(id, Actor, cancellationToken);
        if (exam is null)
            return NotFound();
        var content = GradeSheetBuilder.Build(exam, reportOptions.Value, timeZone.ToLocal);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeSheetExported, Actor.UserId, Actor.Email, id, null,
            L10n.Format("{0} of {1} grades confirmed", exam.Rows.Count(row => row.FinalizedAtUtc is not null), exam.Rows.Count)), cancellationToken);
        logger.LogInformation("User {UserId} exported the grade sheet of exam {ExamId}", Actor.UserId, id);
        var name = $"grades-{Slug(exam.SubjectName)}-{timeZone.ToLocal(exam.StartsAtUtc):yyyyMMdd}-{id}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    [HttpGet]
    public async Task<IActionResult> Audit(int id, CancellationToken cancellationToken)
    {
        var exam = await exams.GetAsync(id, Actor, cancellationToken);
        if (exam is null)
            return NotFound();
        return View("Audit", new AuditViewModel
        {
            Title = exam.Title,
            ExamId = exam.Id,
            Entries = await audit.QueryAsync(new AuditQuery(ExamId: id, Take: 1000), cancellationToken)
        });
    }

    private static decimal ParseScore(string text) =>
        decimal.Parse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string Slug(string text)
    {
        var chars = text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => c == 'đ' || c == 'Đ' ? 'd' : char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "exam" : slug[..Math.Min(40, slug.Length)];
    }
}

public static class ClaimsPrincipalExtensions
{
    public static ExamActor ToExamActor(this ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        user.IsInRole(AppRoles.Admin),
        user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name);
}
