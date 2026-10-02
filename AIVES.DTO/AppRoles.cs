namespace AIVES.DTO;

/// <summary>
/// Account roles. Everyone who registers starts as a Student; an administrator promotes lecturers
/// from the Users page. Admin itself comes from AdminAccess:Emails and is never handed out in the UI.
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Lecturer = "Lecturer";
    public const string Student = "Student";

    public static readonly IReadOnlyList<string> All = [Admin, Lecturer, Student];

    /// <summary>Roles an administrator can set from the Users page; a user holds exactly one of them.</summary>
    public static readonly IReadOnlyList<string> Assignable = [Lecturer, Student];

    /// <summary>The role given to every new account.</summary>
    public const string Default = Student;
}
