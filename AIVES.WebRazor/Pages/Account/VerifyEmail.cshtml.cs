using System.ComponentModel.DataAnnotations;
using AIVES.BLL.Services.Accounts;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Account;

public sealed class VerifyEmailModel(IAccountService accounts, ILogger<VerifyEmailModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    [Required]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please enter the verification code.")]
    [RegularExpression("^[0-9]{6}$", ErrorMessage = "The verification code must be exactly 6 digits.")]
    public string Code { get; set; } = string.Empty;

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            var result = await accounts.VerifyEmailAsync(Email, Code, cancellationToken);
            if (result.Succeeded)
                return RedirectToPage("/Index");

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, L10n.T(error));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email verification failed");
            ModelState.AddModelError(string.Empty, L10n.T("Something went wrong while processing your account. Please try again."));
        }

        return Page();
    }

    public async Task<IActionResult> OnPostResendCodeAsync(string email, CancellationToken cancellationToken)
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

        return RedirectToPage(new { email });
    }
}
