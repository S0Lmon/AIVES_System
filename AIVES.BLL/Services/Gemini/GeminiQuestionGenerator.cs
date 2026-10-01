using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Gemini;

public sealed class GeminiQuestionGenerator : IGeminiQuestionGenerator
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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(string subject, string topic, string? learningOutcomes, string difficulty, int count, CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 10)
            throw new ArgumentException(L10n.T("The number of questions must be between 1 and 10."), nameof(count));
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 120)
            throw new ArgumentException(L10n.T("The subject must be between 1 and 120 characters."), nameof(subject));
        if (string.IsNullOrWhiteSpace(topic) || topic.Length > 200)
            throw new ArgumentException(L10n.T("The topic must be between 1 and 200 characters."), nameof(topic));
        if (learningOutcomes?.Length > 2000)
            throw new ArgumentException(L10n.T("Learning outcomes cannot exceed 2000 characters."), nameof(learningOutcomes));

        if (!IsConfigured)
            throw new InvalidOperationException("Gemini API chưa được cấu hình. Hãy thêm Gemini:ApiKey vào User Secrets.");

        var prompt = BuildPrompt(count, subject, topic, difficulty, learningOutcomes);

        var requestBody = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                temperature = 0.45,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "ARRAY",
                    minItems = count,
                    maxItems = count,
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
                                @enum = new[] { "Remember", "Understand", "Apply", "Analyze" }
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
                        required = new[] { "content", "expectedAnswer", "bloomLevel", "followUpQuestions" }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", _options.ApiKey);
        request.Content = JsonContent.Create(requestBody, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gemini returned HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException(L10n.T("Gemini cannot generate questions right now. Check the API key, quota and configured model."));
        }

        return ParseQuestions(responseText, count);
    }

    private static string BuildPrompt(int count, string subject, string topic, string difficulty, string? learningOutcomes)
    {
        if (L10n.Current == AppLanguage.Vi)
        {
            return $"""
                Bạn là trợ lý học thuật cho hệ thống thi vấn đáp AIVES.
                Hãy tạo đúng {count} câu hỏi vấn đáp bằng tiếng Việt cho môn "{subject}", chủ đề "{topic}".
                Mức độ mong muốn: {difficulty}.
                Chuẩn đầu ra/ghi chú của giảng viên: {learningOutcomes ?? "Không cung cấp"}.

                Yêu cầu:
                - Câu hỏi phải rõ ràng, đánh giá được hiểu biết thay vì chỉ ghi nhớ máy móc.
                - BloomLevel chỉ được là một trong: Remember, Understand, Apply, Analyze.
                - ExpectedAnswer nêu các ý chính để chấm theo rubric, không viết bài mẫu dài.
                - Mỗi câu có 2 câu hỏi đào sâu, dùng khi câu trả lời còn mơ hồ hoặc thiếu ý.
                - Không tự tạo nguồn trích dẫn hoặc dữ kiện không có trong đề bài.
                """;
        }

        return $"""
            You are an academic assistant for the AIVES viva examination system.
            Generate exactly {count} viva exam questions in English for the subject "{subject}" and topic "{topic}".
            Desired difficulty: {difficulty}.
            Lecturer learning outcomes or notes: {learningOutcomes ?? "Not provided"}.

            Requirements:
            - Questions must be clear and assess understanding rather than rote recall.
            - BloomLevel must be one of: Remember, Understand, Apply, Analyze.
            - ExpectedAnswer lists the key points to score against the rubric; do not write a long model essay.
            - Each question has 2 follow up questions, used when the answer is vague or incomplete.
            - Do not invent citations or facts that are not in the brief.
            """;
    }

    private static IReadOnlyList<GeneratedVivaQuestion> ParseQuestions(string responseText, int count)
    {
        var invalidResponse = L10n.T("Gemini did not return a valid question list. Try again with a more specific topic.");
        try
        {
            using var envelope = JsonDocument.Parse(responseText);
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0
                || candidates[0].ValueKind != JsonValueKind.Object
                || !candidates[0].TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
                throw new InvalidOperationException(invalidResponse);

            var textParts = parts.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString());
            var questions = JsonSerializer.Deserialize<List<GeneratedVivaQuestion>>(string.Concat(textParts), JsonOptions);
            string[] allowedBloom = ["Remember", "Understand", "Apply", "Analyze"];
            if (questions is null || questions.Count != count || questions.Any(question => question is null
                || string.IsNullOrWhiteSpace(question.Content) || string.IsNullOrWhiteSpace(question.ExpectedAnswer)
                || !allowedBloom.Contains(question.BloomLevel)
                || question.FollowUpQuestions is null || question.FollowUpQuestions.Count != 2
                || question.FollowUpQuestions.Any(string.IsNullOrWhiteSpace)))
                throw new InvalidOperationException(invalidResponse);

            return questions;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(invalidResponse, ex);
        }
    }
}
