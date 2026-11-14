using AIVES.DTO;

namespace AIVES.BLL.Services.Interview;

/// <summary>
/// Runs a candidate's AI viva: asks the assigned main questions in order, decides after each answer
/// whether to probe with a follow-up, and records every exchange. Candidates are restricted to their
/// own email and slot; administrator test copies are restricted to their owning administrator.
/// </summary>
public interface IInterviewService
{
    Task<InterviewStateDto> GetStateAsync(int candidateId, string email, CancellationToken cancellationToken = default,
        string? adminUserId = null, bool isAdmin = false);
    /// <summary>Starts the viva. A recorded exam needs <paramref name="recordingConsent"/>; only an administrator's own test copy bypasses its slot.</summary>
    Task<InterviewStateDto> StartAsync(int candidateId, string email, bool recordingConsent = false, CancellationToken cancellationToken = default,
        string? adminUserId = null, bool isAdmin = false);

    /// <summary>
    /// Records the answer to the current question and returns the next step. A submission for any
    /// other turn (a repeat, or a stale tab) changes nothing and just returns the current state.
    /// </summary>
    Task<InterviewStateDto> AnswerAsync(int candidateId, string email, InterviewAnswerInput input, CancellationToken cancellationToken = default,
        string? adminUserId = null, bool isAdmin = false);
}
