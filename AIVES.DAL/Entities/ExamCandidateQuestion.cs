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
        /// <summary>The question's rubric as it was when assigned (<see cref="DTO.RubricSnapshot"/> JSON); null for none.</summary>
        public string? RubricJson { get; set; }
        public virtual QuestionGrade? Grade { get; set; }
        public virtual ExamCandidate Candidate { get; set; } = null!;
        public virtual Question? Question { get; set; }
    }
}
