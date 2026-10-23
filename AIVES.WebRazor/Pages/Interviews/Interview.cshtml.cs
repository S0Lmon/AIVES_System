using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Recordings;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIVES.WebRazor.Pages.Interviews;

[RequestSizeLimit(32 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 32 * 1024 * 1024)]
public sealed class InterviewApiModel(
    IInterviewService interviews,
    IRecordingService recordings,
    IExamService exams,
    ITextToSpeech voice,
    IHubContext<InterviewHub, IInterviewClient> monitor,
    ILogger<InterviewApiModel> logger) : PageModel
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new UtcDateTimeConverter() }
    };

    public InterviewStateDto State { get; private set; } = null!;
    private string Email => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            State = await interviews.GetStateAsync(id, Email, cancellationToken);
            return Page();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    public Task<IActionResult> OnGetStateAsync(int id, CancellationToken cancellationToken) =>
        RunAsync(() => interviews.GetStateAsync(id, Email, cancellationToken));

    public async Task<IActionResult> OnPostStartAsync(int id, [FromBody] StartRequest? request, CancellationToken cancellationToken)
    {
        InterviewStatus? before = null;
        return await RunAsync(async () =>
        {
            before = (await interviews.GetStateAsync(id, Email, cancellationToken)).Status;
            var state = await interviews.StartAsync(id, Email, request?.RecordingConsent ?? false, cancellationToken);
            if (before == InterviewStatus.NotStarted && state.Status != InterviewStatus.NotStarted)
            {
                await NotifyAsync(id, InterviewEventKinds.Started, L10n.T("Started the viva"), null, cancellationToken);
                await NotifyQuestionAsync(id, state, cancellationToken);
            }
            return state;
        });
    }

    public Task<IActionResult> OnPostAnswerAsync(int id, [FromBody] AnswerRequest request, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var before = await interviews.GetStateAsync(id, Email, cancellationToken);
            var state = await interviews.AnswerAsync(id, Email, new InterviewAnswerInput(
                request.TurnId, request.Transcript ?? string.Empty, request.InputMode,
                request.ResponseDelayMs, request.SpeakingMs), cancellationToken);
            if (before.CurrentTurn?.TurnId != request.TurnId)
                return state;
            var text = (request.Transcript ?? string.Empty).Trim();
            await NotifyAsync(id, InterviewEventKinds.Answer, text.Length == 0 ? L10n.T("(no answer)") : text, request.TurnId, cancellationToken);
            if (state.Status == InterviewStatus.Completed)
                await NotifyAsync(id, InterviewEventKinds.Completed, L10n.T("Finished the viva"), null, cancellationToken);
            else
                await NotifyQuestionAsync(id, state, cancellationToken);
            return state;
        });

    /// <summary>GET /Interview/{id}/QuestionAudio/{turnId} — the open question as a WAV file.</summary>
    public async Task<IActionResult> OnGetQuestionAudioAsync(int id, int turnId, CancellationToken cancellationToken)
    {
        InterviewStateDto state;
        try
        {
            state = await interviews.GetStateAsync(id, Email, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        // Only the question being asked right now, so the page cannot be used to read ahead.
        if (state.CurrentTurn is not { } turn || turn.TurnId != turnId || !voice.IsAvailable(state.Language))
            return NotFound();

        try
        {
            var wav = await voice.SynthesizeWavAsync(turn.QuestionText, state.Language, state.Speech?.SpeechRate ?? 1, cancellationToken);
            Response.Headers.CacheControl = "private, no-store";
            return File(wav, "audio/wav");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not read turn {TurnId} aloud", turnId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    public async Task<IActionResult> OnPostRecordingAsync(int id, int turnId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new
            {
                error = L10n.T("The recording is empty.")
            });
        try
        {
            await using var stream = file.OpenReadStream();
            await recordings.SaveAsync(id, Email, turnId, stream, file.ContentType ?? string.Empty, cancellationToken);
            return StatusCode(StatusCodes.Status204NoContent);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { error = ex.Message }); }
    }

    public sealed record StartRequest(bool RecordingConsent);
    public sealed record AnswerRequest(int TurnId, string? Transcript, AnswerInputMode InputMode, int? ResponseDelayMs = null, int? SpeakingMs = null);

    private async Task NotifyQuestionAsync(int candidateId, InterviewStateDto state, CancellationToken cancellationToken)
    {
        if (state.CurrentTurn is not { } turn)
            return;
        var kind = turn.Kind == TurnKind.FollowUp ? InterviewEventKinds.FollowUp : InterviewEventKinds.Question;
        await NotifyAsync(candidateId, kind, turn.QuestionText, turn.TurnId, cancellationToken);
    }

    /// <summary>Tells lecturers watching the exam; a failed notification never fails the viva.</summary>
    private async Task NotifyAsync(int candidateId, string kind, string text, int? turnId, CancellationToken cancellationToken)
    {
        try
        {
            var exam = (await exams.ListForCandidateAsync(Email, cancellationToken)).FirstOrDefault(item => item.CandidateId == candidateId);
            if (exam is not null)
                await monitor.Clients.Group(InterviewHub.ExamGroup(exam.ExamId))
                    .InterviewEvent(new InterviewEvent(exam.ExamId, candidateId, Email, kind, text, turnId, DateTime.UtcNow));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not tell the monitor about {Kind} for candidate slot {CandidateId}", kind, candidateId);
        }
    }

    private async Task<IActionResult> RunAsync(Func<Task<InterviewStateDto>> action)
    {
        try
        {
            return new JsonResult(await action(), JsonOptions);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(StatusCodes.Status409Conflict, new { error = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Interview request failed for candidate slot {CandidateId}", RouteData.Values["id"]);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new
                {
                    error = L10n.T("Something went wrong. Please try again.")
                });
        }
    }
}
