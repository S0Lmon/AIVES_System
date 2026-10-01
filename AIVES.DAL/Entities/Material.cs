using AIVES.DTO;

namespace AIVES.DAL.Entities
{
    public class Material
    {
        public int Id
        {
            get; set;
        }
        public int TopicId
        {
            get; set;
        }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? SourceFileName { get; set; }
        public MaterialSourceType SourceType { get; set; } = MaterialSourceType.Manual;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
        public virtual Topic Topic { get; set; } = null!;
    }
}