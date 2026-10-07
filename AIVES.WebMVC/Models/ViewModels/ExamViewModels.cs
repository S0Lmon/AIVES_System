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

    public AppLanguage Language { get; set; } = AppLanguage.Vi;

    public RecordingMode Recording { get; set; } = RecordingMode.Audio;

    public DateTime? EndsAtLocal { get; set; }

    [Range(0, ExamLimits.MaxBufferMinutes)]
    public int BufferMinutes { get; set; }

    [Range(0, ExamLimits.MaxBreakMinutes)]
    public int BreakMinutes { get; set; }

    [Range(0, ExamLimits.MaxBreakEveryCount)]
    public int BreakEveryCount { get; set; }

    public QuestionSelectionStrategy Strategy { get; set; } = QuestionSelectionStrategy.Balanced;

    [StringLength(ExamLimits.TermMaxLength)]
    public string? Term { get; set; }

    [StringLength(ExamLimits.ExamTypeMaxLength)]
    public string? ExamType { get; set; }

    [StringLength(ExamLimits.InstructionsMaxLength)]
    public string? Instructions { get; set; }

    public bool ScheduleOverflowAllowed { get; set; }

    public IReadOnlyList<AppLanguage> EnabledLanguages { get; set; } = [AppLanguage.Vi, AppLanguage.En];

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

public sealed class ExamCalendarDay
{
    public DateTime Date { get; init; }
    public IReadOnlyList<ExamSummaryDto> Exams { get; init; } = [];
}

public sealed class ExamCalendarViewModel
{
    public DateTime Month { get; init; }
    public IReadOnlyList<ExamCalendarDay> Days { get; init; } = [];
    public IReadOnlyList<ExamSummaryDto> UpcomingExams { get; init; } = [];
    public bool ShowOwner { get; init; }
}
