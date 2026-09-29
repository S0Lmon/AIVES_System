namespace AIVES.WebMVC.Models.Entities
{
    public class Question
    {
        public int Id { get; set; }
        public string Content { get; set; } // The question text
        public string Context { get; set; } // Subject/Topic the question relates to
        public int BloomLevelId { get; set; }
        public int RubricId { get; set; }
        public string ExpectedAnswer { get; set; }
        public int DisplayOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
        public virtual BloomLevel BloomLevel { get; set; }
        public virtual Rubric Rubric { get; set; }
    }
}
