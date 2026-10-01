using AIVES.DTO;
using AIVES.DTO.Localization;
using System.Text;
using System.Text.Json;

namespace AIVES.BLL.Services.Ai;

/// <summary>
/// Shared validation for model output. Every provider must return exactly <c>count</c>
/// questions shaped like <see cref="GeneratedVivaQuestion"/>; anything else is rejected
/// rather than surfaced to the lecturer. Small local models such as phi3:mini often answer
/// with a single object instead of an array, so one object is accepted and wrapped.
/// </summary>
internal static class GeneratedQuestionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] AllowedBloom = ["Remember", "Understand", "Apply", "Analyze"];

    public static IReadOnlyList<GeneratedVivaQuestion> ParseJsonArray(string json, int expectedCount)
    {
        var invalidResponse = L10n.T("The model did not return a valid question list. Try again with a more specific topic.");
        try
        {
            var questions = Deserialize(json);
            if (questions is null || questions.Count != expectedCount || questions.Any(question => question is null
                || string.IsNullOrWhiteSpace(question.Content) || string.IsNullOrWhiteSpace(question.ExpectedAnswer)
                || !AllowedBloom.Contains(question.BloomLevel)
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

    private static List<GeneratedVivaQuestion>? Deserialize(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            var single = JsonSerializer.Deserialize<GeneratedVivaQuestion>(json, JsonOptions);
            return single is null ? null : [single];
        }

        return JsonSerializer.Deserialize<List<GeneratedVivaQuestion>>(json, JsonOptions);
    }

    /// <summary>Pulls the concatenated text parts out of a Gemini envelope.</summary>
    public static string ExtractGeminiText(string responseText)
    {
        JsonDocument envelope;
        try
        {
            envelope = JsonDocument.Parse(responseText);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                L10n.T("The model did not return a valid question list. Try again with a more specific topic."), ex);
        }

        using (envelope)
        {
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0
                || candidates[0].ValueKind != JsonValueKind.Object
                || !candidates[0].TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
                throw new InvalidOperationException(L10n.T("The model did not return a valid question list. Try again with a more specific topic."));

            return string.Concat(parts.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString()));
        }
    }
}

/// <summary>Shared instruction text so Gemini and Ollama ask for the same shape.</summary>
internal static class GenerationPrompt
{
    public static void Validate(QuestionGenerationRequest request)
    {
        if (request.Count is < 1 or > 10)
            throw new ArgumentException(L10n.T("The number of questions must be between 1 and 10."), nameof(request));
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 120)
            throw new ArgumentException(L10n.T("The subject must be between 1 and 120 characters."), nameof(request));
        if (string.IsNullOrWhiteSpace(request.Topic) || request.Topic.Length > 200)
            throw new ArgumentException(L10n.T("The topic must be between 1 and 200 characters."), nameof(request));
        if (request.LearningOutcomes?.Length > 2000)
            throw new ArgumentException(L10n.T("Learning outcomes cannot exceed 2000 characters."), nameof(request));
    }

    public static string Build(QuestionGenerationRequest request, bool vietnamese)
    {
        var count = request.Count;
        var subject = request.Subject;
        var topic = request.Topic;
        var difficulty = request.Difficulty;
        var outcomes = vietnamese ? (request.LearningOutcomes ?? "Không cung cấp") : (request.LearningOutcomes ?? "Not provided");
        var context = request.RetrievedContext;

        var contextBlock = vietnamese
            ? (string.IsNullOrWhiteSpace(context) ? string.Empty : $"""

                Nguồn tham khảo (RAG), chỉ dùng nội dung này làm căn cứ:
                ---
                {context}
                ---
                """)
            : (string.IsNullOrWhiteSpace(context) ? string.Empty : $"""

                Reference material (RAG). Base the questions on this content:
                ---
                {context}
                ---
                """);

        return vietnamese
            ? $"""
                Bạn là trợ lý học thuật cho hệ thống thi vấn đáp AIVES.
                Hãy tạo đúng {count} câu hỏi vấn đáp bằng tiếng Việt cho môn "{subject}", chủ đề "{topic}".
                Mức độ mong muốn: {difficulty}.
                Chuẩn đầu ra/ghi chú của giảng viên: {outcomes}.
                {contextBlock}
                Yêu cầu:
                - Câu hỏi phải rõ ràng, đánh giá được hiểu biết thay vì chỉ ghi nhớ máy móc.
                - BloomLevel chỉ được là một trong: Remember, Understand, Apply, Analyze.
                - ExpectedAnswer nêu các ý chính để chấm theo rubric, không viết bài mẫu dài.
                - Mỗi câu có 2 câu hỏi đào sâu, dùng khi câu trả lời còn mơ hồ hoặc thiếu ý.
                - Không tự tạo nguồn trích dẫn hoặc dữ kiện không có trong đề bài.
                """
            : $"""
                You are an academic assistant for the AIVES viva examination system.
                Generate exactly {count} viva exam questions in English for the subject "{subject}" and topic "{topic}".
                Desired difficulty: {difficulty}.
                Lecturer learning outcomes or notes: {outcomes}.
                {contextBlock}
                Requirements:
                - Questions must be clear and assess understanding rather than rote recall.
                - BloomLevel must be one of: Remember, Understand, Apply, Analyze.
                - ExpectedAnswer lists the key points to score against the rubric; do not write a long model essay.
                - Each question has 2 follow up questions, used when the answer is vague or incomplete.
                - Do not invent citations or facts that are not present in the brief or the reference material.
                """;

    }
}