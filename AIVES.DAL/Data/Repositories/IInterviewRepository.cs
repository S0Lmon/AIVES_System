using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface IInterviewRepository
{
    Task<InterviewContext?> GetContextAsync(int candidateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the attempt with its first question. Returns false when an attempt already exists
    /// (for example a second tab started at the same moment); the caller then reads the existing one.
    /// </summary>
    Task<bool> StartAsync(int candidateId, DateTime startedAtUtc, NewInterviewTurn firstTurn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the answer to a turn. Returns false when the turn was already answered, so a repeated
    /// or concurrent submission can never overwrite the first one.
    /// </summary>
    Task<bool> SaveAnswerAsync(int turnId, string transcript, AnswerInputMode? inputMode, DateTime answeredAtUtc, bool timedOut, CancellationToken cancellationToken = default);

    /// <summary>Records the examiner's decision on an answered turn and either asks the next question or completes the attempt.</summary>
    Task AdvanceAsync(int turnId, FollowUpReason decision, NewInterviewTurn? nextTurn, DateTime nowUtc, CancellationToken cancellationToken = default);
}
