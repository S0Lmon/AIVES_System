using System.Security.Claims;
using AIVES.BLL.Services.Accounts;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class SetDisabledModel(IUserAdminService users, ILogger<SetDisabledModel> logger) : PageModel
{
    private string? ActingEmail => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

    public IActionResult OnGet() => RedirectToPage("/Admin/Users");

    public async Task<IActionResult> OnPostAsync(string userId, bool disabled)
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

        return RedirectToPage("/Admin/Users");
    }
}
