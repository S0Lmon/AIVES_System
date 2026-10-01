using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AIVES.DTO;
using AIVES.DTO.Localization;

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

/// <summary>
/// Shared by the AI and Bulk tabs: pick a catalogue subject and topic, review what came back, keep
/// what is worth keeping. Subject and topic are catalogue dropdowns only; the names sent to the
/// model are resolved from those ids so the prompt can never drift from what gets saved.
/// </summary>
public sealed class AiReviewViewModel
{
    /// <summary>Resolved from <see cref="SubjectId"/> for the prompt. Never posted back.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Resolved from <see cref="TopicId"/> for the prompt. Never posted back.</summary>
    public string Topic { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? LearningOutcomes { get; set; }

    /// <summary>Optional. Blank lets the model choose, which is then shown in the review.</summary>
    public int? BloomLevelId { get; set; }

    /// <summary>Optional. Blank lets the model choose.</summary>
    public string? Difficulty { get; set; }

    [Required]
    public int SubjectId { get; set; }

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
    public IReadOnlyList<SelectListItemOption> BloomLevels { get; set; } = [];

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
/// The bulk plan: one row per Bloom level giving a range the total has to sit inside, plus the
/// overall amount to produce. Ranges keep a lecturer's intent visible without asking them to do the
/// arithmetic that splits the total across levels.
/// </summary>
public sealed class BulkGenerationViewModel
{
    public const int MaxTotal = 30;

    public AiReviewViewModel Request { get; set; } = new();
    public List<BulkPlanRow> Plan { get; set; } = [];

    /// <summary>How many questions the lecturer wants in total.</summary>
    [Range(1, MaxTotal)]
    public int TotalAmount { get; set; } = 5;

    /// <summary>Optional difficulty window. Blank on either end means the model chooses.</summary>
    public string? DifficultyFrom { get; set; }
    public string? DifficultyTo { get; set; }

    public bool HasResult { get; set; }

    /// <summary>Populates one row per Bloom level with a sensible starting range.</summary>
    public static List<BulkPlanRow> DefaultPlan(IReadOnlyList<SelectListItemOption> levels) =>
        levels.Select(level => new BulkPlanRow
        {
            BloomLevelId = level.Id,
            BloomLevelName = level.Name,
            Min = 0,
            Max = level.Id >= 2 ? 5 : 0
        }).ToList();

    /// <summary>Sum of every row's minimum. The total cannot sit below this.</summary>
    public int MinTotal => Plan.Sum(row => Math.Max(0, row.Min));

    /// <summary>Sum of every row's maximum. The total cannot sit above this.</summary>
    public int MaxTotalForPlan => Plan.Sum(row => Math.Max(0, row.Max));

    /// <summary>
    /// Splits the total across the plan, starting every level at its minimum and handing out what
    /// is left in row order without pushing any row past its maximum. A total outside the reachable
    /// range is reported rather than silently clamped.
    /// </summary>
    public bool TryAllocate(out Dictionary<int, int> counts, out string? problem)
    {
        counts = [];
        problem = null;

        foreach (var row in Plan)
        {
            if (row.Max < row.Min)
            {
                problem = L10nFormat("The range for {0} ends below where it starts.", row.BloomLevelName);
                return false;
            }
        }

        if (Plan.Count == 0)
        {
            problem = L10nFormat("There are no Bloom levels to split the questions across.");
            return false;
        }

        if (TotalAmount < MinTotal || TotalAmount > MaxTotalForPlan)
        {
            problem = L10nFormat("Ask for between {0} and {1} questions to fit your per level ranges.",
                MinTotal, MaxTotalForPlan);
            return false;
        }

        var allocated = Plan.ToDictionary(row => row.BloomLevelId, _ => 0);
        var remaining = TotalAmount;

        foreach (var row in Plan)
        {
            var take = Math.Min(row.Min, remaining);
            allocated[row.BloomLevelId] = take;
            remaining -= take;
        }

        foreach (var row in Plan)
        {
            if (remaining <= 0)
                break;
            var room = row.Max - allocated[row.BloomLevelId];
            if (room <= 0)
                continue;
            var take = Math.Min(room, remaining);
            allocated[row.BloomLevelId] += take;
            remaining -= take;
        }

        counts = allocated;
        return remaining == 0;
    }

    private static string L10nFormat(string english, params object?[] arguments) => L10n.Format(english, arguments);
}

public sealed class BulkPlanRow
{
    public int BloomLevelId { get; set; }
    public string BloomLevelName { get; set; } = string.Empty;

    [Range(0, 10)]
    public int Min { get; set; }

    [Range(0, 10)]
    public int Max { get; set; }
}

/// <summary>One generated question travelling from the review list into the bank.</summary>
public sealed class GeneratedQuestionInput
{
    public string Content { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string BloomLevel { get; set; } = string.Empty;

    /// <summary>Model assigned when the request did not pin one. Shown in the review only.</summary>
    public string Difficulty { get; set; } = string.Empty;

    public List<string> FollowUpQuestions { get; set; } = [];
    public bool Selected { get; set; }

    public GeneratedQuestionViewModel ToViewModel() => new()
    {
        Content = Content,
        ExpectedAnswer = ExpectedAnswer,
        BloomLevel = BloomLevel,
        Difficulty = Difficulty,
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