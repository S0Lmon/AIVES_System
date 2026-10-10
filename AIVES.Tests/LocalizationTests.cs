using AIVES.DTO.Localization;
using System.Globalization;

namespace AIVES.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void VietnameseTableHasNoKeysThatDifferOnlyByCase()
    {
        // The table is OrdinalIgnoreCase, and a dictionary initialiser using ["key"] = value goes
        // through the indexer rather than Add. A near duplicate therefore overwrites silently
        // instead of throwing, so this has to be checked explicitly.
        var table = AppText.VietnameseEntries;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collisions = table.Keys.Where(key => !seen.Add(key)).ToList();

        Assert.Empty(collisions);
    }

    [Fact]
    public void AnUnknownKeyFallsBackToTheEnglishSource()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("vi-VN");
            Assert.Equal("A phrase nobody translated", L10n.T("A phrase nobody translated"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("Where it belongs")]
    [InlineData("Type to filter")]
    [InlineData("Add a topic")]
    [InlineData("Create subject")]
    [InlineData("Linked to subject")]
    [InlineData("Linked to topic")]
    [InlineData("No topics under this subject yet. Add one above to hold material.")]
    public void NewCatalogueAndPickerCopyIsTranslated(string key)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("vi-VN");
            var translated = L10n.T(key);
            Assert.NotEqual(key, translated);
            Assert.False(string.IsNullOrWhiteSpace(translated));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
