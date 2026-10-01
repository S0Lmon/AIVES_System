namespace AIVES.DAL.Entities
{
    public class Question
    {
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
        public int? TopicId
        {
            get; set;
        }
        public string ExpectedAnswer { get; set; } = string.Empty;
        public int DisplayOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
        public virtual BloomLevel BloomLevel { get; set; } = null!;
        public virtual Rubric Rubric { get; set; } = null!;
        public virtual Topic? Topic { get; set; }
    }
}
