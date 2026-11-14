using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using AIVES.BLL.Services.Operations;
using AIVES.BLL.Services.Recordings;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Pages.Exams;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AIVES.WebRazor.Pages.Grading;

public sealed class GradeQuestionInput
{
    public int Id
    {
        get; set;
    }
    public string? Score
    {
        get; set;
    }
    public string? Comment
    {
        get; set;
    }
}
public sealed class GradeFormInput
{
    public List<GradeQuestionInput> Questions { get; set; } = [];
    public string? Comment
    {
        get; set;
    }
    public bool Finalize
    {
        get; set;
    }
}
public sealed class CandidateModel(
    IGradingService grading,
    IRecordingService recordings,
    DisplayTimeZone timeZone,
    ILiveUpdates liveUpdates,
    IGradingWorkSignal gradingWorkSignal,
    IOptions<GradingOptions> gradingOptions) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }
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
            if (Form.Finalize)
            {
                var candidate = await grading.GetCandidateAsync(Id, ExamPageActors.From(User), cancellationToken);
                await liveUpdates.ExamChangedAsync(LiveActions.Updated, candidate?.ExamId ?? Id, candidate?.ExamTitle ?? string.Empty,
                    candidate is null ? [] : [candidate.Email]);
            }
            TempData["GradeMessage"] = Form.Finalize ? L10n.T("The grade was confirmed. The student can now see the result.") : L10n.T("The draft grade was saved.");
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            TempData["GradeError"] = ex is FormatException ? L10n.T("Scores must be numbers, for example 7.5.") : ex.Message;
        }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnPostReopenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await grading.ReopenAsync(Id, ExamPageActors.From(User), cancellationToken);
            TempData["GradeMessage"] = L10n.T("The grade was reopened. The student no longer sees it until you confirm again.");
        }
        catch (KeyNotFoundException) { return NotFound(); }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnPostRequestAiAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!gradingOptions.Value.WorkerEnabled)
            {
                TempData["GradeError"] = L10n.T("AI grading is disabled on this server.");
                return RedirectToPage(new { id = Id });
            }
            await grading.RequestAiGradingAsync(Id, ExamPageActors.From(User), cancellationToken);
            gradingWorkSignal.Signal();
            TempData["GradeMessage"] = L10n.T("AI grading has been queued and will start shortly. Reload this page to see the result.");
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { TempData["GradeError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnGetRecordingAsync(int recordingId, CancellationToken cancellationToken)
    {
        var recording = await recordings.OpenAsync(recordingId, ExamPageActors.From(User), cancellationToken);
        if (recording is null)
            return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new FileContentResult(recording.Data, recording.ContentType) { EnableRangeProcessing = true };
    }

    /// <summary>GET ?handler=SessionRecording&amp;segment=… — one segment of the whole-viva recording.</summary>
    public async Task<IActionResult> OnGetSessionRecordingAsync(long segment, CancellationToken cancellationToken)
    {
        // A player asks for the file again in ranges while seeking; log the playback once, at its start.
        var range = Request.Headers.Range.ToString();
        var logPlayback = string.IsNullOrEmpty(range) || range.StartsWith("bytes=0-", StringComparison.Ordinal);
        var recording = await recordings.OpenSessionAsync(Id, segment, ExamPageActors.From(User), logPlayback, cancellationToken);
        if (recording is null)
            return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new FileContentResult(recording.Data, recording.ContentType) { EnableRangeProcessing = true };
    }

    public IReadOnlyList<SessionSegmentDto> SessionSegments { get; private set; } = [];

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var candidate = await grading.GetCandidateAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (candidate is null)
            return false;
        Candidate = candidate;
        SessionSegments = await recordings.ListSessionAsync(Id, ExamPageActors.From(User), cancellationToken);
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
