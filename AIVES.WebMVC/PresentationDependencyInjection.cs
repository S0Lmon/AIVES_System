using System.Globalization;
using AIVES.DTO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;

namespace AIVES.WebMVC;

public static class PresentationDependencyInjection
{
    public const string LanguageCookieName = "AIVES.Language";

    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Cookie.Name = "AIVES.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        var googleClientId = configuration["Authentication:Google:ClientId"];
        var googleClientSecret = configuration["Authentication:Google:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
        {
            services.AddAuthentication().AddGoogle(options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleClientSecret;
            });
        }
        services.AddAuthorization(options =>
            options.AddPolicy(AuthorizationPolicies.Staff, policy => policy.RequireRole(AppRoles.Admin, AppRoles.Lecturer)));
        services.AddSingleton<DisplayTimeZone>();
        services.AddControllersWithViews();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supported = AppLanguageExtensions.Supported.Select(language => new CultureInfo(language.ToCultureCode())).ToList();
            options.DefaultRequestCulture = new RequestCulture(AppLanguageExtensions.Default);
            options.SupportedCultures = supported;
            options.SupportedUICultures = supported;
            options.RequestCultureProviders = [new AppLanguageCultureProvider(LanguageCookieName)];
        });
        return services;
    }
}
