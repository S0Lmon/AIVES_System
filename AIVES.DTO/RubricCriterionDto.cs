namespace AIVES.DTO
{
    /// <summary>One row of the matrix.</summary>
    public class RubricCriterionDto
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
        /// <summary>One cell per column. Empty descriptors are allowed while drafting.</summary>
        public List<RubricCriterionLevelDto> Levels { get; set; } = new List<RubricCriterionLevelDto>();
    }
}