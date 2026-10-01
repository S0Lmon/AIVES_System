using System.Globalization;
using AIVES.DTO;
using Microsoft.AspNetCore.Localization;

namespace AIVES.WebMVC;

/// <summary>
/// Resolves the request culture from the language cookie, falling back to the
/// <c>culture</c> query string. Values are always normalized through <see cref="AppLanguage"/>.
/// </summary>
public sealed class AppLanguageCultureProvider : RequestCultureProvider
{
    public const string QueryKey = "culture";

    private readonly string _cookieName;

    public AppLanguageCultureProvider(string cookieName) => _cookieName = cookieName;

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext) =>
        Task.FromResult(DetermineCulture(httpContext));

    private ProviderCultureResult? DetermineCulture(HttpContext httpContext)
    {
        var requested = httpContext.Request.Query[QueryKey].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requested) && httpContext.Request.Cookies.TryGetValue(_cookieName, out var cookieValue))
            requested = cookieValue;

        if (string.IsNullOrWhiteSpace(requested))
            return null;

        var culture = new CultureInfo(requested.ToLanguage().ToCultureCode());
        return new ProviderCultureResult(culture.Name, culture.Name);
    }
}