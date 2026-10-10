using AIVES.BLL.Services.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Profile;

public sealed class LogoutModel(IAccountService accounts) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Profile/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await accounts.LogoutAsync();
        return RedirectToPage("/Account/Login");
    }
}
