using System.Text;
using System.Text.RegularExpressions;
using AIVES.DTO;

namespace AIVES.BLL.Services.Catalog;

/// <summary>
/// Retrieval for question and rubric generation. Materials are split into overlapping passages
/// (keeping the nearest "## Slide n" / "## Page n" heading), each passage is scored against the
/// request with BM25 over single words and adjacent word pairs (Vietnamese terms are usually two or
/// more syllables, e.g. "cơ sở dữ liệu"), and the best passages are packed into the character budget.
/// Only passages that share at least one term with the request are used.
/// </summary>
public static partial class MaterialRetriever
{
    public const int PassageCharacters = 1200;
    public const int OverlapCharacters = 200;
    private const double K1 = 1.2;
    private const double B = 0.75;

    public sealed record Passage(MaterialDto Material, int Index, string Heading, string Text);

    public static RagContext Retrieve(IReadOnlyList<MaterialDto> materials, string query, int budget)
    {
        if (materials.Count == 0)
            return RagContext.Empty;
        var terms = Terms(query).Distinct().ToList();
        if (terms.Count == 0)
            return RagContext.Empty;

        var passages = materials.SelectMany(Split).ToList();
        var documents = passages.Select(passage => Terms(passage.Material.Title + " " + passage.Heading + " " + passage.Text).ToList()).ToList();
        var averageLength = documents.Average(document => (double)Math.Max(1, document.Count));
        var documentFrequency = terms.ToDictionary(term => term, term => documents.Count(document => document.Contains(term)));

        var scored = passages.Select((passage, index) =>
            {
                var document = documents[index];
                var frequencies = document.GroupBy(term => term).ToDictionary(group => group.Key, group => group.Count());
                var score = 0.0;
                foreach (var term in terms)
                {
                    if (!frequencies.TryGetValue(term, out var frequency))
                        continue;
                    var idf = Math.Log(1 + (passages.Count - documentFrequency[term] + 0.5) / (documentFrequency[term] + 0.5));
                    // Word pairs carry more meaning than single syllables.
                    var weight = term.Contains(' ') ? 1.5 : 1.0;
                    score += weight * idf * frequency * (K1 + 1) / (frequency + K1 * (1 - B + B * document.Count / averageLength));
                }
                return (Passage: passage, Score: score);
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Passage.Material.ModifiedDate)
            .ToList();
        if (scored.Count == 0)
            return RagContext.Empty;

        var chosen = new List<Passage>();
        var used = 0;
        foreach (var (passage, _) in scored)
        {
            var cost = passage.Text.Length + passage.Heading.Length + passage.Material.Title.Length + 10;
            if (used + cost > budget)
            {
                if (chosen.Count == 0)
                    chosen.Add(passage with { Text = passage.Text[..Math.Max(0, Math.Min(passage.Text.Length, budget - passage.Material.Title.Length - 10))] });
                continue;
            }
            chosen.Add(passage);
            used += cost;
        }

        // Group by material and keep each material's passages in reading order.
        var builder = new StringBuilder();
        var sources = new List<MaterialExcerpt>();
        foreach (var group in chosen.GroupBy(passage => passage.Material))
        {
            var material = group.First().Material;
            var excerpt = new StringBuilder();
            foreach (var passage in group.OrderBy(passage => passage.Index))
            {
                if (passage.Heading.Length > 0)
                    excerpt.AppendLine(passage.Heading);
                excerpt.AppendLine(passage.Text);
            }
            builder.AppendLine($"### {material.Title}");
            builder.AppendLine(excerpt.ToString().Trim());
            builder.AppendLine();
            sources.Add(new MaterialExcerpt(material.Id, material.Title, excerpt.ToString().Trim()));
        }
        return new RagContext(builder.ToString().Trim(), sources);
    }

    /// <summary>Overlapping passages that break on paragraph or sentence ends where possible.</summary>
    public static IEnumerable<Passage> Split(MaterialDto material)
    {
        var text = material.Content.Replace("\r\n", "\n");
        var headings = HeadingPattern().Matches(text).Select(match => (match.Index, Text: match.Value.Trim())).ToList();
        var index = 0;
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(text.Length, start + PassageCharacters);
            if (end < text.Length)
            {
                var window = text[start..end];
                var cut = Math.Max(window.LastIndexOf("\n\n", StringComparison.Ordinal), Math.Max(window.LastIndexOf(". ", StringComparison.Ordinal), window.LastIndexOf('\n')));
                if (cut > PassageCharacters / 2)
                    end = start + cut + 1;
            }
            var passage = text[start..end].Trim();
            if (passage.Length > 0)
                yield return new Passage(material, index++, passage.StartsWith('#') ? string.Empty : HeadingBefore(headings, start), passage);
            if (end >= text.Length)
                break;
            start = Math.Max(start + 1, end - OverlapCharacters);
        }
    }

    private static string HeadingBefore(List<(int Index, string Text)> headings, int position)
    {
        var heading = string.Empty;
        foreach (var (index, text) in headings)
        {
            if (index >= position)
                break;
            heading = text;
        }
        return heading;
    }

    /// <summary>Lower-case words plus adjacent word pairs. Words shorter than two letters are dropped.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        var words = WordPattern().Matches(text.ToLowerInvariant()).Select(match => match.Value).Where(word => word.Length >= 2 && !StopWords.Contains(word)).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            yield return words[i];
            if (i + 1 < words.Count)
                yield return words[i] + " " + words[i + 1];
        }
    }

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "with", "that", "this", "are", "is", "of", "to", "in", "on", "an", "or", "be", "by", "as", "at", "it",
        "và", "của", "là", "các", "những", "cho", "với", "trong", "được", "có", "một", "này", "khi", "thì", "để", "từ", "về", "theo"
    ];

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"^#{1,6} .+$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();
}
