using System.Net.Http.Json;
using System.Text.Json;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Gemini;
using AIVES.DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Interview;

/// <summary>Follow-up decisions from Gemini, using the interview model (see <see cref="InterviewOptions.Model"/>).</summary>
public sealed class GeminiFollowUpGenerator(
    HttpClient httpClient,
    IOptions<GeminiOptions> gemini,
    IOptions<InterviewOptions> interview,
    ILogger<GeminiFollowUpGenerator> logger) : IFollowUpGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(gemini.Value.ApiKey) && !string.IsNullOrWhiteSpace(interview.Value.Model);

    public async Task<FollowUpDecision> DecideAsync(FollowUpRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini is not configured for interviews.");

        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = FollowUpPrompt.Build(request) } } } },
            generationConfig = new
            {
                temperature = 0.3,
                responseMimeType = "application/json",
                responseSchema = FollowUpPrompt.ResponseSchema
            }
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(interview.Value.Model)}:generateContent");
        message.Headers.Add("x-goog-api-key", gemini.Value.ApiKey);
        message.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini follow-up returned HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException($"Gemini returned HTTP {(int)response.StatusCode}.");
        }
        return FollowUpPrompt.Parse(GeneratedQuestionParser.ExtractGeminiText(text));
    }
}
