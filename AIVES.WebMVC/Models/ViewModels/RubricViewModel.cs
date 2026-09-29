using System.ComponentModel.DataAnnotations;

namespace AIVES.WebMVC.Models.ViewModels
{
    public class RubricViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Rubric name is required")]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Name must be between 5 and 200 characters")]
        public string Name { get; set; }

        [StringLength(1000)]
        public string Description { get; set; }

        [Required(ErrorMessage = "Total points is required")]
        [Range(1, 1000)]
        public int TotalPoints { get; set; }

        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        public IList<RubricCriterionViewModel> Criteria { get; set; } = new List<RubricCriterionViewModel>();
    }
}
