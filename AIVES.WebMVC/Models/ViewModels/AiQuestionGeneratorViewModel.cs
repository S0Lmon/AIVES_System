using System.ComponentModel.DataAnnotations;

namespace AIVES.WebMVC.Models.ViewModels;

public sealed class AiQuestionGeneratorViewModel
{
    [Required(ErrorMessage = "Please enter a subject.")]
    [StringLength(120)]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a topic.")]
    [StringLength(200)]
    public string Topic { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? LearningOutcomes { get; set; }

    [Range(1, 10)]
    public int QuestionCount { get; set; } = 5;

    public string Difficulty { get; set; } = "Cân bằng";
    public List<GeneratedQuestionViewModel> Questions { get; set; } = [];
}

public sealed class GeneratedQuestionViewModel
{
    public string Content { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string BloomLevel { get; set; } = string.Empty;
    public List<string> FollowUpQuestions { get; set; } = [];
}
