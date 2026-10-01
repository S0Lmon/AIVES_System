namespace AIVES.DTO
{
    /// <summary>One column of the matrix. The lecturer names and scores the levels.</summary>
    public class RubricLevelDto
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
        public List<RubricCriterionLevelDto> Cells { get; set; } = new List<RubricCriterionLevelDto>();
    }
}