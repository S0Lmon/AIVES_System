using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIVES.BLL.Services.Interview;
using AIVES.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

/// <summary>
/// The candidate's AI viva. The page reads questions aloud and recognises speech in the browser;
/// these endpoints only exchange text, so no audio reaches the server.
/// </summary>
[Authorize]
[Route("Interview/{id:int}")]
public sealed class InterviewController(IInterviewService interviews, ILogger<InterviewController> logger) : Controller
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new UtcDateTimeConverter() }
    };

    /// <summary>
    /// Times come back from SQL Server with an unspecified kind but are stored in UTC. Writing them
    /// with a trailing "Z" stops the browser from reading them as local time and skewing the timer.
    /// </summary>
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTime().ToUniversalTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
    }

    private string Email => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;

    [HttpGet("")]
    public async Task<IActionResult> Index(int id, CancellationToken cancellationToken)
    {
        try
        {
            return View(await interviews.GetStateAsync(id, Email, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("State")]
    public Task<IActionResult> State(int id, CancellationToken cancellationToken) =>
        RunAsync(() => interviews.GetStateAsync(id, Email, cancellationToken));

    [HttpPost("Start"), ValidateAntiForgeryToken]
    public Task<IActionResult> Start(int id, CancellationToken cancellationToken) =>
        RunAsync(() => interviews.StartAsync(id, Email, cancellationToken));

    [HttpPost("Answer"), ValidateAntiForgeryToken]
    public Task<IActionResult> Answer(int id, [FromBody] AnswerRequest request, CancellationToken cancellationToken) =>
        RunAsync(() => interviews.AnswerAsync(id, Email, new InterviewAnswerInput(request.TurnId, request.Transcript ?? string.Empty, request.InputMode), cancellationToken));

    public sealed record AnswerRequest(int TurnId, string? Transcript, AnswerInputMode InputMode);

    private async Task<IActionResult> RunAsync(Func<Task<InterviewStateDto>> action)
    {
        try
        {
            return Json(await action(), JsonOptions);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Interview request failed for candidate slot {CandidateId}", RouteData.Values["id"]);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = DTO.Localization.L10n.T("Something went wrong. Please try again.") });
        }
    }
}
