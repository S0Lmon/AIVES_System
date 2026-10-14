using AIVES.DTO.Localization;
using Microsoft.Extensions.Localization;

namespace AIVES.WebRazor;

/// <summary>
/// Lets data annotations use the app's own catalog: [Display(Name = ...)] and ErrorMessage are written
/// in English and translated by <see cref="L10n"/> for the request culture, like the rest of the UI.
/// </summary>
public sealed class L10nStringLocalizer : IStringLocalizer
{
    public static readonly L10nStringLocalizer Instance = new();

    public LocalizedString this[string name] => new(name, L10n.T(name));

    public LocalizedString this[string name, params object[] arguments] => new(name, L10n.Format(name, arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
