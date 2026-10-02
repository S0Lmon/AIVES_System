using AIVES.DTO;

namespace AIVES.BLL.Services.Interview;

/// <summary>
/// Runs a candidate's AI viva: asks the assigned main questions in order, decides after each answer
/// whether to probe with a follow-up, and records every exchange. Only the candidate whose email is on
/// the slot can see or drive it (<see cref="KeyNotFoundException"/> otherwise), and only during the
/// slot (<see cref="InvalidOperationException"/> when starting outside it).
/// </summary>
public interface IInterviewService
{
    Task<InterviewStateDto> GetStateAsync(int candidateId, string email, CancellationToken cancellationToken = default);
    Task<InterviewStateDto> StartAsync(int candidateId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the answer to the current question and returns the next step. A submission for any
    /// other turn (a repeat, or a stale tab) changes nothing and just returns the current state.
    /// </summary>
    Task<InterviewStateDto> AnswerAsync(int candidateId, string email, InterviewAnswerInput input, CancellationToken cancellationToken = default);
}
