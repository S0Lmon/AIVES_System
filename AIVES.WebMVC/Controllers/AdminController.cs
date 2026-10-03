using System.Security.Claims;
using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services.Diagnostics;
using AIVES.BLL.Services.Operations;
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
    private readonly ISystemSettingsService _settings;
    private readonly ISubjectAccessService _subjects;
    private readonly IAuditService _audit;
    private readonly ILogger<AdminController> _logger;

    public AdminController(ISystemCheckService systemChecks, IUserAdminService users, ISystemSettingsService settings,
        ISubjectAccessService subjects, IAuditService audit, ILogger<AdminController> logger)
    {
        _systemChecks = systemChecks;
        _users = users;
        _settings = settings;
        _subjects = subjects;
        _audit = audit;
        _logger = logger;
    }

    private string? ActingEmail => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDisabled(string userId, bool disabled)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var result = await _users.SetDisabledAsync(userId, disabled, currentUserId, ActingEmail);
            if (result.Succeeded)
                TempData["UserMessage"] = L10n.Format(disabled ? "{0} can no longer sign in." : "{0} can sign in again.", result.User?.Email ?? userId);
            else
                TempData["UserError"] = string.Join(" ", result.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not change the sign-in state of {UserId}", userId);
            TempData["UserError"] = L10n.T("The account could not be changed. Please try again.");
        }
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        var speech = await _settings.GetSpeechAsync(cancellationToken);
        return View(new SpeechSettingsViewModel
        {
            DefaultLanguage = speech.DefaultLanguage,
            EnableVietnamese = speech.EnabledLanguages.Contains(AppLanguage.Vi),
            EnableEnglish = speech.EnabledLanguages.Contains(AppLanguage.En),
            SpeechRate = speech.SpeechRate,
            VietnameseVoice = speech.VietnameseVoice,
            EnglishVoice = speech.EnglishVoice,
            RecordingRetentionDays = speech.RecordingRetentionDays,
            Message = TempData["SettingsMessage"] as string
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(SpeechSettingsViewModel model, CancellationToken cancellationToken)
    {
        var enabled = new List<AppLanguage>();
        if (model.EnableVietnamese) enabled.Add(AppLanguage.Vi);
        if (model.EnableEnglish) enabled.Add(AppLanguage.En);
        var speech = new SpeechSettingsDto(model.DefaultLanguage, enabled, model.SpeechRate, model.VietnameseVoice, model.EnglishVoice, model.RecordingRetentionDays);
        try
        {
            await _settings.SaveSpeechAsync(speech, cancellationToken);
            await _audit.WriteAsync(new AuditEntryInput(AuditActions.SettingsChanged, User.FindFirstValue(ClaimTypes.NameIdentifier), ActingEmail,
                Details: $"default {speech.DefaultLanguage}, enabled {string.Join("/", enabled)}, rate {speech.SpeechRate:0.00}, voices '{speech.VietnameseVoice}'/'{speech.EnglishVoice}', retention {speech.RecordingRetentionDays} days"),
                cancellationToken);
            TempData["SettingsMessage"] = L10n.T("The settings were saved.");
            return RedirectToAction(nameof(Settings));
        }
        catch (ArgumentException ex)
        {
            model.Error = ex.Message;
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Subjects(CancellationToken cancellationToken)
    {
        var lecturers = (await _users.ListUsersAsync())
            .Where(user => user.Roles.Contains(AppRoles.Lecturer))
            .Select(user => new UserRefDto(user.Id, user.Email, user.DisplayName))
            .OrderBy(user => user.Email)
            .ToList();
        return View(new SubjectAssignmentsViewModel
        {
            Subjects = await _subjects.ListAssignmentsAsync(cancellationToken),
            Lecturers = lecturers,
            Message = TempData["SubjectMessage"] as string
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subjects(int subjectId, List<string>? lecturerIds, CancellationToken cancellationToken)
    {
        var lecturers = (await _users.ListUsersAsync()).Where(user => user.Roles.Contains(AppRoles.Lecturer)).ToDictionary(user => user.Id);
        var chosen = (lecturerIds ?? []).Where(lecturers.ContainsKey).Distinct().ToList();
        await _subjects.SetLecturersAsync(subjectId, chosen, cancellationToken);
        await _audit.WriteAsync(new AuditEntryInput(AuditActions.SubjectLecturersChanged, User.FindFirstValue(ClaimTypes.NameIdentifier), ActingEmail,
            Details: $"Subject {subjectId}: {(chosen.Count == 0 ? "open to all lecturers" : string.Join(", ", chosen.Select(id => lecturers[id].Email)))}"), cancellationToken);
        TempData["SubjectMessage"] = L10n.T("The lecturers of the subject were saved.");
        return RedirectToAction(nameof(Subjects));
    }

    [HttpGet]
    public async Task<IActionResult> Audit(string? action, string? actor, CancellationToken cancellationToken) =>
        View("Audit", new AuditViewModel
        {
            Title = L10n.T("Audit log"),
            Action = action,
            Actor = actor,
            Entries = await _audit.QueryAsync(new AuditQuery(Action: action, Actor: actor, Take: 500), cancellationToken)
        });

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
                IsCurrentUser = user.Id == currentUserId,
                IsDisabled = user.IsDisabled
            }).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string userId, string role)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        try
        {
            var result = await _users.SetRoleAsync(userId, role, currentUserId, ActingEmail);
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