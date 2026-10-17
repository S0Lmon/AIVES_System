namespace AIVES.BLL.Services.Realtime;

public static class LiveEntities
{
    public const string Question = "question";
    public const string Subject = "subject";
    public const string Topic = "topic";
    public const string Exam = "exam";
}

public static class LiveActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
    public const string Ended = "ended";
}

/// <summary>Application event sent by the presentation layer to connected clients.</summary>
public sealed record EntityChange(string Entity, string Action, int Id, string Title, string ActorId, string ActorName, DateTime AtUtc);

/// <summary>Publishes application changes through the presentation layer's real-time transport.</summary>
public interface ILiveUpdates
{
    Task EntityChangedAsync(string entity, string action, int id, string title, string? actorId = null, string? actorName = null);
}
