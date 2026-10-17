using AIVES.BLL.Services.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace AIVES.WebRazor.Realtime;

public sealed class LiveUpdates(IHubContext<AivesHub, IAivesClient> hub, IHttpContextAccessor http,
    TimeProvider clock, ILogger<LiveUpdates> logger) : ILiveUpdates
{
    private const int MaxTitleLength = 80;

    public async Task EntityChangedAsync(string entity, string action, int id, string title, string? actorId = null, string? actorName = null)
    {
        var user = http.HttpContext?.User;
        var change = new EntityChange(entity, action, id, Shorten(title),
            actorId ?? AivesHub.UserId(user), actorName ?? AivesHub.DisplayName(user), clock.GetUtcNow().UtcDateTime);
        try
        {
            if (entity == LiveEntities.Exam)
                await hub.Clients.All.EntityChanged(change);
            else
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
