using AIVES.DTO;
namespace AIVES.BLL.Services.Ai;

public interface IQuestionGeneratorRouter
{
    /// <summary>True when the user has asked for the local Ollama fallback to be used first.</summary>
    bool PreferOllama
    {
        get;
    }

    /// <summary>The provider that would serve the next request, or null when none is configured.</summary>
    AiProvider? ActiveProvider
    {
        get;
    }

    bool IsProviderAvailable(AiProvider provider);

    AiProvider ResolveProvider(AiProvider? requested);

    Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(QuestionGenerationRequest request, AiProvider? requestedProvider = null, CancellationToken cancellationToken = default);
}