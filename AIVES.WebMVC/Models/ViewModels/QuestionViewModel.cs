using System.ComponentModel.DataAnnotations;

namespace AIVES.WebMVC.Models.ViewModels
{
    public class QuestionViewModel
    {
        public int Id
        {
            get; set;
        }

        [Required(ErrorMessage = "Question content is required")]
        [StringLength(5000, MinimumLength = 10, ErrorMessage = "Question must be between 10 and 5000 characters")]
        public string Content { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Context { get; set; }

        [Required(ErrorMessage = "Bloom level is required")]
        public int BloomLevelId
        {
            get; set;
        }

        public string? BloomLevelName
        {
            get; set;
        }

        /// <summary>Optional. A question without a rubric is valid, so this is nullable.</summary>
        public int? RubricId
        {
            get; set;
        }

        public string? RubricName
        {
            get; set;
        }

        /// <summary>Optional link into the catalog so a question is reachable by subject.</summary>
        public int? SubjectId
        {
            get; set;
        }

        /// <summary>Optional link into the catalog so a question is reachable by topic.</summary>
        public int? TopicId
        {
            get; set;
        }

        public string? SubjectName
        {
            get; set;
        }

        public string? TopicName
        {
            get; set;
        }

        [StringLength(5000)]
        public string? ExpectedAnswer { get; set; }

        [Range(0, 10000)]
        public int DisplayOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate
        {
            get; set;
        }
        public DateTime ModifiedDate
        {
            get; set;
        }
    }
}
