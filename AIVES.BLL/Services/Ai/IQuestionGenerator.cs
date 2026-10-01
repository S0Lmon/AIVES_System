using AIVES.DTO;
namespace AIVES.BLL.Services.Ai;

/// <summary>
/// One generation request. <see cref="RetrievedContext"/> carries the RAG passages that the
/// caller assembled for this request, so providers never need to know about the catalog.
/// </summary>
public sealed record QuestionGenerationRequest(
    string Subject,
    string Topic,
    string? LearningOutcomes,
    string Difficulty,
    int Count,
    string? RetrievedContext = null,
    IReadOnlyList<MaterialExcerpt>? Sources = null)
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