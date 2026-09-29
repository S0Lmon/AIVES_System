namespace AIVES.WebMVC.Models.Entities
{
    public class BloomLevel
    {
        public int Id { get; set; }
        public string Name { get; set; } // Remember, Understand, Apply, Analyze, Evaluate, Create
        public int Order { get; set; } // For sorting
        public string Description { get; set; }
        public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
    }
}
