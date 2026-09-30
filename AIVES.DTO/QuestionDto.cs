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
        public string Context { get; set; } = string.Empty; // Subject/Topic the question relates to
        public int BloomLevelId
        {
            get; set;
        }
        public int RubricId
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
