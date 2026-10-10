using System.Security.Claims;
using AIVES.BLL.Services.Accounts;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class UsersModel(IUserAdminService users) : PageModel
{
    public sealed record UserRow(string Id, string Email, string DisplayName, bool EmailConfirmed, DateTime CreatedAtUtc,
        bool IsAdmin, string? Role, bool IsCurrentUser, bool IsDisabled);

    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Users = (await users.ListUsersAsync()).Select(user => new UserRow(
            user.Id, user.Email, user.DisplayName, user.EmailConfirmed, user.CreatedAtUtc,
            user.Roles.Contains(AppRoles.Admin),
            user.Roles.FirstOrDefault(role => AppRoles.Assignable.Contains(role)),
            user.Id == currentUserId,
            user.IsDisabled)).ToList();
        Message = TempData["UserMessage"] as string;
        Error = TempData["UserError"] as string;
    }

    public static string RoleLabel(string? role) => role switch
    {
        AppRoles.Admin => L10n.T("Administrator"),
        AppRoles.Lecturer => L10n.T("Lecturer"),
        AppRoles.Student => L10n.T("Student"),
        _ => L10n.T("No role")
    };
}
