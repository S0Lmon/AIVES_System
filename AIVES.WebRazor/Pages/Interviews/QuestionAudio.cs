using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebRazor.Pages.Interviews;

/// <summary>The open question as a WAV file, shared by the viva page and the Whisper room.</summary>
public static class QuestionAudio
{
    public static async Task<IActionResult> ServeAsync(HttpResponse response, IInterviewService interviews, ITextToSpeech voice,
        ILogger logger, int candidateId, string email, int turnId, CancellationToken cancellationToken)
    {
        InterviewStateDto state;
        try
        {
            state = await interviews.GetStateAsync(candidateId, email, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return new NotFoundResult();
        }
        // Only the question being asked right now, so the page cannot be used to read ahead.
        if (state.CurrentTurn is not { } turn || turn.TurnId != turnId || !voice.IsAvailable(state.Language))
            return new NotFoundResult();

        try
        {
            var wav = await voice.SynthesizeWavAsync(turn.QuestionText, state.Language, state.Speech?.SpeechRate ?? 1, cancellationToken);
            response.Headers.CacheControl = "private, no-store";
            return new FileContentResult(wav, "audio/wav");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not read turn {TurnId} aloud", turnId);
            return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
