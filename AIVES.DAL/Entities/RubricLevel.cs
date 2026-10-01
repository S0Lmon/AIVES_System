namespace AIVES.DAL.Entities
{
    /// <summary>
    /// One column of a rubric matrix. The lecturer defines the scale, so the levels can read
    /// "Exceeds / Meets / Approaching / Below" for one rubric and "Novice / Advanced / Expert"
    /// for another. <see cref="Points"/> is the score awarded for reaching this level.
    /// </summary>
    public class RubricLevel
    {
        public int Id
        {
            get; set;
        }
        public int RubricId
        {
            get; set;
        }
        public string Name { get; set; } = string.Empty;
        public int Points
        {
            get; set;
        }
        public string Description { get; set; } = string.Empty;
        public int Order
        {
            get; set;
        }
        public virtual Rubric Rubric { get; set; } = null!;
        public virtual ICollection<RubricCriterionLevel> Cells { get; set; } = new List<RubricCriterionLevel>();
    }
}