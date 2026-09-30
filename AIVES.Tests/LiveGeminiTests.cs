using AIVES.BLL.Services.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIVES.Tests;

public sealed class LiveGeminiTests
{
    [LiveGeminiFact]
    public async Task ConfiguredGeminiProducesOneValidVivaQuestion()
    {
        var handler = new StatusHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"), Timeout = TimeSpan.FromSeconds(60)
        };
        var options = new GeminiOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("AIVES_TEST_GEMINI_API_KEY")!,
            Model = Environment.GetEnvironmentVariable("AIVES_TEST_GEMINI_MODEL") ?? new GeminiOptions().Model
        };
        var generator = new GeminiQuestionGenerator(client, Options.Create(options), NullLogger<GeminiQuestionGenerator>.Instance);
        try
        {
            var question = Assert.Single(await generator.GenerateAsync("Công nghệ phần mềm", "Mô hình kiến trúc ba lớp", null, "Cơ bản", 1));
            Assert.False(string.IsNullOrWhiteSpace(question.Content));
            Assert.Equal(2, question.FollowUpQuestions.Count);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Live Gemini HTTP {(int?)handler.Status}; model {options.Model}: {ex.Message}");
        }
    }

    private sealed class StatusHandler : DelegatingHandler
    {
        public System.Net.HttpStatusCode? Status { get; private set; }
        public StatusHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            Status = response.StatusCode;
            return response;
        }
    }
}

public sealed class LiveGeminiFactAttribute : FactAttribute
{
    public LiveGeminiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIVES_TEST_GEMINI_API_KEY")))
            Skip = "Set AIVES_TEST_GEMINI_API_KEY to opt into one live provider request.";
    }
}
