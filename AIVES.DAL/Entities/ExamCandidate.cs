using AIVES.DTO;

namespace AIVES.DAL.Entities
{
    /// <summary>
    /// A student sitting an exam, identified by email so a candidate can be listed before they register.
    /// Their slot starts at Exam.StartsAtUtc + (Order - 1) * Exam.SlotMinutes unless
    /// <see cref="SlotStartsAtUtc"/> was stored or moved by hand.
    /// </summary>
    public class ExamCandidate
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public int Order { get; set; }
        public string Email { get; set; } = string.Empty;
        /// <summary>
        /// Start of this candidate's slot. Set when the schedule is generated and whenever the
        /// lecturer moves the slot by hand (plan §6). Null for rows created before slots were
        /// stored; readers then fall back to the layout derived from Exam.StartsAtUtc.
        /// </summary>
        public DateTime? SlotStartsAtUtc { get; set; }
        /// <summary>
        /// Lecturer-set status overriding the derived one: Absent, Cancelled, RequiresReview or
        /// NotScheduled (plan §5); null means derive from the attempt and the clock.
        /// </summary>
        public CandidateStatus? StatusOverride { get; set; }
        public virtual Exam Exam { get; set; } = null!;
        public virtual ICollection<ExamCandidateQuestion> Questions { get; set; } = new List<ExamCandidateQuestion>();
        public virtual ExamAttempt? Attempt { get; set; }
    }
}
