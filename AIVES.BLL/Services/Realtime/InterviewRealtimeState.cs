using System.Collections.Concurrent;

namespace AIVES.BLL.Services.Realtime;

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
