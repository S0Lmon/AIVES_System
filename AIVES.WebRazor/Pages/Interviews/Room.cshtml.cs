using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Text.Json;

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
    public bool CanListen
    {
        get; private set;
    }
    public bool CanSpeak
    {
        get; private set;
    }

    public string StateJson => JsonSerializer.Serialize(State, UtcDateTimeConverter.PageOptions);

    private string Email => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? string.Empty;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            State = await interviews.GetStateAsync(id, Email, cancellationToken);
            // The browser room supports MediaRecorder with camera capture; do not leave
            // audio/video candidates on a page that has no Start button.
            if (State.Recording == RecordingMode.AudioVideo)
                return RedirectToPage("Interview", new { id });
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
    public Task<IActionResult> OnGetQuestionAudioAsync(int id, int turnId, CancellationToken cancellationToken) =>
        QuestionAudio.ServeAsync(Response, interviews, voice, logger, id, Email, turnId, cancellationToken);
}
