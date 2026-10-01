namespace AIVES.DTO
{
    /// <summary>One cell of the matrix: how this level reads for this criterion.</summary>
    public class RubricCriterionLevelDto
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

        /// <summary>
        /// Column name from the submitted form. A brand new rubric has no level identifiers yet,
        /// so cells arrive by name and are bound to a level once one is created.
        /// </summary>
        public string LevelName { get; set; } = string.Empty;
    }
}