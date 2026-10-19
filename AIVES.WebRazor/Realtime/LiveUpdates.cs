using AIVES.DTO;
using Microsoft.AspNetCore.SignalR;

namespace AIVES.WebRazor.Realtime;

public static class LiveEntities
{
    public const string Question = "question";
    public const string Subject = "subject";
    public const string Topic = "topic";
    public const string Exam = "exam";
}

public static class ExamLive
{
    /// <summary>The candidate emails of an exam, for <see cref="ILiveUpdates.ExamChangedAsync"/>.</summary>
    public static IEnumerable<string> Emails(ExamDetailsDto? exam) =>
        exam?.Candidates.Select(candidate => candidate.Email) ?? [];
}

public static class LiveActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
    public const string Ended = "ended";
}

/// <summary>
/// Page models call this after the BLL has saved a change. Broadcasting is a presentation
/// concern, so the BLL stays unaware of SignalR.
/// </summary>
public interface ILiveUpdates
{
    Task EntityChangedAsync(string entity, string action, int id, string title);
    /// <summary>Tells staff and the exam's candidates (old and new, on an edit) that an exam changed.</summary>
    Task ExamChangedAsync(string action, int examId, string title, IEnumerable<string> candidateEmails);
}

public sealed class LiveUpdates(IHubContext<AivesHub, IAivesClient> hub, IHttpContextAccessor http,
    TimeProvider clock, ILogger<LiveUpdates> logger) : ILiveUpdates
{
    private const int MaxTitleLength = 80;

    public async Task EntityChangedAsync(string entity, string action, int id, string title)
    {
        var change = Change(entity, action, id, title);
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

    public async Task ExamChangedAsync(string action, int examId, string title, IEnumerable<string> candidateEmails)
    {
        var change = Change(LiveEntities.Exam, action, examId, title);
        try
        {
            await hub.Clients.Group(AivesHub.StaffGroup).EntityChanged(change);
            var groups = candidateEmails.Where(email => !string.IsNullOrWhiteSpace(email))
                .Select(AivesHub.CandidateGroup).Distinct().ToList();
            if (groups.Count > 0)
                await hub.Clients.Groups(groups).EntityChanged(change);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not broadcast exam {Action} {Id}", action, examId);
        }
    }

    private EntityChange Change(string entity, string action, int id, string title)
    {
        var user = http.HttpContext?.User;
        return new EntityChange(entity, action, id, Shorten(title),
            AivesHub.UserId(user), AivesHub.DisplayName(user), clock.GetUtcNow().UtcDateTime);
    }

    private static string Shorten(string title)
    {
        var text = title.Trim().ReplaceLineEndings(" ");
        return text.Length <= MaxTitleLength ? text : text[..(MaxTitleLength - 1)] + "…";
    }
}
