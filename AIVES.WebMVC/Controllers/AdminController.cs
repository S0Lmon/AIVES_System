using System.Security.Claims;
using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services.Diagnostics;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebMVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIVES.WebMVC.Controllers;

[Authorize(Roles = AdminRoles.RoleName)]
public sealed class AdminController : Controller
{
    private readonly ISystemCheckService _systemChecks;
    private readonly IUserAdminService _users;
    private readonly ILogger<AdminController> _logger;

    public AdminController(ISystemCheckService systemChecks, IUserAdminService users, ILogger<AdminController> logger)
    {
        _systemChecks = systemChecks;
        _users = users;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Users()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var users = await _users.ListUsersAsync();
        return View(new UserManagementViewModel
        {
            Message = TempData["UserMessage"] as string,
            Error = TempData["UserError"] as string,
            Users = users.Select(user => new UserRowViewModel
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                EmailConfirmed = user.EmailConfirmed,
                CreatedAtUtc = user.CreatedAtUtc,
                IsAdmin = user.Roles.Contains(AppRoles.Admin),
                Role = user.Roles.FirstOrDefault(role => AppRoles.Assignable.Contains(role)),
                IsCurrentUser = user.Id == currentUserId
            }).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string userId, string role)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var result = await _users.SetRoleAsync(userId, role, currentUserId);
            if (result.Succeeded)
                TempData["UserMessage"] = L10n.Format("{0} is now {1}.", result.User?.Email ?? userId, RoleLabel(role));
            else
                TempData["UserError"] = string.Join(" ", result.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not change the role of {UserId}", userId);
            TempData["UserError"] = L10n.T("The role could not be changed. Please try again.");
        }
        return RedirectToAction(nameof(Users));
    }

    public static string RoleLabel(string? role) => role switch
    {
        AppRoles.Admin => L10n.T("Administrator"),
        AppRoles.Lecturer => L10n.T("Lecturer"),
        AppRoles.Student => L10n.T("Student"),
        _ => L10n.T("No role")
    };

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var report = await _systemChecks.BuildReportAsync(cancellationToken);
            return View(ToViewModel(report));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while building the system check report");
            return View(new AdminDashboardViewModel { Environment = "Unknown" });
        }
    }

    private static AdminDashboardViewModel ToViewModel(SystemCheckReportDto report) => new()
    {
        Environment = report.Environment,
        GeneratedAtUtc = report.GeneratedAtUtc,
        IsHealthy = report.IsHealthy,
        CriticalCount = report.CriticalCount,
        WarningCount = report.WarningCount,
        OkCount = report.OkCount,
        DatabaseIsReachable = report.Database.IsReachable,
        ServerName = report.Database.ServerName,
        DatabaseName = report.Database.DatabaseName,
        PendingMigrationCount = report.Database.PendingMigrations.Count,
        UserCount = report.Database.UserCount,
        ConfirmedUserCount = report.Database.ConfirmedUserCount,
        QuestionCount = report.Database.QuestionCount,
        RubricCount = report.Database.RubricCount,
        BloomLevelCount = report.Database.BloomLevelCount,
        Checks = report.Checks.Select(check => new AdminCheckViewModel
        {
            Key = check.Key,
            Name = check.Name,
            Detail = check.Detail,
            Status = check.Status.ToString(),
            StatusLabel = check.Status switch
            {
                SystemCheckStatus.Ok => L10n.T("Configured"),
                SystemCheckStatus.Warning => L10n.T("Warnings"),
                _ => L10n.T("Missing / errors")
            },
            StatusClass = check.Status switch
            {
                SystemCheckStatus.Ok => "ready",
                SystemCheckStatus.Warning => "warning",
                _ => "missing"
            },
            Remedy = check.Remedy
        }).ToList()
    };
}