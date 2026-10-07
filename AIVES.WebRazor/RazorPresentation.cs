using System.Globalization;
using AIVES.DTO;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Localization;

namespace AIVES.WebRazor;

/// <summary>Authorization policy names used by page conventions and the hub.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Lecturers and administrators: the question bank, rubric bank and catalogue.</summary>
    public const string Staff = "Staff";
}

public static class RazorPresentation
{
    // Cookies are not scoped by port, so this site must not reuse the MVC site's "AIVES.Auth"
    // when both run on localhost.
    public const string AuthCookieName = "AIVES.Razor.Auth";

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

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Staff, policy => policy.RequireRole(AppRoles.Admin, AppRoles.Lecturer));

        // Authorization lives in conventions, so a new page in a folder is protected by default.
        services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/");
            options.Conventions.AllowAnonymousToFolder("/Account");
            options.Conventions.AllowAnonymousToPage("/Error");
            options.Conventions.AuthorizeFolder("/Questions", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Subjects", AuthorizationPolicies.Staff);
            options.Conventions.AuthorizeFolder("/Rubrics", AuthorizationPolicies.Staff);
        });

        services.AddHttpContextAccessor();
        services.AddSignalR();
        services.AddSingleton<PresenceTracker>();
        services.AddScoped<ILiveUpdates, LiveUpdates>();

        // Vietnamese first; BLL messages are English keys that L10n translates for this culture.
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supported = AppLanguageExtensions.Supported.Select(language => new CultureInfo(language.ToCultureCode())).ToList();
            options.DefaultRequestCulture = new RequestCulture(AppLanguage.Vi.ToCultureCode());
            options.SupportedCultures = supported;
            options.SupportedUICultures = supported;
        });
        return services;
    }
}
