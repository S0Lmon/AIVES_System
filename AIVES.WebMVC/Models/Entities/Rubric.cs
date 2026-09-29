namespace AIVES.WebMVC.Models.Entities
{
    public class Rubric
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int TotalPoints { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
        public virtual ICollection<RubricCriterion> Criteria { get; set; } = new List<RubricCriterion>();
        public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
    }
}
