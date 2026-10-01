using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using AIVES.BLL.Services.Email;
using AIVES.BLL.Services.Gemini;
using AIVES.BLL.Services.Ai;

namespace AIVES.BLL.Services.Diagnostics;

public sealed class SystemCheckService(IDiagnosticsRepository diagnostics, IAppEmailSender emailSender,
    IOptions<GeminiOptions> gemini, IOptions<OllamaOptions> ollama, IQuestionGeneratorRouter generator,
    IOptions<GmailSmtpOptions> smtp, IConfiguration configuration) : ISystemCheckService
{
    public async Task<SystemCheckReportDto> BuildReportAsync(CancellationToken cancellationToken = default)
    {
        var database = await diagnostics.GetDatabaseStatusAsync(cancellationToken);
        var checks = new List<SystemCheckItemDto>
        {
            CheckDatabase(database),
            CheckAiProviders(gemini.Value, ollama.Value, generator.ActiveProvider),
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

    private static SystemCheckItemDto CheckAiProviders(GeminiOptions gemini, OllamaOptions ollama, AiProvider? active)
    {
        var geminiReady = !string.IsNullOrWhiteSpace(gemini.ApiKey);
        var ollamaReady = ollama.Enabled;

        if (!geminiReady && !ollamaReady)
        {
            return new SystemCheckItemDto("ai", L10n.T("AI providers"),
                L10n.T("Neither Gemini nor Ollama is configured, so AI question generation is blocked."),
                SystemCheckStatus.Critical,
                "dotnet user-secrets set \"Gemini:ApiKey\" \"<GEMINI_API_KEY>\" --project AIVES.WebMVC, or start Ollama and set Ollama:Enabled");
        }

        if (!geminiReady)
        {
            return new SystemCheckItemDto("ai", L10n.T("AI providers"),
                L10n.Format("Gemini has no API key, so the local Ollama model ({0}) serves every request.", ollama.Model),
                SystemCheckStatus.Warning,
                L10n.T("Optional. Set Gemini:ApiKey to use Gemini and keep Ollama as a fallback."));
        }

        var model = string.IsNullOrWhiteSpace(gemini.Model) ? L10n.T("(model not set)") : gemini.Model;
        if (!ollamaReady)
        {
            return new SystemCheckItemDto("ai", L10n.T("AI providers"),
                L10n.Format("Gemini is active with model {0}. Ollama is disabled, so there is no fallback.", model),
                SystemCheckStatus.Warning,
                L10n.T("Optional. Set Ollama:Enabled to true so requests fall back to a local model when Gemini fails."));
        }

        return new SystemCheckItemDto("ai", L10n.T("AI providers"),
            L10n.Format("Gemini (model {0}) is active and Ollama ({1}) is the fallback. Active now: {2}.", model, ollama.Model, active?.ToString() ?? "none"),
            SystemCheckStatus.Ok);
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