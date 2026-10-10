using AIVES.DTO;

namespace AIVES.DAL.Entities
{
    /// <summary>A viva exam session: one subject, a list of candidates and a fixed slot per candidate.</summary>
    public class Exam
    {
        public int Id
        {
            get; set;
        }
        public string Title { get; set; } = string.Empty;
        /// <summary>Null once the subject is deleted; <see cref="SubjectName"/> keeps the record readable.</summary>
        public int? SubjectId
        {
            get; set;
        }
        public string SubjectName { get; set; } = string.Empty;
        public int? TopicId
        {
            get; set;
        }
        public string? TopicName
        {
            get; set;
        }
        public DateTime StartsAtUtc
        {
            get; set;
        }
        /// <summary>
        /// End of the examination window the lecturer chose (plan §4/§6). Null when the exam predates
        /// the window column or no end was given; readers then treat the schedule end as the window end.
        /// </summary>
        public DateTime? EndsAtUtc
        {
            get; set;
        }
        public int SlotMinutes
        {
            get; set;
        }
        /// <summary>Quiet time inserted after each candidate's slot, in minutes (plan §6, optional).</summary>
        public int BufferMinutes
        {
            get; set;
        }
        /// <summary>Length of a break inserted after every <see cref="BreakEveryCount"/> candidates (plan §6, optional).</summary>
        public int BreakMinutes
        {
            get; set;
        }
        /// <summary>Put a break after every N candidates; 0 means no scheduled breaks.</summary>
        public int BreakEveryCount
        {
            get; set;
        }
        /// <summary>How main questions are drawn for each candidate (plan §8).</summary>
        public QuestionSelectionStrategy Strategy { get; set; } = QuestionSelectionStrategy.Balanced;
        /// <summary>Academic term, e.g. "Fall 2026" (plan §4).</summary>
        public string? Term
        {
            get; set;
        }
        /// <summary>Examination type, e.g. "Final" or "Competency assessment" (plan §4).</summary>
        public string? ExamType
        {
            get; set;
        }
        /// <summary>Instructions or notes shown with the session (plan §4).</summary>
        public string? Instructions
        {
            get; set;
        }
        public int MainQuestionCount
        {
            get; set;
        }
        public int MaxFollowUpQuestions
        {
            get; set;
        }
        public int AnswerTimeLimitSeconds { get; set; } = 120;
        public int MaxFollowUpsPerQuestion { get; set; } = 2;
        /// <summary>Culture code the interview is read and recognised in, e.g. "vi-VN".</summary>
        public string Language { get; set; } = "vi-VN";
        /// <summary>Record each spoken answer (audio) as evidence for grade appeals.</summary>
        public bool RecordAudio
        {
            get; set;
        }
        /// <summary>Also record the camera; implies audio.</summary>
        public bool RecordVideo
        {
            get; set;
        }
        /// <summary>Identity user id of the lecturer who owns the exam.</summary>
        public string CreatedById { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
        public virtual Subject? Subject
        {
            get; set;
        }
        public virtual Topic? Topic
        {
            get; set;
        }
        public virtual ICollection<ExamCandidate> Candidates { get; set; } = new List<ExamCandidate>();
    }
}
