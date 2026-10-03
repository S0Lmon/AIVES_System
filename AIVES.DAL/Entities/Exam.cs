namespace AIVES.DAL.Entities
{
    /// <summary>A viva exam session: one subject, a list of candidates and a fixed slot per candidate.</summary>
    public class Exam
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        /// <summary>Null once the subject is deleted; <see cref="SubjectName"/> keeps the record readable.</summary>
        public int? SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public int? TopicId { get; set; }
        public string? TopicName { get; set; }
        public DateTime StartsAtUtc { get; set; }
        public int SlotMinutes { get; set; }
        public int MainQuestionCount { get; set; }
        public int MaxFollowUpQuestions { get; set; }
        public int AnswerTimeLimitSeconds { get; set; } = 120;
        public int MaxFollowUpsPerQuestion { get; set; } = 2;
        /// <summary>Culture code the interview is read and recognised in, e.g. "vi-VN".</summary>
        public string Language { get; set; } = "vi-VN";
        /// <summary>Record each spoken answer (audio) as evidence for grade appeals.</summary>
        public bool RecordAudio { get; set; }
        /// <summary>Also record the camera; implies audio.</summary>
        public bool RecordVideo { get; set; }
        /// <summary>Identity user id of the lecturer who owns the exam.</summary>
        public string CreatedById { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
        public virtual Subject? Subject { get; set; }
        public virtual Topic? Topic { get; set; }
        public virtual ICollection<ExamCandidate> Candidates { get; set; } = new List<ExamCandidate>();
    }
}
