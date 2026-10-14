using AIVES.BLL.Services;
using AIVES.BLL.Services.Catalog;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace AIVES.WebRazor.Pages.Questions;

/// <summary>The fields a lecturer edits on Create and Edit; bound with [BindProperty].</summary>
public sealed class QuestionInput
{
    [Required(ErrorMessage = "Enter the question text.")]
    [StringLength(5000, MinimumLength = 10, ErrorMessage = "The question must be between 10 and 5000 characters.")]
    [Display(Name = "Question text")]
    public string Content { get; set; } = string.Empty;

    [StringLength(300)]
    [Display(Name = "Context label")]
    public string? Context
    {
        get; set;
    }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a Bloom level.")]
    [Display(Name = "Bloom level")]
    public int BloomLevelId
    {
        get; set;
    }

    [Display(Name = "Rubric")]
    public int? RubricId
    {
        get; set;
    }

    [Display(Name = "Subject")]
    public int? SubjectId
    {
        get; set;
    }

    [Display(Name = "Topic")]
    public int? TopicId
    {
        get; set;
    }

    [Display(Name = "Difficulty")]
    public string? Difficulty
    {
        get; set;
    }

    [StringLength(5000)]
    [Display(Name = "Expected answer")]
    public string? ExpectedAnswer
    {
        get; set;
    }

    [Range(0, 10000)]
    [Display(Name = "Display order")]
    public int DisplayOrder
    {
        get; set;
    }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public static QuestionInput From(QuestionDto question) => new()
    {
        Content = question.Content,
        Context = question.Context,
        BloomLevelId = question.BloomLevelId,
        RubricId = question.RubricId,
        SubjectId = question.SubjectId,
        TopicId = question.TopicId,
        Difficulty = question.Difficulty,
        ExpectedAnswer = question.ExpectedAnswer,
        DisplayOrder = question.DisplayOrder,
        IsActive = question.IsActive
    };

    public QuestionDto ToDto(int id = 0) => new()
    {
        Id = id,
        Content = Content.Trim(),
        Context = Context?.Trim() ?? string.Empty,
        BloomLevelId = BloomLevelId,
        RubricId = RubricId is > 0 ? RubricId : null,
        SubjectId = SubjectId is > 0 ? SubjectId : null,
        TopicId = TopicId is > 0 ? TopicId : null,
        Difficulty = QuestionDifficulties.Normalize(Difficulty),
        ExpectedAnswer = ExpectedAnswer?.Trim() ?? string.Empty,
        DisplayOrder = DisplayOrder,
        IsActive = IsActive
    };
}

/// <summary>Dropdown data for the question form, read through the BLL.</summary>
public sealed class QuestionFormOptions
{
    public SelectList BloomLevels { get; private init; } = null!;
    public SelectList Rubrics { get; private init; } = null!;
    public SelectList Subjects { get; private init; } = null!;
    public IReadOnlyList<TopicDto> Topics { get; private init; } = [];
    public SelectList Difficulties { get; private init; } = null!;

    public static async Task<QuestionFormOptions> LoadAsync(IBloomLevelService bloomLevels, IRubricService rubrics,
        ICatalogService catalog, CancellationToken cancellationToken = default) => new()
        {
            BloomLevels = new SelectList((await bloomLevels.GetAllAsync()).OrderBy(level => level.Order), nameof(BloomLevelDto.Id), nameof(BloomLevelDto.Name)),
            Rubrics = new SelectList(await rubrics.GetAllRubricsAsync(), nameof(RubricDto.Id), nameof(RubricDto.Name)),
            Subjects = new SelectList(await catalog.GetSubjectsAsync(cancellationToken), nameof(SubjectDto.Id), nameof(SubjectDto.Name)),
            Topics = await catalog.GetTopicsAsync(cancellationToken: cancellationToken),
            Difficulties = new SelectList(QuestionDifficulties.Ordered)
        };
}

public static class QuestionFormValidation
{
    /// <summary>
    /// Keeps only the model state of the form this handler serves. Every [BindProperty] is bound and
    /// validated on every handler, and a question form that was not posted falls back to the empty
    /// prefix, so an AI form would otherwise fail on "Content" and "BloomLevelId" it never sent.
    /// </summary>
    public static void OnlyValidate(this ModelStateDictionary modelState, string prefix)
    {
        foreach (var key in modelState.Keys.Where(key => !IsUnder(key, prefix)).ToList())
            modelState.Remove(key);
    }

    private static bool IsUnder(string key, string prefix) =>
        key == prefix || key.StartsWith(prefix + ".", StringComparison.Ordinal) || key.StartsWith(prefix + "[", StringComparison.Ordinal);
}

/// <summary>Implemented by Create and Edit so both render the same _QuestionFields partial.</summary>
public interface IQuestionFormPage
{
    QuestionInput Input
    {
        get;
    }
    QuestionFormOptions Options
    {
        get;
    }
}
