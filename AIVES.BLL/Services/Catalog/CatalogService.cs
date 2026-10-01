using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Options;
using AIVES.BLL.Services.Ai;

namespace AIVES.BLL.Services.Catalog;

public sealed class CatalogService(ISubjectRepository subjects, ITopicRepository topics, IMaterialRepository materials,
    IOptions<OllamaOptions> ollamaOptions) : ICatalogService
{
    private const int MaxRagDocuments = 6;

    public Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken = default) => subjects.GetAllAsync(cancellationToken);

    public Task<SubjectInput> CreateSubjectAsync(SubjectInput input, CancellationToken cancellationToken = default) =>
        subjects.AddAsync(Validate(input), cancellationToken);

    public Task UpdateSubjectAsync(int id, SubjectInput input, CancellationToken cancellationToken = default) =>
        subjects.UpdateAsync(id, Validate(input), cancellationToken);

    public Task DeleteSubjectAsync(int id, CancellationToken cancellationToken = default) => subjects.DeleteAsync(id, cancellationToken);

    public Task<IReadOnlyList<TopicDto>> GetTopicsAsync(int? subjectId = null, CancellationToken cancellationToken = default) =>
        topics.GetAllAsync(subjectId, cancellationToken);

    public Task<TopicInput> CreateTopicAsync(TopicInput input, CancellationToken cancellationToken = default) =>
        topics.AddAsync(Validate(input), cancellationToken);

    public Task UpdateTopicAsync(int id, TopicInput input, CancellationToken cancellationToken = default) =>
        topics.UpdateAsync(id, Validate(input), cancellationToken);

    public Task DeleteTopicAsync(int id, CancellationToken cancellationToken = default) => topics.DeleteAsync(id, cancellationToken);

    public Task<IReadOnlyList<MaterialDto>> GetMaterialsAsync(int? topicId = null, CancellationToken cancellationToken = default) =>
        materials.GetAllAsync(topicId, cancellationToken);

    public Task<MaterialDto> CreateMaterialAsync(MaterialInput input, CancellationToken cancellationToken = default) =>
        materials.AddAsync(Validate(input), cancellationToken);

    public Task UpdateMaterialAsync(int id, MaterialInput input, CancellationToken cancellationToken = default) =>
        materials.UpdateAsync(id, Validate(input), cancellationToken);

    public Task DeleteMaterialAsync(int id, CancellationToken cancellationToken = default) => materials.DeleteAsync(id, cancellationToken);

    public async Task<RagContext> BuildRagContextAsync(int? topicId, int? subjectId, string query, CancellationToken cancellationToken = default)
    {
        var budget = Math.Max(1000, ollamaOptions.Value.MaxContextCharacters);
        var candidates = await materials.GetActiveForRagAsync(topicId, subjectId, MaxRagDocuments * 4, cancellationToken);
        if (candidates.Count == 0)
            return RagContext.Empty;

        var terms = Tokenize(query);
        var ranked = candidates
            .Select(material => new { Material = material, Score = Score(material, terms) })
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Material.ModifiedDate)
            .Take(MaxRagDocuments)
            .ToList();

        // Only ground the model in material that actually matched the request.
        var relevant = ranked.Where(entry => entry.Score > 0).Take(MaxRagDocuments).ToList();
        if (relevant.Count == 0)
            return RagContext.Empty;

        var builder = new System.Text.StringBuilder();
        var sources = new List<MaterialExcerpt>();
        foreach (var entry in relevant)
        {
            var remaining = budget - builder.Length;
            if (remaining <= 0)
                break;

            var content = entry.Material.Content.Length > remaining
                ? entry.Material.Content[..remaining]
                : entry.Material.Content;
            builder.AppendLine($"### {entry.Material.Title}");
            builder.AppendLine(content);
            builder.AppendLine();
            sources.Add(new MaterialExcerpt(entry.Material.Id, entry.Material.Title, content));
        }

        return sources.Count == 0 ? RagContext.Empty : new RagContext(builder.ToString().Trim(), sources);
    }

    private static int Score(MaterialDto material, HashSet<string> terms)
    {
        if (terms.Count == 0)
            return 1;

        var haystack = $"{material.Title} {material.Content}";
        var hits = terms.Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
        return hits;
    }

    private static HashSet<string> Tokenize(string query) => query
        .Split([' ', ',', '.', ';', ':', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(token => token.Length >= 3)
        .Select(token => token.ToLowerInvariant())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static SubjectInput Validate(SubjectInput input)
    {
        var name = input.Name.Trim();
        if (name.Length is < 2 or > 200)
            throw new ArgumentException(L10n.T("The subject name must be between 2 and 200 characters."), nameof(input));
        return input with { Name = name, Description = input.Description?.Trim() ?? string.Empty };
    }

    private static TopicInput Validate(TopicInput input)
    {
        var name = input.Name.Trim();
        if (name.Length is < 2 or > 200)
            throw new ArgumentException(L10n.T("The topic name must be between 2 and 200 characters."), nameof(input));
        return input with { Name = name, Description = input.Description?.Trim() ?? string.Empty };
    }

    private static MaterialInput Validate(MaterialInput input)
    {
        var title = input.Title.Trim();
        var content = input.Content.Trim();
        if (title.Length is < 2 or > 300)
            throw new ArgumentException(L10n.T("The material title must be between 2 and 300 characters."), nameof(input));
        if (content.Length is < 10)
            throw new ArgumentException(L10n.T("The material content must be at least 10 characters."), nameof(input));
        return input with { Title = title, Content = content };
    }
}