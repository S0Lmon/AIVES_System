using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebRazor.Pages.Interviews;

/// <summary>The open question as synthesized audio, served to the viva page.</summary>
public static class QuestionAudio
{
    public static async Task<IActionResult> ServeAsync(HttpResponse response, IInterviewService interviews, ITextToSpeech voice,
        ILogger logger, int candidateId, string email, int turnId, CancellationToken cancellationToken,
        string? adminUserId = null, bool isAdmin = false)
    {
        InterviewStateDto state;
        try
        {
            state = await interviews.GetStateAsync(candidateId, email, cancellationToken, adminUserId, isAdmin);
        }
        catch (KeyNotFoundException)
        {
            return new NotFoundResult();
        }
        // Only the question being asked right now, so the page cannot be used to read ahead.
        var preferredVoice = state.Speech?.VoiceFor(state.Language);
        if (state.CurrentTurn is not { } turn || turn.TurnId != turnId || !voice.IsAvailable(state.Language, preferredVoice))
            return new NotFoundResult();

        try
        {
            var audio = await voice.SynthesizeAsync(turn.QuestionText, state.Language, state.Speech?.SpeechRate ?? 1,
                preferredVoice, cancellationToken: cancellationToken);
            response.Headers.CacheControl = "private, no-store";
            return new FileContentResult(audio.Data, audio.ContentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not read turn {TurnId} aloud", turnId);
            return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
