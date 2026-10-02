namespace AIVES.DAL.Entities
{
    /// <summary>
    /// A student sitting an exam, identified by email so a candidate can be listed before they register.
    /// Their slot starts at Exam.StartsAtUtc + (Order - 1) * Exam.SlotMinutes.
    /// </summary>
    public class ExamCandidate
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public int Order { get; set; }
        public string Email { get; set; } = string.Empty;
        public virtual Exam Exam { get; set; } = null!;
        public virtual ICollection<ExamCandidateQuestion> Questions { get; set; } = new List<ExamCandidateQuestion>();
    }
}
