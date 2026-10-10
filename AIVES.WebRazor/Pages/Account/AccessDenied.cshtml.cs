using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Account;

public sealed class AccessDeniedModel : PageModel
{
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Page() : Challenge();
}
