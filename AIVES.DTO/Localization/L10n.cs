using System.Globalization;

namespace AIVES.DTO.Localization;

/// <summary>
/// Ambient translation helper. Keys are authored in English, which is also the default
/// language, so an untranslated key degrades to readable English instead of a blank label.
/// </summary>
public static class L10n
{
    public static AppLanguage Current => CultureInfo.CurrentUICulture.Name.ToLanguage();

    public static bool Is(AppLanguage language) => Current == language;

    /// <summary>Translates an English source string for the current request culture.</summary>
    public static string T(string english) => AppText.Resolve(english);

    /// <summary>Translates a composite English format string and fills its placeholders.</summary>
    public static string Format(string english, params object?[] arguments)
    {
        var template = AppText.Resolve(english);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}