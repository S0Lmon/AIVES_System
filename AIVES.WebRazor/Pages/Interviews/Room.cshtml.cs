using System.Security.Claims;
using System.Text.Json;
using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Interviews;

/// <summary>
/// The viva room. The page renders the starting state; everything after that (start, answering,
/// follow-ups) goes over <see cref="InterviewHub"/>. Questions are read aloud from
/// <see cref="OnGetQuestionAudioAsync"/>, synthesised on the server.
/// </summary>
public sealed class RoomModel(IInterviewService interviews, ISpeechToText speech, IAnswerTranscriber answers, ITextToSpeech voice,
    ILogger<RoomModel> logger) : PageModel
{
    public InterviewStateDto State { get; private set; } = null!;
    public bool CanListen { get; private set; }
    public bool CanSpeak { get; private set; }

    public string StateJson => JsonSerializer.Serialize(State, UtcDateTimeConverter.PageOptions);

    private string Email => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            State = await interviews.GetStateAsync(id, Email, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        CanListen = InterviewHub.CanListen(speech, answers);
        CanSpeak = voice.IsAvailable(State.Language);
        return Page();
    }

    /// <summary>GET ?handler=QuestionAudio&amp;turnId=… — the open question as a WAV file.</summary>
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
}
