using AIVES.BLL.Services.Accounts;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace AIVES.WebRazor.Pages.Profile;

public sealed class IndexModel(IAccountService accounts, ILogger<IndexModel> logger) : PageModel
{
    public UserProfileDto Profile { get; private set; } = new(string.Empty, string.Empty, string.Empty, false, DateTime.MinValue, []);

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        try
        {
            var profile = await accounts.GetProfileAsync(userId);
            if (profile is null)
                return NotFound();

            Profile = profile;
            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load the profile page");
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await accounts.LogoutAsync();
        return RedirectToPage("/Account/Login");
    }
}
