namespace AIVES.DTO
{
    public class QuestionDto
    {
        public string? BloomLevelName
        {
            get; set;
        }
        public string? RubricName
        {
            get; set;
        }
        public int Id
        {
            get; set;
        }
        public string Content { get; set; } = string.Empty; // The question text
        public string Context { get; set; } = string.Empty; // Free text label for the question
        public int BloomLevelId
        {
            get; set;
        }
        public int? RubricId
        {
            get; set;
        }
        public int? TopicId
        {
            get; set;
        }
        public int? SubjectId
        {
            get; set;
        }
        public string? SubjectName
        {
            get; set;
        }
        public string? TopicName
        {
            get; set;
        }
        /// <summary>
        /// Optional. One of <see cref="QuestionDifficulties.Ordered"/> when set; null means unset.
        /// </summary>
        public string? Difficulty
        {
            get; set;
        }
        public string ExpectedAnswer { get; set; } = string.Empty;
        public int DisplayOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    }
}
