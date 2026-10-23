using AIVES.DTO.Localization;
using System.Globalization;
using System.Text.RegularExpressions;

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
    public void EveryTextThePresentationLayerTranslatesHasAVietnameseEntry()
    {
        // A key that is wrapped in L10n but missing from the table renders in English for
        // Vietnamese users without any error, which is how whole pages slipped through before.
        var web = Path.Combine(RepositoryRoot(), "AIVES.WebRazor");
        var call = new Regex(@"L10n\.(?:T|Format)\(\s*""((?:[^""\\]|\\.)*)""");
        var missing = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cshtml", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => call.Matches(File.ReadAllText(path)).Select(match =>
                (File: Path.GetRelativePath(web, path), Key: match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"))))
            .Where(item => !AppText.VietnameseEntries.ContainsKey(item.Key))
            .Select(item => $"{item.File}: {item.Key}")
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AIVES_System.slnx")))
                return directory.FullName;
        throw new InvalidOperationException("AIVES_System.slnx was not found above the test output folder.");
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
