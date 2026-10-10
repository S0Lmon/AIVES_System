using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages;

public sealed class SetLanguageModel : PageModel
{
    public IActionResult OnPost(AppLanguage language, string? returnUrl)
    {
        if (!AppLanguageExtensions.Supported.Contains(language))
            return BadRequest();

        Response.Cookies.Append(RazorPresentation.LanguageCookieName, language.ToCultureCode(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            HttpOnly = false,
            SameSite = SameSiteMode.Lax
        });

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Page("/Index")!);
    }
}
