using AIVES.BLL.Services.Ai;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIVES.BLL.Services.Ai;

public sealed class OllamaQuestionGenerator : IQuestionGenerator
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaQuestionGenerator> _logger;

    public OllamaQuestionGenerator(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaQuestionGenerator> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public AiProvider Provider => AiProvider.Ollama;

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.BaseUrl) && !string.IsNullOrWhiteSpace(_options.Model);

    public async Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(QuestionGenerationRequest request, CancellationToken cancellationToken = default)
    {
        GenerationPrompt.Validate(request);

        if (!IsConfigured)
            throw new InvalidOperationException(L10n.T("Local Ollama generation is disabled. Enable Ollama:Enabled and set Ollama:BaseUrl."));

        // phi3:mini answers one question at a time no matter how the prompt is worded, so a
        // multi-question request becomes several single-question calls. That also keeps the
        // shared parser's exact-count rule intact instead of trusting the local model.
        var questions = new List<GeneratedVivaQuestion>(request.Count);
        for (var index = 0; index < request.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            questions.Add(await GenerateOneAsync(request with { Count = 1 }, cancellationToken));
        }

        return questions;
    }

    private async Task<GeneratedVivaQuestion> GenerateOneAsync(QuestionGenerationRequest request, CancellationToken cancellationToken)
    {
        var blooms = string.Join("|", BloomLevels.All);
        var difficulties = string.Join("|", QuestionDifficulties.Ordered);

        var payload = new
        {
            model = _options.Model,
            prompt = GenerationPrompt.Build(request, L10n.Current == AppLanguage.Vi)
                + $"\n\nReturn ONLY one JSON object: {{\"content\": string, \"expectedAnswer\": string, "
                + $"\"bloomLevel\": \"{blooms}\", \"difficulty\": \"{difficulties}\", \"followUpQuestions\": [string, string]}}.",
            format = "json",
            stream = false,
            options = new { temperature = 0.4, num_ctx = 4096 }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/generate");
        message.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama returned HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException(L10n.T("Ollama cannot generate questions right now. Confirm the local server is running and the model is pulled."));
        }

        return GeneratedQuestionParser.ParseJsonArray(ExtractOllamaText(responseText), 1)[0];
    }

    private static string ExtractOllamaText(string responseText)
    {
        using var envelope = JsonDocument.Parse(responseText);
        var root = envelope.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("response", out var text)
            || text.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException(L10n.T("The model did not return a valid question list. Try again with a more specific topic."));

        return text.GetString()!;
    }

    /// <summary>Probes the local server so the UI can show whether the fallback is actually usable.</summary>
    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BaseUrl))
            return false;

        try
        {
            using var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("models", out var models)
                && models.ValueKind == JsonValueKind.Array
                && models.EnumerateArray().Any(model => model.TryGetProperty("name", out var name)
                    && name.GetString()?.StartsWith(_options.Model, StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug("Ollama at {BaseUrl} is not reachable", _options.BaseUrl);
            return false;
        }
    }
}