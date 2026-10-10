using System.Globalization;
using System.Security.Claims;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using AIVES.BLL.Services.Operations;
using AIVES.BLL.Services.Recordings;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Pages.Exams;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace AIVES.WebRazor.Pages.Grading;

public sealed class GradingIndexModel(IExamService exams) : PageModel
{
    public IReadOnlyList<ExamSummaryDto> Exams { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Exams = await exams.ListAsync(ExamPageActors.From(User), cancellationToken);
}

public sealed class ExamModel(IGradingService grading) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    public ExamGradingDto Exam { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var exam = await grading.GetExamAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (exam is null) return NotFound();
        Exam = exam;
        return Page();
    }
}

public sealed class GradeQuestionInput
{
    public int Id { get; set; }
    public string? Score { get; set; }
    public string? Comment { get; set; }
}

public sealed class GradeFormInput
{
    public List<GradeQuestionInput> Questions { get; set; } = [];
    public string? Comment { get; set; }
    public bool Finalize { get; set; }
}

public sealed class CandidateModel(
    IGradingService grading,
    IRecordingService recordings,
    DisplayTimeZone timeZone) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public GradeFormInput Form { get; set; } = new();
    public CandidateGradingDto Candidate { get; private set; } = null!;
    public DisplayTimeZone TimeZone { get; } = timeZone;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var questions = Form.Questions.Where(question => !string.IsNullOrWhiteSpace(question.Score))
                .Select(question => new QuestionGradeInput(question.Id, ParseScore(question.Score!), question.Comment)).ToList();
            await grading.SaveAsync(Id, new CandidateGradeInput(questions, Form.Comment, Form.Finalize), ExamPageActors.From(User), cancellationToken);
            TempData["GradeMessage"] = Form.Finalize ? L10n.T("The grade was confirmed. The student can now see the result.") : L10n.T("The draft grade was saved.");
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            TempData["GradeError"] = ex is FormatException ? L10n.T("Scores must be numbers, for example 7.5.") : ex.Message;
        }
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostReopenAsync(CancellationToken cancellationToken)
    {
        try { await grading.ReopenAsync(Id, ExamPageActors.From(User), cancellationToken); TempData["GradeMessage"] = L10n.T("The grade was reopened. The student no longer sees it until you confirm again."); }
        catch (KeyNotFoundException) { return NotFound(); }
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostRequestAiAsync(CancellationToken cancellationToken)
    {
        try { await grading.RequestAiGradingAsync(Id, ExamPageActors.From(User), cancellationToken); TempData["GradeMessage"] = L10n.T("The AI will grade this viva again within a minute. Your own scores are kept."); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { TempData["GradeError"] = ex.Message; }
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnGetRecordingAsync(int recordingId, CancellationToken cancellationToken)
    {
        var recording = await recordings.OpenAsync(recordingId, ExamPageActors.From(User), cancellationToken);
        if (recording is null) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new FileContentResult(recording.Data, recording.ContentType) { EnableRangeProcessing = true };
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var candidate = await grading.GetCandidateAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (candidate is null) return false;
        Candidate = candidate;
        Form = new GradeFormInput
        {
            Comment = candidate.LecturerComment,
            Questions = candidate.Questions.Select(question => new GradeQuestionInput
            {
                Id = question.Id,
                Score = (question.LecturerScore ?? question.AiScore)?.ToString("0.##", CultureInfo.InvariantCulture),
                Comment = question.LecturerComment
            }).ToList()
        };
        return true;
    }

    private static decimal ParseScore(string text) =>
        decimal.Parse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture);
}

public sealed class AuditModel(
    IExamService exams,
    IAuditService audit,
    DisplayTimeZone timeZone) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    public string Title { get; private set; } = string.Empty;
    public IReadOnlyList<AuditEntryDto> Entries { get; private set; } = [];
    public DisplayTimeZone TimeZone { get; } = timeZone;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (exam is null) return NotFound();
        Title = exam.Title;
        Entries = await audit.QueryAsync(new AuditQuery(ExamId: Id, Take: 1000), cancellationToken);
        return Page();
    }
}

public abstract class GradeExportPage(IGradingService grading, IAuditService audit, IOptions<ReportOptions> options, DisplayTimeZone timeZone, ILogger logger) : PageModel
{
    protected async Task<IActionResult> ExportAsync(int id, CancellationToken cancellationToken)
    {
        var exam = await grading.GetExamAsync(id, ExamPageActors.From(User), cancellationToken);
        if (exam is null) return NotFound();
        var content = GradeSheetBuilder.Build(exam, options.Value, timeZone.ToLocal);
        var actor = ExamPageActors.From(User);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeSheetExported, actor.UserId, actor.Email, id, null,
            L10n.Format("{0} of {1} grades confirmed", exam.Rows.Count(row => row.FinalizedAtUtc is not null), exam.Rows.Count)), cancellationToken);
        logger.LogInformation("User {UserId} exported the grade sheet of exam {ExamId}", actor.UserId, id);
        var slug = string.Join('-', new string(exam.SubjectName.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => c == 'đ' || c == 'Đ' ? 'd' : char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '-').ToArray())
            .Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length == 0) slug = "exam";
        slug = slug[..Math.Min(40, slug.Length)];
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"grades-{slug}-{timeZone.ToLocal(exam.StartsAtUtc):yyyyMMdd}-{id}.xlsx");
    }
}

public sealed class ExportModel(IGradingService grading, IAuditService audit, IOptions<ReportOptions> options, DisplayTimeZone timeZone, ILogger<ExportModel> logger)
    : GradeExportPage(grading, audit, options, timeZone, logger)
{
    public Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken) => ExportAsync(id, cancellationToken);
}

public sealed class ReportAliasModel : PageModel
{
    public IActionResult OnGet(int id) => RedirectToPage("/Reports/Exam", new { id });
}
