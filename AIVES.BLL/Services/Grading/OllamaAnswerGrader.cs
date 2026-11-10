using AIVES.BLL.Services.Ai;
using AIVES.DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Grading;

/// <summary>Grading suggestions from a local Ollama model, with the same prompt, schema and clamping as Gemini.</summary>
public sealed class OllamaAnswerGrader(HttpClient httpClient, IOptions<OllamaOptions> ollama, ILogger<OllamaAnswerGrader> logger) : IAnswerGrader
{
    public bool IsConfigured => ollama.Value.IsUsable;

    public string ModelName => "ollama/" + ollama.Value.GradingModelName;

    public async Task<GradeSuggestion> GradeAsync(GradingRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Ollama is not enabled for grading (Ollama:Enabled, Ollama:Model).");
        var started = DateTime.UtcNow;
        // A low temperature keeps re-grading the same answer stable.
        var json = await OllamaJsonClient.GenerateAsync(httpClient, ollama.Value.GradingModelName, GradingPrompt.Build(request), GradingPrompt.ResponseSchema,
            temperature: 0.1, contextTokens: ollama.Value.ContextTokens, ollama.Value.KeepAlive, cancellationToken);
        logger.LogInformation("Ollama {Model} graded an answer in {Seconds:F1} s", ollama.Value.GradingModelName, (DateTime.UtcNow - started).TotalSeconds);
        return GradingPrompt.Parse(json, request, ModelName);
    }
}

/// <summary>
/// The grader the grading worker uses: Ollama first when <see cref="OllamaOptions.PreferLocal"/> is on (or Gemini has
/// no key), otherwise Gemini first; when the first one fails, the other is tried if it is configured.
/// </summary>
public sealed class RoutedAnswerGrader(GeminiAnswerGrader gemini, OllamaAnswerGrader ollama, IOptions<OllamaOptions> options,
    ILogger<RoutedAnswerGrader> logger) : IAnswerGrader
{
    private IReadOnlyList<IAnswerGrader> Order =>
        (options.Value.PreferLocal ? new IAnswerGrader[] { ollama, gemini } : [gemini, ollama]).Where(item => item.IsConfigured).ToList();

    public bool IsConfigured => gemini.IsConfigured || ollama.IsConfigured;

    public string ModelName => Order.FirstOrDefault()?.ModelName ?? gemini.ModelName;

    public async Task<GradeSuggestion> GradeAsync(GradingRequest request, CancellationToken cancellationToken = default)
    {
        var order = Order;
        if (order.Count == 0)
            throw new InvalidOperationException("No AI model is configured for grading (Gemini:ApiKey or Ollama).");
        for (var i = 0; ; i++)
        {
            try
            {
                return await order[i].GradeAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && i + 1 < order.Count)
            {
                logger.LogWarning(ex, "{Model} could not grade the answer; trying {Next}", order[i].ModelName, order[i + 1].ModelName);
            }
        }
    }
}
