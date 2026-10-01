namespace AIVES.DAL.Entities
{
    /// <summary>
    /// One cell of a rubric matrix: what this level looks like for this criterion. The
    /// descriptor is what a marker reads while grading; <see cref="Points"/> defaults to the
    /// level's points and can be overridden when a single cell is worth less.
    /// </summary>
    public class RubricCriterionLevel
    {
        public int Id
        {
            get; set;
        }
        public int RubricCriterionId
        {
            get; set;
        }
        public int RubricLevelId
        {
            get; set;
        }
        public string Descriptor { get; set; } = string.Empty;
        public int Points
        {
            get; set;
        }
        public virtual RubricCriterion RubricCriterion { get; set; } = null!;
        public virtual RubricLevel RubricLevel { get; set; } = null!;
    }
}