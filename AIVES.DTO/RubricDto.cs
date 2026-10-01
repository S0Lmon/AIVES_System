namespace AIVES.DTO
{
    /// <summary>
    /// A rubric matrix. Rows are <see cref="Criteria"/> and columns are <see cref="Levels"/>;
    /// each criterion holds one cell per level.
    /// </summary>
    public class RubricDto
    {
        public int Id
        {
            get; set;
        }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int TotalPoints
        {
            get; set;
        }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
        public List<RubricLevelDto> Levels { get; set; } = new List<RubricLevelDto>();
        public List<RubricCriterionDto> Criteria { get; set; } = new List<RubricCriterionDto>();
    }
}