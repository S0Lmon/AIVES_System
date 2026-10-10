using AIVES.BLL.Services.Accounts;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Account;

public sealed class ResendCodeModel(IAccountService accounts, ILogger<ResendCodeModel> logger) : PageModel
{
    public async Task<IActionResult> OnPostAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            await accounts.ResendCodeAsync(email, cancellationToken);
            TempData["AuthMessage"] = L10n.T("If the account is valid, a new code has been sent.");
        }
        catch (InvalidOperationException ex)
        {
            TempData["AuthMessage"] = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not resend verification email");
            TempData["AuthMessage"] = L10n.T("Could not send the verification code. Please try again.");
        }

        return RedirectToPage("/Account/VerifyEmail", new { email });
    }
}
