using AIVES.DTO;
using System.Text.RegularExpressions;

namespace AIVES.BLL.Services.Interview;

/// <summary>
/// Corrects how browser speech recognition writes subject terms. Vietnamese recognisers spell
/// English terms phonetically ("ây pi ai" for API, "ét kiu eo" for SQL), which both the examiner
/// prompt and the lecturer then misread. Each glossary term lists such spoken forms; they are
/// replaced as whole words, ignoring case, and the term itself is restored to its canonical casing.
/// </summary>
public static class TranscriptNormalizer
{
    public static string Normalize(string transcript, IReadOnlyList<GlossaryTermDto> glossary)
    {
        if (string.IsNullOrWhiteSpace(transcript) || glossary.Count == 0)
            return transcript;

        // Longest forms first so "ây pi ai rét" wins over "ây pi ai".
        var replacements = glossary
            .SelectMany(term => term.SpokenForms.Append(term.Term).Select(form => (Form: form.Trim(), term.Term)))
            .Where(pair => pair.Form.Length >= 2)
            .DistinctBy(pair => pair.Form.ToLowerInvariant())
            .OrderByDescending(pair => pair.Form.Length);

        var text = transcript;
        foreach (var (form, term) in replacements)
        {
            var words = form.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
            // Whole words only: the form must not be glued to other letters or digits.
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){string.Join(@"[\s\-]+", words)}(?![\p{{L}}\p{{N}}])";
            text = Regex.Replace(text, pattern, term.Replace("$", "$$"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        }
        return text;
    }

    /// <summary>Terms to bias the recogniser towards, for browsers that support phrase hints.</summary>
    public static IReadOnlyList<string> Phrases(IReadOnlyList<GlossaryTermDto> glossary) =>
        glossary.Select(term => term.Term).Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToList();
}
