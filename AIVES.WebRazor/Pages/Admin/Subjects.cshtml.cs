using System.Security.Claims;
using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services.Operations;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class SubjectsModel(IUserAdminService users, ISubjectAccessService subjects, IAuditService audit) : PageModel
{
    public IReadOnlyList<SubjectAssignmentDto> Subjects { get; private set; } = [];
    public IReadOnlyList<UserRefDto> Lecturers { get; private set; } = [];
    public string? Message { get; private set; }

    private string? ActingEmail => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Lecturers = (await users.ListUsersAsync())
            .Where(user => user.Roles.Contains(AppRoles.Lecturer))
            .Select(user => new UserRefDto(user.Id, user.Email, user.DisplayName))
            .OrderBy(user => user.Email)
            .ToList();
        Subjects = await subjects.ListAssignmentsAsync(cancellationToken);
        Message = TempData["SubjectMessage"] as string;
    }

    public async Task<IActionResult> OnPostAsync(int subjectId, List<string>? lecturerIds, CancellationToken cancellationToken)
    {
        var lecturers = (await users.ListUsersAsync())
            .Where(user => user.Roles.Contains(AppRoles.Lecturer))
            .ToDictionary(user => user.Id);
        var chosen = (lecturerIds ?? []).Where(lecturers.ContainsKey).Distinct().ToList();
        await subjects.SetLecturersAsync(subjectId, chosen, cancellationToken);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.SubjectLecturersChanged,
            User.FindFirstValue(ClaimTypes.NameIdentifier), ActingEmail,
            Details: $"Subject {subjectId}: {(chosen.Count == 0 ? "open to all lecturers" : string.Join(", ", chosen.Select(id => lecturers[id].Email)))}"),
            cancellationToken);
        TempData["SubjectMessage"] = L10n.T("The lecturers of the subject were saved.");
        return RedirectToPage();
    }
}
