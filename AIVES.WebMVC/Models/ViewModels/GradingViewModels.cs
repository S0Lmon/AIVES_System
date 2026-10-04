using AIVES.DTO;

namespace AIVES.WebMVC.Models.ViewModels;

/// <summary>The lecturer's grading form: one score per main question, comments, and whether to confirm.</summary>
public sealed class GradeFormModel
{
    public List<QuestionScoreForm> Questions { get; set; } = [];
    public string? Comment { get; set; }
    public bool Finalize { get; set; }
}

public sealed class QuestionScoreForm
{
    public int Id { get; set; }
    /// <summary>Text so that "7,5" typed on a Vietnamese keyboard is accepted as well as "7.5".</summary>
    public string? Score { get; set; }
    public string? Comment { get; set; }
}

public sealed class AuditViewModel
{
    public string Title { get; set; } = string.Empty;
    public int? ExamId { get; set; }
    public string? Action { get; set; }
    public string? Actor { get; set; }
    public IReadOnlyList<AuditEntryDto> Entries { get; set; } = [];
}

public sealed class SpeechSettingsViewModel
{
    public AppLanguage DefaultLanguage { get; set; } = AppLanguage.Vi;
    public bool EnableVietnamese { get; set; } = true;
    public bool EnableEnglish { get; set; } = true;
    public double SpeechRate { get; set; } = 0.95;
    public string? VietnameseVoice { get; set; }
    public string? EnglishVoice { get; set; }
    public int RecordingRetentionDays { get; set; } = 365;
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public sealed class SubjectAssignmentsViewModel
{
    public IReadOnlyList<SubjectAssignmentDto> Subjects { get; set; } = [];
    public IReadOnlyList<UserRefDto> Lecturers { get; set; } = [];
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public sealed class GlossaryViewModel
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public IReadOnlyList<SubjectOption> Subjects { get; set; } = [];
    public IReadOnlyList<GlossaryTermDto> Terms { get; set; } = [];
    public string? Message { get; set; }
    public string? Error { get; set; }
}
