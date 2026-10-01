namespace AIVES.DAL.Entities
{
    public class RubricCriterion
    {
        public int Id
        {
            get; set;
        }
        public int RubricId
        {
            get; set;
        }
        public string Criterion { get; set; } = string.Empty; // e.g., "Understanding of concept", "Clarity of explanation"
        public int MaxPoints
        {
            get; set;
        }
        public string Description { get; set; } = string.Empty;
        public int Order
        {
            get; set;
        }
        public virtual Rubric Rubric { get; set; } = null!;
        public virtual ICollection<RubricCriterionLevel> Levels { get; set; } = new List<RubricCriterionLevel>();
    }
}
