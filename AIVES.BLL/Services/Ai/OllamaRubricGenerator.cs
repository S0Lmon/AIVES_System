using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIVES.BLL.Services.Ai;

/// <summary>
/// phi3:mini cannot hold a whole matrix in one answer, so the local path builds the grid in
/// pieces: agree the columns first, then ask for one row at a time against those columns.
/// Each response is small enough for the model to get right, and every piece is validated
/// before it is used.
/// </summary>
public sealed class OllamaRubricGenerator : IRubricGenerator
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaRubricGenerator> _logger;

    public OllamaRubricGenerator(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaRubricGenerator> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public AiProvider Provider => AiProvider.Ollama;

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.BaseUrl) && !string.IsNullOrWhiteSpace(_options.Model);

    public async Task<GeneratedRubric> GenerateAsync(RubricGenerationRequest request, CancellationToken cancellationToken = default)
    {
        RubricGenerationPrompt.Validate(request);

        if (!IsConfigured)
            throw new InvalidOperationException(L10n.T("Local Ollama generation is disabled. Enable Ollama:Enabled and set Ollama:BaseUrl."));

        var vietnamese = L10n.Current == AppLanguage.Vi;
        var levels = GeneratedRubricParser.ParseLevels(
            await AskAsync(RubricGenerationPrompt.BuildLevels(request, vietnamese)
                + $"\n\nReturn ONLY a JSON array: [{{\"name\": string, \"points\": integer}}] with {request.LevelCount} items.",
                cancellationToken),
            request.LevelCount);

        levels = levels.OrderBy(level => level.Points).ToList();

        var criteria = new List<GeneratedRubricCriterion>(request.CriterionCount);
        for (var index = 0; index < request.CriterionCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            criteria.Add(GeneratedRubricParser.ParseCriterion(
                await AskAsync(RubricGenerationPrompt.BuildCriterion(request, levels, vietnamese)
                    + "\n\nReturn ONLY a JSON object: {\"criterion\": string, \"cells\": [{\"levelName\": string, \"descriptor\": string, \"points\": integer}]}"
                    + $" with exactly {levels.Count} cells.",
                    cancellationToken),
                levels));
        }

        return new GeneratedRubric
        {
            Name = BuildName(request, levels),
            Description = vietnamese
                ? $"Ma trận chấm điểm cho chủ đề \"{request.Topic}\" với {criteria.Count} tiêu chí và {levels.Count} mức độ."
                : $"Scoring matrix for the topic \"{request.Topic}\" with {criteria.Count} criteria across {levels.Count} levels.",
            Levels = levels,
            Criteria = criteria
        };
    }

    private static string BuildName(RubricGenerationRequest request, IReadOnlyList<GeneratedRubricLevel> levels) =>
        levels.Count == 0 ? request.Topic : $"{request.Topic} ({string.Join(" / ", levels.Select(level => level.Name))})";

    private async Task<string> AskAsync(string prompt, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = _options.Model,
            prompt,
            format = "json",
            stream = false,
            options = new
            {
                temperature = 0.3,
                num_ctx = 4096
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/generate");
        message.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama returned HTTP {StatusCode} for a rubric request", (int)response.StatusCode);
            throw new InvalidOperationException(L10n.T("Ollama cannot generate a rubric right now. Confirm the local server is running and the model is pulled."));
        }

        using var envelope = JsonDocument.Parse(responseText);
        if (envelope.RootElement.ValueKind != JsonValueKind.Object
            || !envelope.RootElement.TryGetProperty("response", out var text)
            || text.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException(L10n.T("The model did not return a usable rubric matrix. Try again with a clearer topic."));

        return text.GetString()!;
    }
}