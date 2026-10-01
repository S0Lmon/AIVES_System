using System.Net;
using System.Text.Json;
using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Gemini;
using AIVES.BLL.Services.Email;
using AIVES.DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIVES.Tests;

public sealed class GeminiTests
{
    private static GeneratedVivaQuestion ValidQuestion => new()
    {
        Content = "Explain the three layers", ExpectedAnswer = "Presentation, BLL, DAL", BloomLevel = "Understand", Difficulty = "Intermediate",
        FollowUpQuestions = ["Why separate the layers?", "What does DAL do?"]
    };
    private static string Envelope(object questions) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(questions) } } } } }
    });
    private static QuestionGenerationRequest Request(string subject, string topic, int count) =>
        new(subject, topic, null, count, Difficulty: "Intermediate");

    private static GeminiQuestionGenerator Generator(ResponseHandler handler, string key = "fake-test-key") => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") },
        Options.Create(new GeminiOptions { ApiKey = key, Model = "test-model" }), NullLogger<GeminiQuestionGenerator>.Instance);

    [Fact]
    public async Task ValidResponseIsParsedAndRequestHasExpectedSchema()
    {
        var handler = new ResponseHandler(HttpStatusCode.OK, Envelope(new[] { ValidQuestion }));
        var output = await Generator(handler).GenerateAsync(Request("Software", "Layers", 1));
        Assert.Equal("Understand", Assert.Single(output).BloomLevel);
        Assert.Equal(2, output[0].FollowUpQuestions.Count);
        Assert.Equal("/v1beta/models/test-model:generateContent", handler.Path);
        Assert.Equal("fake-test-key", handler.Key);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public async Task MissingKeyDoesNotMakeAnHttpRequest()
    {
        var handler = new ResponseHandler(HttpStatusCode.OK, "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(handler, "").GenerateAsync(Request("Test", "Test", 1)));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(429)] [InlineData(500)]
    public async Task ProviderErrorsAreReportedWithoutLeakingResponseBody(int status)
    {
        var handler = new ResponseHandler((HttpStatusCode)status, "private-provider-detail");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(handler).GenerateAsync(Request("Test", "Test", 1)));
        Assert.DoesNotContain("private-provider-detail", error.Message);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"candidates\":[]}")] [InlineData("not-json")]
    public async Task MalformedProviderEnvelopeIsAControlledError(string body)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(new(HttpStatusCode.OK, body)).GenerateAsync(Request("Test", "Test", 1)));
    }

    [Fact]
    public async Task ProviderMustReturnRequestedNumberOfQuestions()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(new(HttpStatusCode.OK, Envelope(new[] { ValidQuestion }))).GenerateAsync(Request("Test", "Test", 2)));
    }

    [Fact]
    public async Task SingleObjectInsteadOfAnArrayIsAcceptedForOneQuestion()
    {
        // phi3:mini often answers a single-question request with a bare object rather than a
        // one element array, so the parser has to accept both shapes.
        var handler = new ResponseHandler(HttpStatusCode.OK, Envelope(ValidQuestion));
        var output = await Generator(handler).GenerateAsync(Request("Test", "Test", 1));

        Assert.Equal("Explain the three layers", Assert.Single(output).Content);
    }

    [Fact]
    public async Task SingleObjectStillFailsWhenMoreThanOneQuestionWasRequested()
    {
        var handler = new ResponseHandler(HttpStatusCode.OK, Envelope(ValidQuestion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(handler).GenerateAsync(Request("Test", "Test", 2)));
    }

    [Fact]
    public async Task EmptyObjectIsNotMistakenForAValidQuestion()
    {
        var handler = new ResponseHandler(HttpStatusCode.OK, Envelope(new { }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(handler).GenerateAsync(Request("Test", "Test", 1)));
    }

    [Fact]
    public async Task ProviderMustReturnAllowedBloomAndTwoFollowUps()
    {
        var question = ValidQuestion; question.BloomLevel = "Invalid"; question.FollowUpQuestions = [];
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(new(HttpStatusCode.OK, Envelope(new[] { question }))).GenerateAsync(Request("Test", "Test", 1)));
    }

    [Fact]
    public async Task EmptyQuestionListIsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(new(HttpStatusCode.OK, Envelope(Array.Empty<GeneratedVivaQuestion>()))).GenerateAsync(Request("Test", "Test", 1)));
    }

    [Theory]
    [InlineData(0)] [InlineData(11)]
    public async Task InvalidCountIsRejectedBeforeHttpRequest(int count)
    {
        var handler = new ResponseHandler(HttpStatusCode.OK, "{}");
        await Assert.ThrowsAsync<ArgumentException>(() => Generator(handler).GenerateAsync(Request("Test", "Test", count)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task UnconfiguredSmtpIsRejectedBeforeSending()
    {
        var sender = new GmailSmtpEmailSender(Options.Create(new GmailSmtpOptions()));
        Assert.False(sender.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendVerificationCodeAsync("test@gmail.com", "Test", "123456"));
    }

    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public string? Key { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Path = request.RequestUri!.AbsolutePath;
            Key = request.Headers.GetValues("x-goog-api-key").Single();
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(status) { Content = new StringContent(body) };
        }
    }
}
