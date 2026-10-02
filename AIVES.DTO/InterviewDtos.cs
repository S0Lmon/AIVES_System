namespace AIVES.DTO;

public enum InterviewStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2
}

/// <summary>Why the examiner moved on or asked more. Stored with each answer for the record.</summary>
public enum FollowUpReason
{
    /// <summary>The answer covers the expected points; move on.</summary>
    Sufficient = 0,
    /// <summary>The answer is too general or unclear.</summary>
    Vague = 1,
    /// <summary>Key points of the expected answer are missing.</summary>
    Missing = 2,
    /// <summary>The answer contradicts itself or the expected answer.</summary>
    Contradiction = 3,
    /// <summary>The answer does not address the question.</summary>
    OffTopic = 4,
    /// <summary>No follow-up was allowed: the per-question or per-candidate limit was reached.</summary>
    LimitReached = 5,
    /// <summary>Nothing was said or typed.</summary>
    NoAnswer = 6,
    /// <summary>The AI could not be reached in time; the interview carried on without a follow-up.</summary>
    AiUnavailable = 7
}

public enum TurnKind
{
    Main = 0,
    FollowUp = 1
}

/// <summary>How the candidate gave an answer: speech recognised in the browser, or typed.</summary>
public enum AnswerInputMode
{
    Speech = 0,
    Typed = 1
}

/// <summary>The question the candidate should answer now.</summary>
public sealed record InterviewTurnDto(
    int TurnId,
    TurnKind Kind,
    string QuestionText,
    int MainIndex,
    int MainCount,
    int FollowUpIndex,
    DateTime AskedAtUtc,
    int TimeLimitSeconds);

/// <summary>Everything the interview page needs to render the current step.</summary>
public sealed record InterviewStateDto(
    int CandidateId,
    string ExamTitle,
    AppLanguage Language,
    InterviewStatus Status,
    DateTime SlotStartsAtUtc,
    DateTime SlotEndsAtUtc,
    InterviewTurnDto? CurrentTurn,
    DateTime ServerNowUtc = default);

public sealed record InterviewAnswerInput(int TurnId, string Transcript, AnswerInputMode InputMode);

/// <summary>What the follow-up generator gets: one main question and the exchange so far about it.</summary>
public sealed record FollowUpRequest(
    AppLanguage Language,
    string SubjectName,
    string MainQuestion,
    string ExpectedAnswer,
    IReadOnlyList<InterviewExchange> Exchanges,
    int FollowUpsLeft);

public sealed record InterviewExchange(string Question, string Answer);

public sealed record FollowUpDecision(bool NeedsFollowUp, FollowUpReason Reason, string? FollowUpQuestion);

/// <summary>A stored question/answer pair, for the lecturer's view of an interview.</summary>
public sealed record InterviewTurnRecordDto(
    int Order,
    TurnKind Kind,
    int MainIndex,
    string QuestionText,
    string? Answer,
    AnswerInputMode? InputMode,
    DateTime AskedAtUtc,
    DateTime? AnsweredAtUtc,
    bool TimedOut,
    FollowUpReason? Decision,
    int TurnId = 0,
    int FollowUpIndex = 0);

public sealed record InterviewRecordDto(
    InterviewStatus Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<InterviewTurnRecordDto> Turns);

/// <summary>An exam slot as the interview needs it: settings, the candidate's questions and progress so far.</summary>
public sealed record InterviewContext(
    int CandidateId,
    string ExamTitle,
    string SubjectName,
    string CandidateEmail,
    DateTime SlotStartsAtUtc,
    DateTime SlotEndsAtUtc,
    int AnswerTimeLimitSeconds,
    int MaxFollowUpsPerQuestion,
    int MaxFollowUpQuestions,
    AppLanguage Language,
    IReadOnlyList<ExamAssignedQuestion> Questions,
    int? AttemptId,
    InterviewRecordDto? Record);

/// <summary>A question about to be asked, before it has an id.</summary>
public sealed record NewInterviewTurn(TurnKind Kind, int MainIndex, int FollowUpIndex, string QuestionText, DateTime AskedAtUtc);
