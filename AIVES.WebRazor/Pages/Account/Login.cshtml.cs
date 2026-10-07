using System.ComponentModel.DataAnnotations;
using AIVES.BLL.Services.Accounts;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Account;

public sealed class LoginModel(IAccountService accounts, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Ghi nhớ đăng nhập")]
        public bool RememberMe { get; set; }
    }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl) : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            var result = await accounts.LoginAsync(Input.Email, Input.Password, Input.RememberMe);
            if (result.Succeeded)
                return LocalRedirect(SafeReturnUrl);
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, L10n.T(error));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sign-in failed");
            ModelState.AddModelError(string.Empty, "Không thể đăng nhập lúc này. Vui lòng thử lại.");
        }
        return Page();
    }

    private string SafeReturnUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Content("~/");
}
