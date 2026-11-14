using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace AIVES.WebRazor.Realtime;

public interface ISpeechRecognitionClient
{
    Task RecognitionResult(int turnId, SpeechRecognitionUpdate update);
}

[Authorize]
public sealed class SpeechRecognitionHub(
    IInterviewService interviews,
    IVoskSpeechRecognition recognition,
    IHubContext<SpeechRecognitionHub, ISpeechRecognitionClient> hubContext,
    ILogger<VoskSpeechRecognitionSession> sessionLogger,
    ILogger<SpeechRecognitionHub> logger) : Hub<ISpeechRecognitionClient>
{
    public const string Path = "/hubs/speech-recognition";
    private static readonly ConcurrentDictionary<string, VoskSpeechRecognitionSession> Sessions = new();

    private string Email => Context.User?.FindFirstValue(ClaimTypes.Email) ?? Context.User?.Identity?.Name ?? string.Empty;
    private string? UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin => Context.User?.IsInRole(AppRoles.Admin) == true;

    public async Task StartRecognition(int candidateId, int turnId)
    {
        var connectionId = Context.ConnectionId;
        if (Sessions.ContainsKey(connectionId))
            throw new HubException("Speech recognition is already active.");

        InterviewStateDto state;
        try
        {
            state = await interviews.GetStateAsync(candidateId, Email, Context.ConnectionAborted, UserId, IsAdmin);
        }
        catch (KeyNotFoundException)
        {
            throw new HubException("The interview was not found.");
        }
        if (state.Status != InterviewStatus.InProgress || state.CurrentTurn?.TurnId != turnId)
            throw new HubException("This question is no longer open.");

        if (!recognition.IsAvailable(state.Language))
            throw new HubException($"The local Vosk speech model for {state.Language} is not installed.");

        var session = recognition.CreateSession(
            state.Language,
            update => hubContext.Clients.Client(connectionId).RecognitionResult(turnId, update),
            sessionLogger);
        if (!Sessions.TryAdd(connectionId, session))
        {
            await session.DisposeAsync();
            throw new HubException("Speech recognition is already active.");
        }
    }

    public async Task StartAdminTestRecognition(AppLanguage language)
    {
        var connectionId = Context.ConnectionId;
        if (!IsAdmin)
            throw new HubException("Only administrators can run the speech sandbox.");
        if (language is not (AppLanguage.Vi or AppLanguage.En))
            throw new HubException("Choose Vietnamese or English.");
        if (Sessions.ContainsKey(connectionId))
            throw new HubException("Speech recognition is already active.");
        if (!recognition.IsAvailable(language))
            throw new HubException($"The local Vosk speech model for {language} is not installed.");

        var session = recognition.CreateSession(
            language,
            update => hubContext.Clients.Client(connectionId).RecognitionResult(0, update),
            sessionLogger);
        if (!Sessions.TryAdd(connectionId, session))
        {
            await session.DisposeAsync();
            throw new HubException("Speech recognition is already active.");
        }
    }

    public Task SendAudio(byte[] pcm)
    {
        if (!Sessions.TryGetValue(Context.ConnectionId, out var session))
            throw new HubException("Speech recognition has not started.");
        try
        {
            session.WriteAudio(pcm);
            return Task.CompletedTask;
        }
        catch (ArgumentException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task StopRecognition()
    {
        if (Sessions.TryRemove(Context.ConnectionId, out var session))
            await session.DisposeAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Sessions.TryRemove(Context.ConnectionId, out var session))
            await session.DisposeAsync();
        if (exception is not null)
            logger.LogDebug(exception, "Speech recognition connection ended");
        await base.OnDisconnectedAsync(exception);
    }
}
