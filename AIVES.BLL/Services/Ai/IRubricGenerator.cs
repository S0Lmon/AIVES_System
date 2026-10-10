using AIVES.DTO;

namespace AIVES.BLL.Services.Ai;

/// <summary>
/// One request for a rubric matrix. The lecturer picks how many rows and columns to ask for,
/// so the generated grid lands close to the shape they intend to use.
/// </summary>
public sealed record RubricGenerationRequest(
    string Subject,
    string Topic,
    int CriterionCount,
    int LevelCount,
    string? LearningOutcomes = null,
    string? RetrievedContext = null,
    IReadOnlyList<MaterialExcerpt>? Sources = null)
{
    public IReadOnlyList<MaterialExcerpt> RetrievedSources { get; } = Sources ?? [];
}

public interface IRubricGenerator
{
    AiProvider Provider
    {
        get;
    }
    bool IsConfigured
    {
        get;
    }
    Task<GeneratedRubric> GenerateAsync(RubricGenerationRequest request, CancellationToken cancellationToken = default);
}

public interface IRubricGeneratorRouter
{
    AiProvider? ActiveProvider
    {
        get;
    }
    bool IsProviderAvailable(AiProvider provider);
    AiProvider ResolveProvider(AiProvider? requested);
    Task<GeneratedRubric> GenerateAsync(RubricGenerationRequest request, AiProvider? requestedProvider = null, CancellationToken cancellationToken = default);
}