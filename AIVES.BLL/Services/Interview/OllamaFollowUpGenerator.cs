using AIVES.BLL.Services.Ai;
using AIVES.DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Interview;

/// <summary>Follow-up decisions from a local Ollama model, with the same prompt and parser as Gemini.</summary>
public sealed class OllamaFollowUpGenerator(HttpClient httpClient, IOptions<OllamaOptions> ollama, ILogger<OllamaFollowUpGenerator> logger)
    : IFollowUpGenerator
{
    public bool IsConfigured => ollama.Value.IsUsable;

    public string ModelName => ollama.Value.InterviewModelName;

    public async Task<FollowUpDecision> DecideAsync(FollowUpRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Ollama is not enabled for interviews (Ollama:Enabled, Ollama:Model).");
        var started = DateTime.UtcNow;
        var json = await OllamaJsonClient.GenerateAsync(httpClient, ModelName, FollowUpPrompt.Build(request), FollowUpPrompt.ResponseSchema,
            temperature: 0.3, contextTokens: ollama.Value.ContextTokens, ollama.Value.KeepAlive, cancellationToken);
        logger.LogInformation("Ollama {Model} decided a follow-up in {Seconds:F1} s", ModelName, (DateTime.UtcNow - started).TotalSeconds);
        return FollowUpPrompt.Parse(json);
    }
}

/// <summary>
/// The follow-up generator the viva uses: Ollama first when <see cref="OllamaOptions.PreferLocal"/> is on (or Gemini has
/// no key), otherwise Gemini first; when the first one fails, the other is tried if it is configured.
/// </summary>
public sealed class RoutedFollowUpGenerator(GeminiFollowUpGenerator gemini, OllamaFollowUpGenerator ollama, IOptions<OllamaOptions> options,
    ILogger<RoutedFollowUpGenerator> logger) : IFollowUpGenerator
{
    public bool IsConfigured => gemini.IsConfigured || ollama.IsConfigured;

    private IReadOnlyList<IFollowUpGenerator> Order =>
        (options.Value.PreferLocal ? new IFollowUpGenerator[] { ollama, gemini } : [gemini, ollama]).Where(item => item.IsConfigured).ToList();

    public async Task<FollowUpDecision> DecideAsync(FollowUpRequest request, CancellationToken cancellationToken = default)
    {
        var order = Order;
        if (order.Count == 0)
            throw new InvalidOperationException("No AI model is configured for interviews (Gemini:ApiKey or Ollama).");
        for (var i = 0; ; i++)
        {
            try
            {
                return await order[i].DecideAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && i + 1 < order.Count)
            {
                logger.LogWarning(ex, "{Provider} could not decide a follow-up; trying {Next}", order[i].GetType().Name, order[i + 1].GetType().Name);
            }
        }
    }
}
