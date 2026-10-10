using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Recordings;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace AIVES.WebRazor.Realtime;

/// <summary>One step of a live viva, as the monitoring lecturer sees it.</summary>
public sealed record InterviewEvent(int ExamId, int CandidateId, string Candidate, string Kind, string Text, int? TurnId, DateTime AtUtc);

public static class InterviewEventKinds
{
    public const string Started = "started";
    public const string Question = "question";
    public const string FollowUp = "followup";
    public const string Partial = "partial";
    public const string Answer = "answer";
    public const string Completed = "completed";
}

public interface IInterviewClient
{
    /// <summary>The candidate's own words so far; <paramref name="isFinal"/> once they stopped speaking.</summary>
    Task Transcript(int turnId, string text, bool isFinal);

    Task InterviewEvent(InterviewEvent change);
}

/// <summary>Audio and text of the answer a connection is giving right now.</summary>
public sealed class AnswerSessions
{
    public sealed record Session(int CandidateId, int TurnId, string FinalText, float[] Audio, int SpeakingMs);

    private readonly ConcurrentDictionary<string, Session> sessions = new();

    public void Set(string connectionId, Session session) => sessions[connectionId] = session;

    public Session? Take(string connectionId, int candidateId, int turnId) =>
        sessions.TryRemove(connectionId, out var session) && session.CandidateId == candidateId && session.TurnId == turnId ? session : null;

    public void Forget(string connectionId) => sessions.TryRemove(connectionId, out _);
}

