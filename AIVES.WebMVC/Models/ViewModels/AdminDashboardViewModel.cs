namespace AIVES.WebMVC.Models.ViewModels;

public sealed class AdminDashboardViewModel
{
    public string Environment { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public bool IsHealthy { get; set; }
    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int OkCount { get; set; }

    public bool DatabaseIsReachable { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public int PendingMigrationCount { get; set; }
    public int UserCount { get; set; }
    public int ConfirmedUserCount { get; set; }
    public int QuestionCount { get; set; }
    public int RubricCount { get; set; }
    public int BloomLevelCount { get; set; }

    public IReadOnlyList<AdminCheckViewModel> Checks { get; set; } = [];
}

public sealed class AdminCheckViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusClass { get; set; } = string.Empty;
    public string? Remedy { get; set; }
}