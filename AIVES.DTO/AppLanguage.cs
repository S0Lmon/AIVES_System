namespace AIVES.DTO;

/// <summary>
/// Languages the user interface can be displayed in. English is the default.
/// </summary>
public enum AppLanguage
{
    En = 1,
    Vi = 2
}

public static class AppLanguageExtensions
{
    public const string Default = "en";

    public static IEnumerable<AppLanguage> Supported => Enum.GetValues<AppLanguage>();

    public static string ToCultureCode(this AppLanguage language) => language switch
    {
        AppLanguage.Vi => "vi",
        _ => Default
    };

    public static AppLanguage ToLanguage(this string? cultureCode) =>
        cultureCode?.Trim().ToLowerInvariant() switch
        {
            "vi" or "vi-vn" => AppLanguage.Vi,
            _ => AppLanguage.En
        };

    public static string GetShortLabel(this AppLanguage language) => language switch
    {
        AppLanguage.Vi => "VI",
        _ => "EN"
    };
}