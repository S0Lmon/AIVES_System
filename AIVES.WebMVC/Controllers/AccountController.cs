using System.Security.Claims;
using AIVES.WebMVC.Models.Entities;
using AIVES.WebMVC.Models.ViewModels;
using AIVES.WebMVC.Services.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

public sealed class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailVerificationService _verificationService;
    private readonly IAppEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController> _logger;

    public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
        IEmailVerificationService verificationService, IAppEmailSender emailSender, IConfiguration configuration,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _verificationService = verificationService;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    private bool IsGoogleConfigured => !string.IsNullOrWhiteSpace(_configuration["Authentication:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(_configuration["Authentication:Google:ClientSecret"]);

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl, IsGoogleConfigured = IsGoogleConfigured });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        model.IsGoogleConfigured = IsGoogleConfigured;
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null || !user.EmailConfirmed)
        {
            ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng, hoặc tài khoản chưa được xác minh.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
            return LocalRedirect(string.IsNullOrWhiteSpace(model.ReturnUrl) ? Url.Action("Index", "Home")! : model.ReturnUrl);

        ModelState.AddModelError(string.Empty, result.IsLockedOut ? "Tài khoản tạm khóa do đăng nhập sai nhiều lần." : "Email hoặc mật khẩu không đúng.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel { IsEmailConfigured = _emailSender.IsConfigured, IsGoogleConfigured = IsGoogleConfigured });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        model.IsEmailConfigured = _emailSender.IsConfigured;
        model.IsGoogleConfigured = IsGoogleConfigured;
        if (!model.Email.Trim().EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(model.Email), "Vui lòng sử dụng địa chỉ @gmail.com.");
        if (!_emailSender.IsConfigured)
            ModelState.AddModelError(string.Empty, "Gmail SMTP chưa được cấu hình nên chưa thể gửi mã xác minh.");
        if (!ModelState.IsValid)
            return View(model);

        var user = new ApplicationUser { UserName = model.Email.Trim(), Email = model.Email.Trim(), DisplayName = model.DisplayName.Trim() };
        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        try
        {
            await _verificationService.IssueAndSendAsync(user.Id, user.Email!, user.DisplayName, cancellationToken);
            return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send registration verification email");
            await _userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "Không thể gửi mã xác minh. Vui lòng kiểm tra cấu hình Gmail và thử lại.");
            return View(model);
        }
    }

    [HttpGet]
    public IActionResult VerifyEmail(string email) => View(new VerifyEmailViewModel { Email = email });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);
        var user = await _userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null || !await _verificationService.VerifyAsync(user.Id, model.Code, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Code), "Mã không đúng, đã hết hạn hoặc vượt quá số lần thử.");
            return View(model);
        }
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);
        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendCode(string email, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user is not null && !user.EmailConfirmed && _emailSender.IsConfigured)
                await _verificationService.IssueAndSendAsync(user.Id, user.Email!, user.DisplayName, cancellationToken);
            TempData["AuthMessage"] = "Nếu tài khoản hợp lệ, một mã mới đã được gửi.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["AuthMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(VerifyEmail), new { email });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string? returnUrl = null)
    {
        if (!IsGoogleConfigured)
            return RedirectToAction(nameof(Login));
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(GoogleDefaults.AuthenticationScheme, redirectUrl);
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
            return RedirectToAction(nameof(Login));
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
            return RedirectToAction(nameof(Login));

        var signInResult = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false);
        if (!signInResult.Succeeded)
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return RedirectToAction(nameof(Login));
            var user = await _userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email.Split('@')[0]
                };
                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return RedirectToAction(nameof(Login));
            }
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
            var loginResult = await _userManager.AddLoginAsync(user, info);
            if (!loginResult.Succeeded && !loginResult.Errors.Any(error => error.Code.Contains("LoginAlreadyAssociated", StringComparison.OrdinalIgnoreCase)))
                return RedirectToAction(nameof(Login));
            await _signInManager.SignInAsync(user, isPersistent: false);
        }
        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? Url.Action("Index", "Home")! : returnUrl);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }
}
