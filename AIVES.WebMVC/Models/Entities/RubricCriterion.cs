namespace AIVES.WebMVC.Models.Entities
{
    public class RubricCriterion
    {
        public int Id { get; set; }
        public int RubricId { get; set; }
        public string Criterion { get; set; } // e.g., "Understanding of concept", "Clarity of explanation"
        public int MaxPoints { get; set; }
        public string Description { get; set; }
        public int Order { get; set; }
        public virtual Rubric Rubric { get; set; }
    }
}
