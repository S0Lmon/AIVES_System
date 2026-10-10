using AIVES.BLL.Services.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Account;

public sealed class ExternalLoginCallbackModel(IAccountService accounts, ILogger<ExternalLoginCallbackModel> logger) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? remoteError = null)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
            return RedirectToPage("/Account/Login");

        try
        {
            var result = await accounts.CompleteGoogleLoginAsync();
            if (result.Succeeded)
                return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not complete Google login");
        }

        return RedirectToPage("/Account/Login");
    }
}
