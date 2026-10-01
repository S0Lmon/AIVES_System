using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Ai;

/// <summary>
/// Picks a provider for rubric generation the same way <see cref="QuestionGeneratorRouter"/> does
/// for questions: Gemini first when it is configured, otherwise the local model, and an
/// unavailable request falls back instead of failing.
/// </summary>
public sealed class RubricGeneratorRouter(IEnumerable<IRubricGenerator> generators,
    ILogger<RubricGeneratorRouter> logger) : IRubricGeneratorRouter
{
    private static readonly AiProvider[] PreferenceOrder = [AiProvider.Gemini, AiProvider.Ollama];

    private readonly IReadOnlyList<IRubricGenerator> _generators = generators.ToList();

    private IRubricGenerator? Find(AiProvider provider) =>
        _generators.FirstOrDefault(generator => generator.Provider == provider);

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
            logger.LogWarning("{Provider} was requested for rubric generation but is not configured; falling back", missing);

        foreach (var provider in PreferenceOrder)
        {
            if (Find(provider) is { } generator && generator.IsConfigured)
                return provider;
        }

        throw new InvalidOperationException(L10n.T("No AI provider is available. Configure Gemini:ApiKey or enable the local Ollama model."));
    }

    public async Task<GeneratedRubric> GenerateAsync(RubricGenerationRequest request, AiProvider? requestedProvider = null, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(requestedProvider);
        logger.LogInformation("Generating a rubric matrix with {Provider}", provider);
        return await Find(provider)!.GenerateAsync(request, cancellationToken);
    }
}