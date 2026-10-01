using AIVES.BLL.Services.Accounts;
using AIVES.WebMVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

[Authorize]
public sealed class ProfileController(IAccountService accounts, ILogger<ProfileController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        try
        {
            var profile = await accounts.GetProfileAsync(userId);
            if (profile is null)
                return NotFound();

            return View(new ProfileViewModel
            {
                DisplayName = profile.DisplayName,
                Email = profile.Email,
                EmailConfirmed = profile.EmailConfirmed,
                MemberSince = profile.CreatedAtUtc,
                Roles = profile.Roles
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load the profile page");
            return NotFound();
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await accounts.LogoutAsync();
        return RedirectToAction("Login", "Account");
    }
}