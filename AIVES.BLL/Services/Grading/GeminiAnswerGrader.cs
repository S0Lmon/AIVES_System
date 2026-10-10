using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Gemini;
using AIVES.DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIVES.BLL.Services.Grading;

/// <summary>Proposes a score for one answered main question.</summary>
public interface IAnswerGrader
{
    bool IsConfigured
    {
        get;
    }
    string ModelName
    {
        get;
    }

    /// <summary>Throws when the model cannot be reached or returns something unusable.</summary>
    Task<GradeSuggestion> GradeAsync(GradingRequest request, CancellationToken cancellationToken = default);
}

public sealed class GradingOptions
{
    public const string SectionName = "Grading";

    /// <summary>Gemini model for grading suggestions; empty uses Gemini:Model. Grading is not live, so a larger model is fine.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Run the background worker that grades finished interviews and closes abandoned ones.</summary>
    public bool WorkerEnabled { get; set; } = true;

    public int PollSeconds { get; set; } = 15;

    /// <summary>Longest wait for one question's grade.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class GeminiAnswerGrader(
    HttpClient httpClient,
    IOptions<GeminiOptions> gemini,
    IOptions<GradingOptions> grading,
    ILogger<GeminiAnswerGrader> logger) : IAnswerGrader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ModelName => string.IsNullOrWhiteSpace(grading.Value.Model) ? gemini.Value.Model : grading.Value.Model;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(gemini.Value.ApiKey) && !string.IsNullOrWhiteSpace(ModelName);

    public async Task<GradeSuggestion> GradeAsync(GradingRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini is not configured for grading.");

        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = GradingPrompt.Build(request) } } } },
            generationConfig = new
            {
                // Low temperature: the same answer should get the same proposal when re-graded.
                temperature = 0.1,
                responseMimeType = "application/json",
                responseSchema = GradingPrompt.ResponseSchema
            }
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(ModelName)}:generateContent");
        message.Headers.Add("x-goog-api-key", gemini.Value.ApiKey);
        message.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini grading returned HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException($"Gemini returned HTTP {(int)response.StatusCode}.");
        }
        return GradingPrompt.Parse(GeneratedQuestionParser.ExtractGeminiText(text), request, ModelName);
    }
}
