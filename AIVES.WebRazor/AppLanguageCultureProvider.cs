using AIVES.DTO;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

namespace AIVES.WebRazor;

public sealed class AppLanguageCultureProvider(string cookieName) : RequestCultureProvider
{
    public const string QueryKey = "culture";

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var requested = httpContext.Request.Query[QueryKey].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requested)
            && httpContext.Request.Cookies.TryGetValue(cookieName, out var cookieValue))
            requested = cookieValue;

        if (string.IsNullOrWhiteSpace(requested))
            return Task.FromResult<ProviderCultureResult?>(null);

        var culture = new CultureInfo(requested.ToLanguage().ToCultureCode());
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture.Name, culture.Name));
    }
}
