using AIVES.BLL.Services.Ai;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIVES.BLL.Services.Gemini;

public sealed class GeminiRubricGenerator : IRubricGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiRubricGenerator> _logger;

    public GeminiRubricGenerator(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<GeminiRubricGenerator> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public AiProvider Provider => AiProvider.Gemini;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<GeneratedRubric> GenerateAsync(RubricGenerationRequest request, CancellationToken cancellationToken = default)
    {
        RubricGenerationPrompt.Validate(request);

        if (!IsConfigured)
            throw new InvalidOperationException(L10n.T("Gemini API chưa được cấu hình. Hãy thêm Gemini:ApiKey vào User Secrets."));

        var criterionSchema = new
        {
            type = "OBJECT",
            properties = new
            {
                criterion = new
                {
                    type = "STRING"
                },
                cells = new
                {
                    type = "ARRAY",
                    minItems = request.LevelCount,
                    maxItems = request.LevelCount,
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            levelName = new
                            {
                                type = "STRING"
                            },
                            descriptor = new
                            {
                                type = "STRING"
                            },
                            points = new
                            {
                                type = "INTEGER"
                            }
                        },
                        required = new[] { "levelName", "descriptor", "points" }
                    }
                }
            },
            required = new[] { "criterion", "cells" }
        };

        var requestBody = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = RubricGenerationPrompt.Build(request, L10n.Current == AppLanguage.Vi) } } } },
            generationConfig = new
            {
                temperature = 0.4,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        name = new
                        {
                            type = "STRING"
                        },
                        description = new
                        {
                            type = "STRING"
                        },
                        levels = new
                        {
                            type = "ARRAY",
                            minItems = request.LevelCount,
                            maxItems = request.LevelCount,
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    name = new
                                    {
                                        type = "STRING"
                                    },
                                    points = new
                                    {
                                        type = "INTEGER"
                                    }
                                },
                                required = new[] { "name", "points" }
                            }
                        },
                        criteria = new
                        {
                            type = "ARRAY",
                            minItems = request.CriterionCount,
                            maxItems = request.CriterionCount,
                            items = criterionSchema
                        }
                    },
                    required = new[] { "name", "description", "levels", "criteria" }
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
            _logger.LogWarning("Gemini returned HTTP {StatusCode} for a rubric request", (int)response.StatusCode);
            throw new InvalidOperationException(L10n.T("Gemini cannot generate a rubric right now. Check the API key, quota and configured model."));
        }

        return GeneratedRubricParser.Parse(GeneratedQuestionParser.ExtractGeminiText(responseText),
            request.CriterionCount, request.LevelCount);
    }
}