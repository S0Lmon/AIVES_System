namespace AIVES.DAL.Entities
{
    /// <summary>
    /// A main question assigned to a candidate. The text is copied at assignment time so the exam record
    /// stays the same if the bank question is later edited or deleted (QuestionId then becomes null).
    /// </summary>
    public class ExamCandidateQuestion
    {
        public int Id { get; set; }
        public int ExamCandidateId { get; set; }
        public int Order { get; set; }
        public int? QuestionId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string ExpectedAnswer { get; set; } = string.Empty;
        public string BloomLevelName { get; set; } = string.Empty;
        public virtual ExamCandidate Candidate { get; set; } = null!;
        public virtual Question? Question { get; set; }
    }
}
