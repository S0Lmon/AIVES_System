using System.ComponentModel.DataAnnotations;
using AIVES.BLL.Services.Accounts;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Account;

public sealed class RegisterModel(IAccountService accounts, ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public IReadOnlyList<string> AllowedEmailDomains => accounts.AllowedEmailDomains;
    public bool IsGoogleConfigured => accounts.IsGoogleConfigured;

    public sealed class RegisterInput
    {
        [Required(ErrorMessage = "Please enter your full name.")]
        [StringLength(120, MinimumLength = 2)]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email.")]
        [EmailAddress(ErrorMessage = "The email address is not valid.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a password.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "The password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The password confirmation does not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            var result = await accounts.RegisterAsync(
                new RegisterRequest(Input.Email, Input.Password, Input.DisplayName), cancellationToken);
            if (result.Succeeded)
                return RedirectToPage("/Account/VerifyEmail", new { email = result.User!.Email });

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, L10n.T(error));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Registration failed");
            ModelState.AddModelError(string.Empty, L10n.T("Something went wrong while processing your account. Please try again."));
        }

        return Page();
    }
}
