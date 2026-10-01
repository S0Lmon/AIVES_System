using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using AIVES.BLL.Services.Email;
using AIVES.BLL.Services.Gemini;

namespace AIVES.BLL.Services.Diagnostics;

public sealed class SystemCheckService(IDiagnosticsRepository diagnostics, IAppEmailSender emailSender,
    IOptions<GeminiOptions> gemini, IOptions<GmailSmtpOptions> smtp, IConfiguration configuration) : ISystemCheckService
{
    public async Task<SystemCheckReportDto> BuildReportAsync(CancellationToken cancellationToken = default)
    {
        var database = await diagnostics.GetDatabaseStatusAsync(cancellationToken);
        var checks = new List<SystemCheckItemDto>
        {
            CheckDatabase(database),
            CheckGemini(gemini.Value),
            CheckSmtp(smtp.Value, emailSender.IsConfigured),
            CheckGoogleOAuth(),
            CheckProductionSecrets()
        };

        return new SystemCheckReportDto(
            configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"] ?? "Unknown",
            DateTimeOffset.UtcNow,
            database,
            checks);
    }

    private static SystemCheckItemDto CheckDatabase(DatabaseDiagnosticsDto database)
    {
        if (!database.IsReachable)
        {
            return new SystemCheckItemDto("database", "SQL Server", database.Error ?? L10n.T("Could not connect to SQL Server."), SystemCheckStatus.Critical,
                L10n.T("Check the SQL Server service and the ConnectionStrings:DefaultConnection value."));
        }

        if (database.PendingMigrations.Count > 0)
        {
            return new SystemCheckItemDto("database", "SQL Server",
                L10n.Format("{0}/{1} has {2} pending migrations: {3}.", database.ServerName, database.DatabaseName, database.PendingMigrations.Count, string.Join(", ", database.PendingMigrations)),
                SystemCheckStatus.Critical,
                L10n.T("The application has not finished migrating. Check the startup log or restart the application to apply migrations."));
        }

        return new SystemCheckItemDto("database", "SQL Server",
            L10n.Format("{0}/{1} is connected and migrations are up to date.", database.ServerName, database.DatabaseName),
            SystemCheckStatus.Ok);
    }

    private static SystemCheckItemDto CheckGemini(GeminiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new SystemCheckItemDto("gemini", "Gemini API",
                L10n.T("Gemini:ApiKey is not set, so AI question generation is blocked."),
                SystemCheckStatus.Critical,
                "dotnet user-secrets set \"Gemini:ApiKey\" \"<GEMINI_API_KEY>\" --project AIVES.WebMVC");
        }

        var model = string.IsNullOrWhiteSpace(options.Model) ? L10n.T("(model not set)") : options.Model;
        return new SystemCheckItemDto("gemini", "Gemini API", L10n.Format("API key present. Model in use: {0}.", model), SystemCheckStatus.Ok);
    }

    private static SystemCheckItemDto CheckSmtp(GmailSmtpOptions options, bool isConfigured)
    {
        if (!isConfigured)
        {
            return new SystemCheckItemDto("smtp", "Gmail SMTP",
                L10n.T("GmailSmtp:Username or GmailSmtp:AppPassword is not set, so verification codes cannot be sent and new accounts cannot be registered."),
                SystemCheckStatus.Critical,
                "dotnet user-secrets set \"GmailSmtp:Username\" \"<GMAIL_ADDRESS>\" --project AIVES.WebMVC (and GmailSmtp:AppPassword)");
        }

        return new SystemCheckItemDto("smtp", "Gmail SMTP", L10n.Format("Mail is configured through {0}:{1} with the sender name {2}.", options.Host, options.Port, options.SenderName), SystemCheckStatus.Ok);
    }

    private SystemCheckItemDto CheckGoogleOAuth()
    {
        var hasClientId = !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]);
        var hasClientSecret = !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);

        if (!hasClientId && !hasClientSecret)
        {
            return new SystemCheckItemDto("google-oauth", L10n.T("Google sign in"),
                L10n.T("ClientId/ClientSecret are not set, so Google sign in is disabled."),
                SystemCheckStatus.Warning,
                L10n.T("Optional. Set Authentication:Google:ClientId and ClientSecret to enable Google sign in."));
        }

        if (!hasClientId || !hasClientSecret)
        {
            return new SystemCheckItemDto("google-oauth", L10n.T("Google sign in"),
                L10n.T("Only one of ClientId/ClientSecret is set, so Google sign in still does not work."),
                SystemCheckStatus.Warning,
                L10n.T("Both Authentication:Google:ClientId and Authentication:Google:ClientSecret are required."));
        }

        return new SystemCheckItemDto("google-oauth", L10n.T("Google sign in"), L10n.T("ClientId and ClientSecret are set."), SystemCheckStatus.Ok);
    }

    private SystemCheckItemDto CheckProductionSecrets()
    {
        var environment = configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"];
        if (!string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return new SystemCheckItemDto("environment", L10n.T("Runtime environment"), L10n.Format("Running the {0} environment.", environment), SystemCheckStatus.Ok);
        }

        var usesLocalDb = (configuration.GetConnectionString("DefaultConnection") ?? string.Empty).Contains("localdb", StringComparison.OrdinalIgnoreCase);
        if (usesLocalDb)
        {
            return new SystemCheckItemDto("environment", L10n.T("Runtime environment"),
                L10n.T("Running Production but the connection string still points at LocalDB, which will not work when deployed."),
                SystemCheckStatus.Critical,
                L10n.T("Set ConnectionStrings:DefaultConnection to an environment variable pointing at a real SQL Server."));
        }

        return new SystemCheckItemDto("environment", L10n.T("Runtime environment"), L10n.T("Running the Production environment with an external connection string."), SystemCheckStatus.Ok);
    }
}