using System.Security.Claims;
using AIVES.BLL.Services.Accounts;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class UsersModel(IUserAdminService users, ILogger<UsersModel> logger) : PageModel
{
    public sealed record UserRow(string Id, string Email, string DisplayName, bool EmailConfirmed, DateTime CreatedAtUtc,
        bool IsAdmin, string? Role, bool IsCurrentUser, bool IsDisabled);

    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    private string? ActingEmail => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

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

    public async Task<IActionResult> OnPostSetRoleAsync(string userId, string role)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var result = await users.SetRoleAsync(userId, role, currentUserId, ActingEmail);
            if (result.Succeeded)
                TempData["UserMessage"] = L10n.Format("{0} is now {1}.", result.User?.Email ?? userId, RoleLabel(role));
            else
                TempData["UserError"] = string.Join(" ", result.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not change the role of {UserId}", userId);
            TempData["UserError"] = L10n.T("The role could not be changed. Please try again.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetDisabledAsync(string userId, bool disabled)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var result = await users.SetDisabledAsync(userId, disabled, currentUserId, ActingEmail);
            if (result.Succeeded)
                TempData["UserMessage"] = L10n.Format(disabled ? "{0} can no longer sign in." : "{0} can sign in again.", result.User?.Email ?? userId);
            else
                TempData["UserError"] = string.Join(" ", result.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not change the sign-in state of {UserId}", userId);
            TempData["UserError"] = L10n.T("The account could not be changed. Please try again.");
        }

        return RedirectToPage();
    }

    public static string RoleLabel(string? role) => role switch
    {
        AppRoles.Admin => L10n.T("Administrator"),
        AppRoles.Lecturer => L10n.T("Lecturer"),
        AppRoles.Student => L10n.T("Student"),
        _ => L10n.T("No role")
    };
}
