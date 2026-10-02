using AIVES.DAL;
using AIVES.BLL.Services;
using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services.Gemini;
using AIVES.BLL.Services.Email;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Diagnostics;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace AIVES.BLL;

public static class DependencyInjection
{
    public static IServiceCollection AddAives(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDataAccess(configuration, options =>
        {
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            options.User.RequireUniqueEmail = true;
        });
        services.AddScoped<IBloomLevelService, BloomLevelService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IExamService, ExamService>();
        services.AddSingleton(TimeProvider.System);
        // Role changes bump the security stamp; re-check it every minute so a demoted lecturer
        // loses access promptly instead of after the 30 minute default.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));
        services.AddScoped<IQuestionService, QuestionService>();
        services.AddScoped<IRubricService, RubricService>();
        services.AddScoped<ISystemCheckService, SystemCheckService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        // Each generator needs its own client name: AddHttpClient<TClient, TImpl> otherwise names the
        // client after the shared interface, and the Ollama BaseAddress would also apply to Gemini.
        services.AddHttpClient<IQuestionGenerator, GeminiQuestionGenerator>(nameof(GeminiQuestionGenerator), client =>
        {
            client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            client.Timeout = TimeSpan.FromSeconds(120);
        }).AddHttpMessageHandler(() => new TransientRetryHandler());
        services.AddHttpClient<IQuestionGenerator, OllamaQuestionGenerator>(nameof(OllamaQuestionGenerator), (provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(30, options.TimeoutSeconds));
        });
        services.AddScoped<IQuestionGeneratorRouter, QuestionGeneratorRouter>();
        services.AddHttpClient<IRubricGenerator, GeminiRubricGenerator>(nameof(GeminiRubricGenerator), client =>
        {
            client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            client.Timeout = TimeSpan.FromSeconds(150);
        }).AddHttpMessageHandler(() => new TransientRetryHandler());
        services.AddHttpClient<IRubricGenerator, OllamaRubricGenerator>(nameof(OllamaRubricGenerator), (provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(60, options.TimeoutSeconds * 2));
        });
        services.AddScoped<IRubricGeneratorRouter, RubricGeneratorRouter>();
        services.Configure<GmailSmtpOptions>(configuration.GetSection(GmailSmtpOptions.SectionName));
        services.AddScoped<IAppEmailSender, GmailSmtpEmailSender>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddLogging(configure =>
        {
            configure.AddConsole();
            configure.AddDebug();
        });

        return services;
    }
    public static Task InitializeAivesAsync(this IServiceProvider services, IConfiguration configuration, bool isDevelopment) => services.InitializeDataAccessAsync(configuration, isDevelopment);
}
