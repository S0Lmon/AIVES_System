using AIVES.BLL.Services.Exams;
using AIVES.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace AIVES.WebRazor.Realtime;

/// <summary>One step of a live viva, as the monitoring lecturer sees it.</summary>
public sealed record InterviewEvent(int ExamId, int CandidateId, string Candidate, string Kind, string Text, int? TurnId, DateTime AtUtc);

public static class InterviewEventKinds
{
    public const string Started = "started";
    public const string Question = "question";
    public const string FollowUp = "followup";
    public const string Answer = "answer";
    public const string Completed = "completed";
}

public interface IInterviewClient
{
    Task InterviewEvent(InterviewEvent change);
}

/// <summary>
/// The monitoring feed of the AI viva. Candidates take the viva through the Interview page; each step
/// it records is pushed here, and lecturers watching the exam get every one.
/// </summary>
[Authorize]
public sealed class InterviewHub(IExamService exams) : Hub<IInterviewClient>
{
    public const string Path = "/hubs/interview";

    public static string ExamGroup(int examId) => $"exam-{examId}";

    private string Email => Context.User?.FindFirstValue(ClaimTypes.Email) ?? Context.User?.Identity?.Name ?? string.Empty;

    /// <summary>Only the exam's lecturer or an administrator may observe candidates' transcripts.</summary>
    public async Task WatchExam(int examId)
    {
        var user = Context.User;
        if (AivesHub.IsStaff(user))
        {
            var actor = new ExamActor(AivesHub.UserId(user), user!.IsInRole(AppRoles.Admin), Email);
            if (await exams.GetAsync(examId, actor, Context.ConnectionAborted) is null)
                throw new HubException("Not allowed.");
            await Groups.AddToGroupAsync(Context.ConnectionId, ExamGroup(examId));
            return;
        }

        // Exam groups include other candidates' questions and answers. Being listed as
        // a candidate must never grant access to this monitoring feed.
        throw new HubException("Not allowed.");
    }
}
