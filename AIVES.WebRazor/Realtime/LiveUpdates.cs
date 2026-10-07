using Microsoft.AspNetCore.SignalR;

namespace AIVES.WebRazor.Realtime;

public static class LiveEntities
{
    public const string Question = "question";
    public const string Subject = "subject";
    public const string Topic = "topic";
}

public static class LiveActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
}

/// <summary>
/// Page models call this after the BLL has saved a change. Broadcasting is a presentation
/// concern, so the BLL stays unaware of SignalR.
/// </summary>
public interface ILiveUpdates
{
    Task EntityChangedAsync(string entity, string action, int id, string title);
}

public sealed class LiveUpdates(IHubContext<AivesHub, IAivesClient> hub, IHttpContextAccessor http,
    TimeProvider clock, ILogger<LiveUpdates> logger) : ILiveUpdates
{
    private const int MaxTitleLength = 80;

    public async Task EntityChangedAsync(string entity, string action, int id, string title)
    {
        var user = http.HttpContext?.User;
        var change = new EntityChange(entity, action, id, Shorten(title),
            AivesHub.UserId(user), AivesHub.DisplayName(user), clock.GetUtcNow().UtcDateTime);
        try
        {
            await hub.Clients.Group(AivesHub.StaffGroup).EntityChanged(change);
        }
        catch (Exception ex)
        {
            // The change is already saved; a failed broadcast must not turn it into an error page.
            logger.LogWarning(ex, "Could not broadcast {Entity} {Action} {Id}", entity, action, id);
        }
    }

    private static string Shorten(string title)
    {
        var text = title.Trim().ReplaceLineEndings(" ");
        return text.Length <= MaxTitleLength ? text : text[..(MaxTitleLength - 1)] + "…";
    }
}
