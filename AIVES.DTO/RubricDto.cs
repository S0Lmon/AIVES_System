namespace AIVES.DTO
{
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
    }
}
