using AIVES.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace AIVES.WebRazor.Realtime;

/// <summary>A signed-in user as shown in the "online now" list.</summary>
public sealed record OnlineUser(string UserId, string Name, int Connections);

/// <summary>
/// One change to the question bank or catalogue. Pages refresh the affected list; the
/// layout shows a toast and the dashboard adds it to the activity feed.
/// </summary>
public sealed record EntityChange(string Entity, string Action, int Id, string Title, string ActorId, string ActorName, DateTime AtUtc);

/// <summary>Messages the server pushes to browsers. Method names are what realtime.js listens for.</summary>
public interface IAivesClient
{
    Task PresenceChanged(IReadOnlyList<OnlineUser> users);
    Task EntityChanged(EntityChange change);
    Task EditorsChanged(int questionId, IReadOnlyList<string> editors);
}

/// <summary>
/// The single SignalR hub. Everyone signed in gets presence; only staff join the group that
/// receives question/catalogue changes, because question content must not reach students.
/// </summary>
[Authorize]
public sealed class AivesHub(PresenceTracker presence) : Hub<IAivesClient>
{
    public const string Path = "/hubs/aives";
    public const string StaffGroup = "staff";

    public static string QuestionGroup(int questionId) => $"question-{questionId}";

    public override async Task OnConnectedAsync()
    {
        if (IsStaff(Context.User))
            await Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup);

        presence.Connect(Context.ConnectionId, UserId(Context.User), DisplayName(Context.User));
        await Clients.All.PresenceChanged(presence.OnlineUsers());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var leftQuestions = presence.Disconnect(Context.ConnectionId);
        foreach (var questionId in leftQuestions)
            await Clients.Group(QuestionGroup(questionId)).EditorsChanged(questionId, presence.Editors(questionId));
        await Clients.All.PresenceChanged(presence.OnlineUsers());
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Called by the edit page so other editors of the same question see each other.</summary>
    public async Task JoinQuestion(int questionId)
    {
        if (!IsStaff(Context.User) || questionId <= 0)
            throw new HubException("Not allowed.");

        await Groups.AddToGroupAsync(Context.ConnectionId, QuestionGroup(questionId));
        presence.StartEditing(Context.ConnectionId, questionId);
        await Clients.Group(QuestionGroup(questionId)).EditorsChanged(questionId, presence.Editors(questionId));
    }

    public async Task LeaveQuestion(int questionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, QuestionGroup(questionId));
        if (presence.StopEditing(Context.ConnectionId, questionId))
            await Clients.Group(QuestionGroup(questionId)).EditorsChanged(questionId, presence.Editors(questionId));
    }

    public static bool IsStaff(ClaimsPrincipal? user) =>
        user is not null && (user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Lecturer));

    public static string UserId(ClaimsPrincipal? user) =>
        user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    public static string DisplayName(ClaimsPrincipal? user) =>
        user?.Identity?.Name is { Length: > 0 } name ? name : "?";
}
