namespace AIVES.DTO
{
    public class BloomLevelDto
    {
        public int Id
        {
            get; set;
        }
        public string Name { get; set; } = string.Empty; // Remember, Understand, Apply, Analyze, Evaluate, Create
        public int Order
        {
            get; set;
        } // For sorting
        public string Description { get; set; } = string.Empty;
    }
}
