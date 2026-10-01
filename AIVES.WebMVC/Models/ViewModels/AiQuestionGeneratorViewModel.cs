using System.ComponentModel.DataAnnotations;
using AIVES.DTO;

namespace AIVES.WebMVC.Models.ViewModels;

public sealed class AiQuestionGeneratorViewModel
{
    /// <summary>Resolved from <see cref="SubjectId"/> for the prompt. Never posted back.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Resolved from <see cref="TopicId"/> for the prompt. Never posted back.</summary>
    public string Topic { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? LearningOutcomes { get; set; }

    /// <summary>Optional. Blank lets the model choose.</summary>
    public int? BloomLevelId { get; set; }

    /// <summary>Optional. Blank lets the model choose.</summary>
    public string? Difficulty { get; set; }

    [Required]
    public int SubjectId { get; set; }

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
    public IReadOnlyList<SelectListItemOption> BloomLevels { get; set; } = [];

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
    public string Difficulty { get; set; } = string.Empty;
    public List<string> FollowUpQuestions { get; set; } = [];
}