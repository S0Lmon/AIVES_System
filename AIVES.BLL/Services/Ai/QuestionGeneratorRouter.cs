using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Ai;

/// <summary>
/// Chooses between the registered AI providers. Gemini is preferred when it is configured;
/// otherwise the local phi3:mini model takes over. A caller can also force a specific provider,
/// and an unavailable choice falls back instead of failing.
/// </summary>
public sealed class QuestionGeneratorRouter(IEnumerable<IQuestionGenerator> generators,
    ILogger<QuestionGeneratorRouter> logger) : IQuestionGeneratorRouter
{
    private static readonly AiProvider[] PreferenceOrder = [AiProvider.Gemini, AiProvider.Ollama];

    private readonly IReadOnlyList<IQuestionGenerator> _generators = generators.ToList();

    private IQuestionGenerator? Find(AiProvider provider) =>
        _generators.FirstOrDefault(generator => generator.Provider == provider);

    public bool PreferOllama => IsProviderAvailable(AiProvider.Ollama);

    public AiProvider? ActiveProvider
    {
        get
        {
            foreach (var provider in PreferenceOrder)
            {
                if (IsProviderAvailable(provider))
                    return provider;
            }

            return null;
        }
    }

    public bool IsProviderAvailable(AiProvider provider) => Find(provider)?.IsConfigured == true;

    public AiProvider ResolveProvider(AiProvider? requested)
    {
        if (requested is { } choice && Find(choice) is { } exact && exact.IsConfigured)
            return choice;

        if (requested is { } missing)
            logger.LogWarning("{Provider} was requested but is not configured; falling back", missing);

        foreach (var provider in PreferenceOrder)
        {
            if (Find(provider) is { } generator && generator.IsConfigured)
                return provider;
        }

        throw new InvalidOperationException(L10n.T("No AI provider is available. Configure Gemini:ApiKey or enable the local Ollama model."));
    }

    public async Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(QuestionGenerationRequest request, AiProvider? requestedProvider = null, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(requestedProvider);
        logger.LogInformation("Generating questions with {Provider}", provider);
        return await Find(provider)!.GenerateAsync(request, cancellationToken);
    }
}