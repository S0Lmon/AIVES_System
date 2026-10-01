namespace AIVES.DTO;

public static class AdminRoles
{
    public const string RoleName = "Admin";
}

public enum SystemCheckStatus
{
    Ok = 0,
    Warning = 1,
    Critical = 2
}

public sealed record SystemCheckItemDto(
    string Key,
    string Name,
    string Detail,
    SystemCheckStatus Status,
    string? Remedy = null);

public sealed record DatabaseDiagnosticsDto(
    bool IsReachable,
    string ServerName,
    string DatabaseName,
    IReadOnlyList<string> PendingMigrations,
    int UserCount,
    int ConfirmedUserCount,
    int QuestionCount,
    int RubricCount,
    int BloomLevelCount,
    string? Error = null);

public sealed record SystemCheckReportDto(
    string Environment,
    DateTimeOffset GeneratedAtUtc,
    DatabaseDiagnosticsDto Database,
    IReadOnlyList<SystemCheckItemDto> Checks)
{
    public int CriticalCount => Checks.Count(check => check.Status == SystemCheckStatus.Critical);
    public int WarningCount => Checks.Count(check => check.Status == SystemCheckStatus.Warning);
    public int OkCount => Checks.Count(check => check.Status == SystemCheckStatus.Ok);
    public bool IsHealthy => CriticalCount == 0;
}