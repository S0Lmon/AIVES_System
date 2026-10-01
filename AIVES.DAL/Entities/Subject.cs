namespace AIVES.DAL.Entities
{
    public class Subject
    {
        public int Id
        {
            get; set;
        }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
        public virtual ICollection<Topic> Topics { get; set; } = new List<Topic>();
    }
}