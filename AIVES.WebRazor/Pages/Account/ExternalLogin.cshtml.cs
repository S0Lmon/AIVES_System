using AIVES.BLL.Services.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Account;

public sealed class ExternalLoginModel(IAccountService accounts) : PageModel
{
    public IActionResult OnPost(string? returnUrl = null)
    {
        if (!accounts.IsGoogleConfigured)
            return RedirectToPage("/Account/Login");

        var callbackUrl = Url.Page("/Account/ExternalLoginCallback", values: new
        {
            returnUrl
        });
        return Challenge(accounts.ConfigureGoogleLogin(callbackUrl), "Google");
    }
}
