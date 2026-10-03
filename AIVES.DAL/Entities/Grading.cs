using AIVES.DTO;

namespace AIVES.DAL.Entities
{
    /// <summary>
    /// The grade of one main question for one candidate. The AI columns hold the suggestion and are
    /// never shown to the student as a grade; the lecturer columns hold the decision. The final score
    /// is the lecturer's score once set, so the AI can only ever propose.
    /// </summary>
    public class QuestionGrade
    {
        public int Id { get; set; }
        public int ExamCandidateQuestionId { get; set; }
        public decimal MaxScore { get; set; }
        public decimal? AiScore { get; set; }
        /// <summary>Per-criterion suggestion as JSON (<see cref="CriterionScoreDto"/> list).</summary>
        public string? AiCriteriaJson { get; set; }
        public string? AiStrengths { get; set; }
        public string? AiWeaknesses { get; set; }
        public string? AiMissingPoints { get; set; }
        public string? AiSummary { get; set; }
        public string? AiModel { get; set; }
        public DateTime? AiGradedAtUtc { get; set; }
        public decimal? LecturerScore { get; set; }
        public string? LecturerComment { get; set; }
        public string? UpdatedById { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public virtual ExamCandidateQuestion Question { get; set; } = null!;
    }

    /// <summary>An encrypted audio (or audio + video) recording of one answer, kept as evidence.</summary>
    public class TurnRecording
    {
        public int Id { get; set; }
        public int ExamTurnId { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public bool HasVideo { get; set; }
        /// <summary>Path relative to the recording store's root.</summary>
        public string StoragePath { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public virtual ExamTurn Turn { get; set; } = null!;
    }

    /// <summary>Who did what, when: grade changes, recording access, exam and account changes.</summary>
    public class AuditEntry
    {
        public long Id { get; set; }
        public DateTime AtUtc { get; set; }
        public string? ActorId { get; set; }
        public string? ActorEmail { get; set; }
        public string Action { get; set; } = string.Empty;
        public int? ExamId { get; set; }
        public int? CandidateId { get; set; }
        public string? Details { get; set; }
    }

    /// <summary>A lecturer allowed to run exams for a subject.</summary>
    public class SubjectLecturer
    {
        public int SubjectId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public virtual Subject Subject { get; set; } = null!;
    }

    /// <summary>A subject term and how speech recognition tends to mishear it, e.g. "API" ← "ây pi ai".</summary>
    public class GlossaryTerm
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }
        public string Term { get; set; } = string.Empty;
        /// <summary>Spoken or misrecognised forms, separated by ';'.</summary>
        public string SpokenForms { get; set; } = string.Empty;
        public virtual Subject Subject { get; set; } = null!;
    }

    /// <summary>A system-wide setting changed from the admin pages (speech, recording retention).</summary>
    public class SystemSetting
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime ModifiedAtUtc { get; set; }
    }
}
