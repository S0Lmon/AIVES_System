using AIVES.DTO;
using AIVES.WebMVC.Models.ViewModels;
using AIVES.BLL.Services.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AIVES.WebMVC.Controllers;

public sealed class AccountController(IAccountService accounts, ILogger<AccountController> logger) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl, IsGoogleConfigured = accounts.IsGoogleConfigured });
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        model.IsGoogleConfigured = accounts.IsGoogleConfigured;
        if (!ModelState.IsValid)
            return View(model);
        try
        {
            var result = await accounts.LoginAsync(model.Email, model.Password, model.RememberMe);
            if (result.Succeeded)
                return RedirectLocally(model.ReturnUrl);
            AddErrors(result);
        }
        catch (Exception ex) { ReportError(ex); }
        return View(model);
    }
    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel { IsEmailConfigured = accounts.IsEmailConfigured, IsGoogleConfigured = accounts.IsGoogleConfigured });
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        model.IsEmailConfigured = accounts.IsEmailConfigured;
        model.IsGoogleConfigured = accounts.IsGoogleConfigured;
        if (!ModelState.IsValid)
            return View(model);
        try
        {
            var result = await accounts.RegisterAsync(new RegisterRequest(model.Email, model.Password, model.DisplayName), cancellationToken);
            if (result.Succeeded)
                return RedirectToAction(nameof(VerifyEmail), new
                {
                    email = result.User!.Email
                });
            AddErrors(result);
        }
        catch (Exception ex) { ReportError(ex); }
        return View(model);
    }
    [HttpGet]
    public IActionResult VerifyEmail(string email) => View(new VerifyEmailViewModel { Email = email });
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);
        try
        {
            var result = await accounts.VerifyEmailAsync(model.Email, model.Code, cancellationToken);
            if (result.Succeeded)
                return RedirectToAction("Index", "Home");
            AddErrors(result);
        }
        catch (Exception ex) { ReportError(ex); }
        return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendCode(string email, CancellationToken cancellationToken)
    {
        try
        {
            await accounts.ResendCodeAsync(email, cancellationToken);
            TempData["AuthMessage"] = "Nếu tài khoản hợp lệ, một mã mới đã được gửi.";
        }
        catch (InvalidOperationException ex) { TempData["AuthMessage"] = ex.Message; }
        catch (Exception ex) { logger.LogError(ex, "Could not resend verification email"); TempData["AuthMessage"] = "Không thể gửi mã xác minh. Vui lòng thử lại."; }
        return RedirectToAction(nameof(VerifyEmail), new
        {
            email
        });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string? returnUrl = null)
    {
        if (!accounts.IsGoogleConfigured)
            return RedirectToAction(nameof(Login));
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new
        {
            returnUrl
        });
        return Challenge(accounts.ConfigureGoogleLogin(redirectUrl), "Google");
    }
    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
            return RedirectToAction(nameof(Login));
        try
        {
            var result = await accounts.CompleteGoogleLoginAsync();
            if (result.Succeeded)
                return RedirectLocally(returnUrl);
        }
        catch (Exception ex) { logger.LogError(ex, "Could not complete Google login"); }
        return RedirectToAction(nameof(Login));
    }
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await accounts.LogoutAsync();
        return RedirectToAction(nameof(Login));
    }
    private IActionResult RedirectLocally(string? returnUrl) => LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action("Index", "Home")!);
    private void AddErrors(AccountResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error);
    }
    private void ReportError(Exception ex)
    {
        logger.LogError(ex, "Account operation failed");
        ModelState.AddModelError(string.Empty, "Đã có lỗi khi xử lý tài khoản. Vui lòng thử lại.");
    }
}
