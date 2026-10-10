using System.Security.Claims;
using AIVES.BLL.Services.Accounts;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class SetRoleModel(IUserAdminService users, ILogger<SetRoleModel> logger) : PageModel
{
    private string? ActingEmail => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

    public IActionResult OnGet() => RedirectToPage("/Admin/Users");

    public async Task<IActionResult> OnPostAsync(string userId, string role)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var result = await users.SetRoleAsync(userId, role, currentUserId, ActingEmail);
            if (result.Succeeded)
                TempData["UserMessage"] = L10n.Format("{0} is now {1}.", result.User?.Email ?? userId, role switch
                {
                    AppRoles.Admin => L10n.T("Administrator"),
                    AppRoles.Lecturer => L10n.T("Lecturer"),
                    AppRoles.Student => L10n.T("Student"),
                    _ => L10n.T("No role")
                });
            else
                TempData["UserError"] = string.Join(" ", result.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not change the role of {UserId}", userId);
            TempData["UserError"] = L10n.T("The role could not be changed. Please try again.");
        }

        return RedirectToPage("/Admin/Users");
    }
}
