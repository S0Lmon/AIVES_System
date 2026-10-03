using AIVES.DTO;
using AIVES.DTO.Localization;

namespace AIVES.WebMVC;

/// <summary>Labels shared by the exam, grading and result pages.</summary>
public static class ViewText
{
    public static string Reason(FollowUpReason reason) => reason switch
    {
        FollowUpReason.Sufficient => L10n.T("answer sufficient"),
        FollowUpReason.Vague => L10n.T("answer vague"),
        FollowUpReason.Missing => L10n.T("key points missing"),
        FollowUpReason.Contradiction => L10n.T("contradiction"),
        FollowUpReason.OffTopic => L10n.T("off topic"),
        FollowUpReason.LimitReached => L10n.T("follow-up limit reached"),
        FollowUpReason.NoAnswer => L10n.T("no answer given"),
        _ => L10n.T("AI unavailable, moved on")
    };

    public static string Score(decimal? value) => value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    public static string Seconds(double? value) => value is null ? "—" : L10n.Format("{0} s", value.Value.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));

    public static string Action(string action) => action switch
    {
        AuditActions.ExamCreated => L10n.T("Exam created"),
        AuditActions.ExamUpdated => L10n.T("Exam changed"),
        AuditActions.ExamQuestionsRedrawn => L10n.T("Questions drawn again"),
        AuditActions.ExamDeleted => L10n.T("Exam deleted"),
        AuditActions.InterviewStarted => L10n.T("Viva started"),
        AuditActions.InterviewCompleted => L10n.T("Viva finished"),
        AuditActions.RecordingViewed => L10n.T("Recording played"),
        AuditActions.RecordingPurged => L10n.T("Recordings deleted (retention)"),
        AuditActions.AiGraded => L10n.T("AI proposed a grade"),
        AuditActions.AiGradingFailed => L10n.T("AI grading failed"),
        AuditActions.AiGradingRequested => L10n.T("AI grading requested again"),
        AuditActions.GradeSaved => L10n.T("Scores saved"),
        AuditActions.GradeFinalized => L10n.T("Grade confirmed"),
        AuditActions.GradeReopened => L10n.T("Grade reopened"),
        AuditActions.GradeSheetExported => L10n.T("Grade sheet exported"),
        AuditActions.UserRoleChanged => L10n.T("Role changed"),
        AuditActions.UserLocked => L10n.T("Account disabled"),
        AuditActions.UserUnlocked => L10n.T("Account enabled"),
        AuditActions.SubjectLecturersChanged => L10n.T("Subject lecturers changed"),
        AuditActions.SettingsChanged => L10n.T("Settings changed"),
        _ => action
    };
}
