using System.Globalization;
using System.Text.Json.Serialization;
using AIVES.DTO;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Localization;

namespace AIVES.WebRazor;

/// <summary>Authorization policy names used by page conventions and the hub.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Lecturers and administrators: the question bank, rubric bank and catalogue.</summary>
    public const string Staff = "Staff";
    public const string Admin = "Admin";
}

public static class RazorPresentation
{
    public const string AuthCookieName = "AIVES.Auth";
    public const string LanguageCookieName = "AIVES.Language";

    public static IServiceCollection AddRazorPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Cookie.Name = AuthCookieName;
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

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Staff, policy => policy.RequireRole(AppRoles.Admin, AppRoles.Lecturer))
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(AppRoles.Admin));

        // Authorization lives in conventions, so a new page in a folder is protected by default.
        services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/");
            options.Conventions.AllowAnonymousToFolder("/Account");
            options.Conventions.AllowAnonymousToPage("/Error");
            options.Conventions.AuthorizeFolder("/Questions", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Subjects", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Rubrics", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Catalog", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Glossary", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Exams", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Grading", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Reports", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Admin", AuthorizationPolicies.Admin);
            options.Conventions.AuthorizePage("/Interviews/Monitor", AuthorizationPolicies.Staff);
        });

        services.AddHttpContextAccessor();
        services.AddSingleton<DisplayTimeZone>();
        services.AddSignalR(options =>
            {
                // Room for audio chunks that arrive while Whisper is busy with the previous ones.
                options.StreamBufferCapacity = 64;
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.PayloadSerializerOptions.Converters.Add(new UtcDateTimeConverter());
            });
        services.AddSingleton<PresenceTracker>();
        services.AddSingleton<AnswerSessions>();
        services.AddScoped<ILiveUpdates, LiveUpdates>();

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
