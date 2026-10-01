using AIVES.DTO;
namespace AIVES.BLL.Services.Ai;

/// <summary>
/// One generation request. <see cref="RetrievedContext"/> carries the RAG passages that the
/// caller assembled for this request, so providers never need to know about the catalog.
///
/// <see cref="BloomLevel"/> and <see cref="Difficulty"/> are optional constraints. When either is
/// left blank the model picks the value and the parser accepts whatever it returns; when one is
/// set the caller treats its own value as authoritative.
/// </summary>
public sealed record QuestionGenerationRequest(
    string Subject,
    string Topic,
    string? LearningOutcomes,
    int Count,
    string? RetrievedContext = null,
    IReadOnlyList<MaterialExcerpt>? Sources = null,
    string? BloomLevel = null,
    string? Difficulty = null,
    string? DifficultyFrom = null,
    string? DifficultyTo = null)
{
    public IReadOnlyList<MaterialExcerpt> RetrievedSources
    {
        get;
    } = Sources ?? [];
}

public interface IQuestionGenerator
{
    AiProvider Provider
    {
        get;
    }
    bool IsConfigured
    {
        get;
    }
    Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(QuestionGenerationRequest request, CancellationToken cancellationToken = default);
}