using AIVES.DAL;
using AIVES.BLL.Services;
using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services.Gemini;
using AIVES.BLL.Services.Email;
using AIVES.BLL.Services.Diagnostics;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
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
        services.AddScoped<IQuestionService, QuestionService>();
        services.AddScoped<IRubricService, RubricService>();
        services.AddScoped<ISystemCheckService, SystemCheckService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.AddHttpClient<IQuestionGenerator, GeminiQuestionGenerator>(client =>
        {
            client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<IQuestionGenerator, OllamaQuestionGenerator>((provider, client) =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(30, options.TimeoutSeconds));
        });
        services.AddScoped<IQuestionGeneratorRouter, QuestionGeneratorRouter>();
        services.AddHttpClient<IRubricGenerator, GeminiRubricGenerator>(client =>
        {
            client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        services.AddHttpClient<IRubricGenerator, OllamaRubricGenerator>((provider, client) =>
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
