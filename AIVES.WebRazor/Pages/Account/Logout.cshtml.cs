using AIVES.BLL.Services.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Account;

/// <summary>POST only, so a link or image elsewhere cannot sign the user out.</summary>
public sealed class LogoutModel(IAccountService accounts) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        await accounts.LogoutAsync();
        return RedirectToPage("/Account/Login");
    }
}
