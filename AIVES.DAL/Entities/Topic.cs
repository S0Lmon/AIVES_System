namespace AIVES.DAL.Entities
{
    public class Topic
    {
        public int Id
        {
            get; set;
        }
        public int SubjectId
        {
            get; set;
        }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
        public virtual Subject Subject { get; set; } = null!;
        public virtual ICollection<Material> Materials { get; set; } = new List<Material>();
    }
}