using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Recordings;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Interviews;

public sealed class InterviewApiModel(
    IInterviewService interviews,
    IRecordingService recordings,
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
        try { State = await interviews.GetStateAsync(id, Email, cancellationToken); return Page(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    public Task<IActionResult> OnGetStateAsync(int id, CancellationToken cancellationToken) =>
        RunAsync(() => interviews.GetStateAsync(id, Email, cancellationToken));

    public Task<IActionResult> OnPostStartAsync(int id, [FromBody] StartRequest? request, CancellationToken cancellationToken) =>
        RunAsync(() => interviews.StartAsync(id, Email, request?.RecordingConsent ?? false, cancellationToken));

    public Task<IActionResult> OnPostAnswerAsync(int id, [FromBody] AnswerRequest request, CancellationToken cancellationToken) =>
        RunAsync(() => interviews.AnswerAsync(id, Email, new InterviewAnswerInput(
            request.TurnId, request.Transcript ?? string.Empty, request.InputMode,
            request.ResponseDelayMs, request.SpeakingMs), cancellationToken));

    [RequestSizeLimit(32 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 32 * 1024 * 1024)]
    public async Task<IActionResult> OnPostRecordingAsync(int id, int turnId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = L10n.T("The recording is empty.") });
        try
        {
            await using var stream = file.OpenReadStream();
            await recordings.SaveAsync(id, Email, turnId, stream, file.ContentType ?? string.Empty, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { error = ex.Message }); }
    }

    public sealed record StartRequest(bool RecordingConsent);
    public sealed record AnswerRequest(int TurnId, string? Transcript, AnswerInputMode InputMode, int? ResponseDelayMs = null, int? SpeakingMs = null);

    private async Task<IActionResult> RunAsync(Func<Task<InterviewStateDto>> action)
    {
        try { return new JsonResult(await action(), JsonOptions); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Interview request failed for candidate slot {CandidateId}", RouteData.Values["id"]);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = L10n.T("Something went wrong. Please try again.") });
        }
    }
}
