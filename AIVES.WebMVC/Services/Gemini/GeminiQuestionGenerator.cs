using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AIVES.WebMVC.Services.Gemini;

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
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini API chưa được cấu hình. Hãy thêm Gemini:ApiKey vào User Secrets.");

        var prompt = $"""
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
                            content = new { type = "STRING" },
                            expectedAnswer = new { type = "STRING" },
                            bloomLevel = new { type = "STRING", @enum = new[] { "Remember", "Understand", "Apply", "Analyze" } },
                            followUpQuestions = new { type = "ARRAY", minItems = 2, maxItems = 2, items = new { type = "STRING" } }
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
            throw new InvalidOperationException("Gemini không thể tạo câu hỏi lúc này. Hãy kiểm tra API key, quota và model cấu hình.");
        }

        using var envelope = JsonDocument.Parse(responseText);
        var jsonText = envelope.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

        var questions = JsonSerializer.Deserialize<List<GeneratedVivaQuestion>>(jsonText ?? "[]", JsonOptions) ?? [];
        if (questions.Count == 0)
            throw new InvalidOperationException("Gemini chưa trả về danh sách câu hỏi hợp lệ. Vui lòng thử lại với chủ đề cụ thể hơn.");

        return questions;
    }
}
