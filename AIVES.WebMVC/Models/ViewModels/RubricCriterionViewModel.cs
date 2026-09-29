using System.ComponentModel.DataAnnotations;

namespace AIVES.WebMVC.Models.ViewModels
{
    public class RubricCriterionViewModel
    {
        public int Id { get; set; }
        public int RubricId { get; set; }

        [Required(ErrorMessage = "Criterion name is required")]
        [StringLength(300)]
        public string Criterion { get; set; }

        [Required(ErrorMessage = "Max points is required")]
        [Range(1, 1000)]
        public int MaxPoints { get; set; }

        [StringLength(1000)]
        public string Description { get; set; }

        public int Order { get; set; }
    }
}