/// <summary>
/// The AI viva over SignalR. The candidate streams microphone audio (16 kHz PCM, base64 chunks) and
/// gets their transcript back while speaking; Whisper runs on the server through the BLL. Submitting
/// hands the text to <see cref="IInterviewService"/>, which enforces the time limit and the follow-up
/// limits and asks the AI whether to probe further. Lecturers watching the exam get every step.
/// </summary>
[Authorize]
public sealed class InterviewHub(
    IInterviewService interviews,
    IExamService exams,
    IRecordingService recordings,
    ISpeechToText speech,
    IAnswerTranscriber answers,
    AnswerSessions sessions,
    IOptions<SpeechOptions> speechOptions,
    IOptions<InterviewOptions> interviewOptions,
    ILogger<InterviewHub> logger) : Hub<IInterviewClient>
{
    public const string Path = "/hubs/interview";

    public static string ExamGroup(int examId) => $"exam-{examId}";

    private string Email => Context.User?.FindFirstValue(ClaimTypes.Email) ?? Context.User?.Identity?.Name ?? string.Empty;

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        sessions.Forget(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task<InterviewStateDto> GetState(int candidateId) =>
        Guard(() => interviews.GetStateAsync(candidateId, Email, Context.ConnectionAborted));

    public async Task<InterviewStateDto> Start(int candidateId, bool recordingConsent)
    {
        var before = await Guard(() => interviews.GetStateAsync(candidateId, Email, Context.ConnectionAborted));
        if (before.Recording == RecordingMode.AudioVideo)
            throw new HubException("Bài thi này yêu cầu ghi hình. Vui lòng làm bài trên trang AIVES chính.");

        var state = await Guard(() => interviews.StartAsync(candidateId, Email, recordingConsent, Context.ConnectionAborted));
        if (before.Status == InterviewStatus.NotStarted && state.Status != InterviewStatus.NotStarted)
        {
            await NotifyAsync(candidateId, InterviewEventKinds.Started, "Bắt đầu vấn đáp", null);
            await NotifyQuestionAsync(candidateId, state);
        }
        return state;
    }

    /// <summary>
    /// Client-to-server stream of one answer. Chunks are base64 little-endian 16-bit PCM at 16 kHz.
    /// The transcript is pushed back as it grows and once more, final, when the stream ends.
    /// </summary>
    public async Task StreamAnswer(int candidateId, int turnId, IAsyncEnumerable<string> chunks)
    {
        if (!CanListen(speech, answers))
            throw new HubException("Nhận dạng giọng nói chưa được cài đặt trên máy chủ. Vui lòng gõ câu trả lời.");

        var state = await Guard(() => interviews.GetStateAsync(candidateId, Email, Context.ConnectionAborted));
        var turn = state.CurrentTurn;
        if (state.Status != InterviewStatus.InProgress || turn is null || turn.TurnId != turnId)
            throw new HubException("Câu hỏi này không còn mở.");

        var maxSeconds = turn.TimeLimitSeconds + Math.Max(0, interviewOptions.Value.AnswerGraceSeconds);
        // Without a Whisper model there is no live preview, but Gemini can still write the transcript.
        var preview = speech.IsAvailable ? speech : NoPreviewSpeechToText.Instance;
        var transcriber = new LiveTranscriber(preview, speechOptions.Value, state.Language, Prompt(turn, state.Phrases), maxSeconds);
        var aborted = Context.ConnectionAborted;
        var examId = await ExamIdAsync(candidateId);

        await foreach (var chunk in chunks.WithCancellation(aborted))
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(chunk);
            }
            catch (FormatException)
            {
                throw new HubException("Dữ liệu âm thanh không hợp lệ.");
            }
            if (transcriber.IsFull)
                continue;
            transcriber.Append(AIVES.BLL.Services.Speech.Audio.FromPcm16(bytes));

            var text = await transcriber.UpdateAsync(aborted);
            if (text is null)
                continue;
            await Clients.Caller.Transcript(turnId, text, false);
            if (examId is { } id)
                await Clients.Group(ExamGroup(id)).InterviewEvent(Event(id, candidateId, InterviewEventKinds.Partial, text, turnId));
        }

        var audio = transcriber.Samples.ToArray();
        // Gemini reads the whole answer while Whisper finishes its last window; the candidate sees
        // Whisper's text first and Gemini's replaces it if it arrives in time.
        var remote = answers.TranscribeAnswerAsync(audio, state.Language, Vocabulary(state.Phrases), aborted);
        var final = await transcriber.FinishAsync(aborted);
        if (answers.IsEnabled)
        {
            if (final.Length > 0)
                await Clients.Caller.Transcript(turnId, final, false);
            var better = await remote;
            logger.LogInformation("Turn {TurnId} transcript from {Engine}", turnId, better is null ? "Whisper" : "Gemini");
            final = better ?? final;
        }
        sessions.Set(Context.ConnectionId, new AnswerSessions.Session(candidateId, turnId, final, audio, SpeakingMs(audio)));
        await Clients.Caller.Transcript(turnId, final, true);
        if (examId is { } exam)
            await Clients.Group(ExamGroup(exam)).InterviewEvent(Event(exam, candidateId, InterviewEventKinds.Partial, final, turnId));
    }

    /// <summary>
    /// Submits the answer to the current question. Text the candidate typed or changed after
    /// recognition is recorded as typed, so the lecturer can tell the two apart.
    /// </summary>
    public async Task<InterviewStateDto> SubmitAnswer(int candidateId, int turnId, string? transcript, int? responseDelayMs)
    {
        var session = sessions.Take(Context.ConnectionId, candidateId, turnId);
        var text = (transcript ?? string.Empty).Trim();
        var spoken = session is not null && string.Equals(Normalize(session.FinalText), Normalize(text), StringComparison.Ordinal);
        var input = new InterviewAnswerInput(turnId, text, spoken ? AnswerInputMode.Speech : AnswerInputMode.Typed,
            responseDelayMs, session?.SpeakingMs);

        var before = await Guard(() => interviews.GetStateAsync(candidateId, Email, Context.ConnectionAborted));
        var state = await Guard(() => interviews.AnswerAsync(candidateId, Email, input, Context.ConnectionAborted));
        if (before.CurrentTurn?.TurnId != turnId)
            return state;

        if (session is { Audio.Length: > 0 } && state.Recording == RecordingMode.Audio && state.RecordingConsentGiven)
            await SaveRecordingAsync(candidateId, turnId, session.Audio);

        await NotifyAsync(candidateId, InterviewEventKinds.Answer, text.Length == 0 ? "(không trả lời)" : text, turnId);
        if (state.Status == InterviewStatus.Completed)
            await NotifyAsync(candidateId, InterviewEventKinds.Completed, "Hoàn thành vấn đáp", null);
        else
            await NotifyQuestionAsync(candidateId, state);
        return state;
    }

    /// <summary>Lecturers join an exam's group to watch its vivas live.</summary>
    public async Task WatchExam(int examId)
    {
        var user = Context.User;
        if (!AivesHub.IsStaff(user))
            throw new HubException("Not allowed.");
        var actor = new ExamActor(AivesHub.UserId(user), user!.IsInRole(AppRoles.Admin), Email);
        if (await exams.GetAsync(examId, actor, Context.ConnectionAborted) is null)
            throw new HubException("Not allowed.");
        await Groups.AddToGroupAsync(Context.ConnectionId, ExamGroup(examId));
    }

    private async Task SaveRecordingAsync(int candidateId, int turnId, float[] audio)
    {
        try
        {
            using var wav = new MemoryStream(AIVES.BLL.Services.Speech.Audio.ToWav(audio, AIVES.BLL.Services.Speech.Audio.SampleRate));
            await recordings.SaveAsync(candidateId, Email, turnId, wav, "audio/wav", Context.ConnectionAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The answer itself is saved; a lost recording must not fail the viva.
            logger.LogWarning(ex, "Could not save the recording of turn {TurnId} for candidate {CandidateId}", turnId, candidateId);
        }
    }

    private async Task NotifyQuestionAsync(int candidateId, InterviewStateDto state)
    {
        if (state.CurrentTurn is not { } turn)
            return;
        var kind = turn.Kind == TurnKind.FollowUp ? InterviewEventKinds.FollowUp : InterviewEventKinds.Question;
        await NotifyAsync(candidateId, kind, turn.QuestionText, turn.TurnId);
    }

    private async Task NotifyAsync(int candidateId, string kind, string text, int? turnId)
    {
        if (await ExamIdAsync(candidateId) is { } examId)
            await Clients.Group(ExamGroup(examId)).InterviewEvent(Event(examId, candidateId, kind, text, turnId));
    }

    private InterviewEvent Event(int examId, int candidateId, string kind, string text, int? turnId) =>
        new(examId, candidateId, Email, kind, text, turnId, DateTime.UtcNow);

    /// <summary>The exam a candidate slot belongs to, cached for the connection.</summary>
    private async Task<int?> ExamIdAsync(int candidateId)
    {
        var key = $"exam-of-{candidateId}";
        if (Context.Items.TryGetValue(key, out var cached))
            return (int?)cached;
        var exam = (await exams.ListForCandidateAsync(Email, Context.ConnectionAborted)).FirstOrDefault(item => item.CandidateId == candidateId);
        Context.Items[key] = exam?.ExamId;
        return exam?.ExamId;
    }

    /// <summary>The question and the subject's terms, so Whisper spells them the way the course does.</summary>
    public static string Prompt(InterviewTurnDto turn, IReadOnlyList<string>? phrases)
    {
        var prompt = turn.QuestionText;
        if (phrases is { Count: > 0 })
            prompt += " " + string.Join(", ", phrases.Take(30));
        return prompt.Length <= 400 ? prompt : prompt[..400];
    }

    /// <summary>
    /// Terms for Gemini's custom vocabulary: only the subject's glossary, which the lecturer curates.
    /// Terms taken from the question were tried on 2026-10-08 and pulled the transcript towards them
    /// ("qua WebSocket" came back as "qua ASP.NET Core"), which would put the right words in a
    /// candidate's mouth.
    /// </summary>
    public static IReadOnlyList<string> Vocabulary(IReadOnlyList<string>? glossary) =>
        (glossary ?? []).Where(term => term.Trim().Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Speech can be taken when either Whisper is installed or Gemini writes transcripts.</summary>
    public static bool CanListen(ISpeechToText speech, IAnswerTranscriber answers) => speech.IsAvailable || answers.IsEnabled;

    private static int SpeakingMs(float[] audio)
    {
        const int frame = AIVES.BLL.Services.Speech.Audio.SampleRate * 30 / 1000;
        var loud = 0;
        for (var start = 0; start + frame <= audio.Length; start += frame)
        {
            if (AIVES.BLL.Services.Speech.Audio.Rms(audio.AsSpan(start, frame)) >= 0.01f)
                loud++;
        }
        return loud * 30;
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Turns the BLL's exceptions into messages the page can show.</summary>
    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException)
        {
            throw new HubException("Không tìm thấy lượt thi này.");
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(L10n.T(ex.Message));
        }
    }
}
