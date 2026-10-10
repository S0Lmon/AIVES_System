using AIVES.BLL.Services.Ai;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIVES.BLL.Services.Gemini;

public sealed class GeminiQuestionGenerator : IQuestionGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiQuestionGenerator> _logger;

    public GeminiQuestionGenerator(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<GeminiQuestionGenerator> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public AiProvider Provider => AiProvider.Gemini;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(QuestionGenerationRequest request, CancellationToken cancellationToken = default)
    {
        GenerationPrompt.Validate(request);

        if (!IsConfigured)
            throw new InvalidOperationException(L10n.T("Gemini API chưa được cấu hình. Hãy thêm Gemini:ApiKey vào User Secrets."));

        var requestBody = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = GenerationPrompt.Build(request, L10n.Current == AppLanguage.Vi) } } } },
            generationConfig = new
            {
                temperature = 0.45,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "ARRAY",
                    minItems = request.Count,
                    maxItems = request.Count,
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            content = new
                            {
                                type = "STRING"
                            },
                            expectedAnswer = new
                            {
                                type = "STRING"
                            },
                            bloomLevel = new
                            {
                                type = "STRING",
                                @enum = BloomLevels.All.ToArray()
                            },
                            difficulty = new
                            {
                                type = "STRING",
                                @enum = QuestionDifficulties.Ordered.ToArray()
                            },
                            followUpQuestions = new
                            {
                                type = "ARRAY",
                                minItems = 2,
                                maxItems = 2,
                                items = new
                                {
                                    type = "STRING"
                                }
                            }
                        },
                        required = new[] { "content", "expectedAnswer", "bloomLevel", "difficulty", "followUpQuestions" }
                    }
                }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent");
        message.Headers.Add("x-goog-api-key", _options.ApiKey);
        message.Content = JsonContent.Create(requestBody, options: JsonOptions);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gemini returned HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException(L10n.T("Gemini cannot generate questions right now. Check the API key, quota and configured model."));
        }

        return GeneratedQuestionParser.ParseJsonArray(GeneratedQuestionParser.ExtractGeminiText(responseText), request.Count);
    }
}