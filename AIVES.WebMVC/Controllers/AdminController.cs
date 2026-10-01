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
    private readonly ILogger<AdminController> _logger;

    public AdminController(ISystemCheckService systemChecks, ILogger<AdminController> logger)
    {
        _systemChecks = systemChecks;
        _logger = logger;
    }

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