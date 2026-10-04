using System.ComponentModel.DataAnnotations;
using AIVES.DTO;

namespace AIVES.WebMVC.Models.ViewModels;

/// <summary>Create/edit form. Times are in the display time zone; the controller converts to UTC.</summary>
public sealed class ExamFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Please enter a title.")]
    [StringLength(ExamLimits.TitleMaxLength, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Pick a subject.")]
    public int SubjectId { get; set; }

    public int? TopicId { get; set; }

    [Required(ErrorMessage = "Please choose when the first slot starts.")]
    public DateTime? StartsAtLocal { get; set; }

    [Range(ExamLimits.MinSlotMinutes, ExamLimits.MaxSlotMinutes)]
    public int SlotMinutes { get; set; } = 15;

    [Range(1, ExamLimits.MaxMainQuestions)]
    public int MainQuestionCount { get; set; } = 3;

    [Range(0, ExamLimits.MaxFollowUpQuestions)]
    public int MaxFollowUpQuestions { get; set; } = 2;

    [Range(0, ExamLimits.MaxFollowUpsPerQuestionLimit)]
    public int MaxFollowUpsPerQuestion { get; set; } = ExamLimits.DefaultFollowUpsPerQuestion;

    [Range(ExamLimits.MinAnswerSeconds, ExamLimits.MaxAnswerSeconds)]
    public int AnswerTimeLimitSeconds { get; set; } = ExamLimits.DefaultAnswerSeconds;

    /// <summary>Language the AI examiner speaks and listens in.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.Vi;

    /// <summary>What is recorded of each answer as evidence for grade appeals.</summary>
    public RecordingMode Recording { get; set; } = RecordingMode.Audio;

    /// <summary>Interview languages the administrator enabled.</summary>
    public IReadOnlyList<AppLanguage> EnabledLanguages { get; set; } = [AppLanguage.Vi, AppLanguage.En];

    /// <summary>One email per line (commas and semicolons also work), in sitting order.</summary>
    public string CandidateEmails { get; set; } = string.Empty;

    public IReadOnlyList<SubjectOption> Subjects { get; set; } = [];
    public IReadOnlyList<TopicOption> Topics { get; set; } = [];
    public string TimeZoneLabel { get; set; } = string.Empty;
}

public sealed class ExamIndexViewModel
{
    public IReadOnlyList<ExamSummaryDto> Exams { get; set; } = [];
    public bool ShowOwner { get; set; }
}
