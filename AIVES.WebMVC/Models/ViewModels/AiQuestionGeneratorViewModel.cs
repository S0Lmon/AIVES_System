using System.ComponentModel.DataAnnotations;
using AIVES.DTO;

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

    public string Difficulty { get; set; } = "Balanced";

    public int? SubjectId { get; set; }

    public int? TopicId { get; set; }

    /// <summary>When true the panel retrieves material for the selected topic/subject and grounds the prompt in it.</summary>
    public bool UseMaterials { get; set; } = true;

    /// <summary>Null lets the router choose; otherwise force a provider.</summary>
    public AiProvider? Provider { get; set; }

    public bool GeminiAvailable { get; set; }
    public bool OllamaAvailable { get; set; }
    public AiProvider ActiveProvider { get; set; } = AiProvider.Gemini;

    public IReadOnlyList<SubjectOption> Subjects { get; set; } = [];
    public IReadOnlyList<TopicOption> Topics { get; set; } = [];

    /// <summary>Every topic, used by the slide panel to filter client side without a round trip.</summary>
    public IReadOnlyList<TopicOption> AllTopics { get; set; } = [];

    public List<GeneratedQuestionViewModel> Questions { get; set; } = [];
    public List<MaterialExcerptViewModel> Sources { get; set; } = [];
}

public sealed record MaterialExcerptViewModel(int Id, string Title);

public sealed class GeneratedQuestionViewModel
{
    public string Content { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string BloomLevel { get; set; } = string.Empty;
    public List<string> FollowUpQuestions { get; set; } = [];
}