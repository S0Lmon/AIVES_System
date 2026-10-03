using AIVES.DTO;

namespace AIVES.DAL.Entities
{
    /// <summary>A candidate's interview: at most one per candidate, resumed if the page is reloaded.</summary>
    public class ExamAttempt
    {
        public int Id { get; set; }
        public int ExamCandidateId { get; set; }
        public InterviewStatus Status { get; set; } = InterviewStatus.InProgress;
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        /// <summary>When the candidate agreed to being recorded; null when nothing is recorded.</summary>
        public DateTime? RecordingConsentAtUtc { get; set; }
        public GradingStatus GradingStatus { get; set; } = GradingStatus.Pending;
        /// <summary>Set by the grading worker that is working on this attempt; also its concurrency token.</summary>
        public DateTime? GradingClaimedAtUtc { get; set; }
        public string? GradingError { get; set; }
        /// <summary>Set when the lecturer confirms the grade; the student sees the result from then on.</summary>
        public DateTime? FinalizedAtUtc { get; set; }
        public string? FinalizedById { get; set; }
        /// <summary>Final grade on a 10-point scale, computed from the lecturer's question scores.</summary>
        public decimal? FinalScore { get; set; }
        public string? LecturerComment { get; set; }
        public virtual ExamCandidate Candidate { get; set; } = null!;
        public virtual ICollection<ExamTurn> Turns { get; set; } = new List<ExamTurn>();
    }

    /// <summary>
    /// One question put to the candidate (a main question or a follow-up) and the answer given.
    /// The question text is stored as asked; <see cref="Decision"/> records what the examiner made
    /// of the answer (move on, or which gap the next follow-up targets).
    /// </summary>
    public class ExamTurn
    {
        public int Id { get; set; }
        public int ExamAttemptId { get; set; }
        public int Order { get; set; }
        public TurnKind Kind { get; set; }
        /// <summary>1-based position of the main question this turn belongs to.</summary>
        public int MainIndex { get; set; }
        /// <summary>0 for the main question, 1.. for its follow-ups.</summary>
        public int FollowUpIndex { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public DateTime AskedAtUtc { get; set; }
        public string? Answer { get; set; }
        public AnswerInputMode? InputMode { get; set; }
        /// <summary>Also the concurrency token: only the first submission for a turn is accepted.</summary>
        public DateTime? AnsweredAtUtc { get; set; }
        public bool TimedOut { get; set; }
        public FollowUpReason? Decision { get; set; }
        /// <summary>The transcript as recognised, kept when glossary correction changed it.</summary>
        public string? RawAnswer { get; set; }
        /// <summary>From the end of the question being read to the first recognised word, measured in the browser.</summary>
        public int? ResponseDelayMs { get; set; }
        /// <summary>How long the candidate spoke, measured in the browser.</summary>
        public int? SpeakingMs { get; set; }
        /// <summary>Time the examiner (AI) took to decide on this answer.</summary>
        public int? DecisionLatencyMs { get; set; }
        public virtual ExamAttempt Attempt { get; set; } = null!;
        public virtual TurnRecording? Recording { get; set; }
    }
}
