using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AIVES.DTO;

namespace AIVES.WebMVC.Models.ViewModels;

public static class QuestionTabs
{
    public const string Bank = "bank";
    public const string Create = "create";
    public const string Ai = "ai";
    public const string Bulk = "bulk";

    public static string Normalize(string? tab) => tab switch
    {
        Create => Create,
        Ai => Ai,
        Bulk => Bulk,
        _ => Bank
    };
}

public sealed class QuestionBankViewModel
{
    public string ActiveTab { get; set; } = QuestionTabs.Bank;

    public string? Search { get; set; }
    public int? BloomLevelId { get; set; }
    public int? RubricId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public bool ActiveOnly { get; set; }

    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int CoveredByRubric { get; set; }
    public int BloomSpread { get; set; }
    public int LinkedToSubject { get; set; }
    public int LinkedToTopic { get; set; }

    public IReadOnlyList<QuestionViewModel> Questions { get; set; } = [];
    public IReadOnlyList<SelectListItemOption> BloomLevels { get; set; } = [];
    public IReadOnlyList<SelectListItemOption> Rubrics { get; set; } = [];
    public IReadOnlyList<SubjectOption> Subjects { get; set; } = [];
    public IReadOnlyList<TopicOption> Topics { get; set; } = [];

    public QuestionViewModel Editor { get; set; } = new();
    public AiReviewViewModel Ai { get; set; } = new();
    public BulkGenerationViewModel Bulk { get; set; } = new();
}

/// <summary>Shared by the AI and Bulk tabs: describe the work, review the output, keep what is worth keeping.</summary>
public sealed class AiReviewViewModel
{
    [Required]
    [StringLength(120)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Topic { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? LearningOutcomes { get; set; }

    [Range(1, 10)]
    public int QuestionCount { get; set; } = 5;

    public string Difficulty { get; set; } = "Balanced";

    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }

    /// <summary>When true the request retrieves material for the selected topic/subject and grounds the prompt in it.</summary>
    public bool UseMaterials { get; set; } = true;

    /// <summary>Null lets the router choose; otherwise force a provider.</summary>
    public AiProvider? Provider { get; set; }

    public bool GeminiAvailable { get; set; }
    public bool OllamaAvailable { get; set; }
    public AiProvider ActiveProvider { get; set; } = AiProvider.Gemini;

    public IReadOnlyList<SubjectOption> Subjects { get; set; } = [];
    public IReadOnlyList<TopicOption> Topics { get; set; } = [];
    public IReadOnlyList<TopicOption> AllTopics { get; set; } = [];

    /// <summary>Blank until a generation succeeds.</summary>
    public bool HasResult { get; set; }

    /// <summary>Carries the reviewed set back to the save action without server side state.</summary>
    public string? ResultJson { get; set; }

    public List<GeneratedQuestionViewModel> Results { get; set; } = [];
    public List<MaterialExcerptViewModel> Sources { get; set; } = [];

    /// <summary>Rubric to attach to the saved questions.</summary>
    public int? RubricId { get; set; }
    public IReadOnlyList<SelectListItemOption> RubricOptions { get; set; } = [];
}

/// <summary>
/// The bulk plan: one row per Bloom level saying how many questions to produce for it. The
/// totals are what the generation request is built from.
/// </summary>
public sealed class BulkGenerationViewModel
{
    public AiReviewViewModel Request { get; set; } = new();
    public List<BulkPlanRow> Plan { get; set; } = [];
    public bool HasResult { get; set; }

    /// <summary>Populates one row per Bloom level with a sensible starting count.</summary>
    public static List<BulkPlanRow> DefaultPlan(IReadOnlyList<SelectListItemOption> levels) =>
        levels.Select(level => new BulkPlanRow { BloomLevelId = level.Id, BloomLevelName = level.Name, Count = level.Id >= 2 ? 2 : 0 }).ToList();

    public int PlannedCount => Plan.Sum(row => Math.Max(0, row.Count));
}

public sealed class BulkPlanRow
{
    public int BloomLevelId { get; set; }
    public string BloomLevelName { get; set; } = string.Empty;

    [Range(0, 10)]
    public int Count { get; set; }
}

/// <summary>One generated question travelling from the review list into the bank.</summary>
public sealed class GeneratedQuestionInput
{
    public string Content { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string BloomLevel { get; set; } = string.Empty;
    public List<string> FollowUpQuestions { get; set; } = [];
    public bool Selected { get; set; }

    public GeneratedQuestionViewModel ToViewModel() => new()
    {
        Content = Content,
        ExpectedAnswer = ExpectedAnswer,
        BloomLevel = BloomLevel,
        FollowUpQuestions = FollowUpQuestions
    };
}

public static class GeneratedQuestionJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The review list is round tripped through a hidden field rather than session state, so a
    /// save never depends on a previous request still being around.
    /// </summary>
    public static string Serialize(IEnumerable<GeneratedQuestionInput> questions) =>
        JsonSerializer.Serialize(questions, Options);

    public static List<GeneratedQuestionInput> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<GeneratedQuestionInput>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}