using AIVES.BLL.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace AIVES.Tests;

public sealed class OllamaTests
{
    private static OllamaQuestionGenerator Generator(HttpMessageHandler handler, bool enabled = true) => new(
        new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") },
        Options.Create(new OllamaOptions { Enabled = enabled, Model = "phi3:mini", BaseUrl = "http://localhost:11434" }),
        NullLogger<OllamaQuestionGenerator>.Instance);

    private static string Question(int index) => JsonSerializer.Serialize(new
    {
        content = $"Question {index}",
        expectedAnswer = $"Answer {index}",
        bloomLevel = "Understand",
        difficulty = "Intermediate",
        followUpQuestions = new[] { $"First {index}", $"Second {index}" }
    });

    private static string Envelope(string text) => JsonSerializer.Serialize(new { response = text });

    [Fact]
    public async Task OneQuestionPerCallKeepsTheExactCountForSmallLocalModels()
    {
        // phi3:mini answers a single object even when asked for several, so the generator has to
        // make one request per question instead of trusting the model for the count.
        var handler = new CountingHandler(index => Envelope(Question(index)));
        var questions = await Generator(handler).GenerateAsync(
            new QuestionGenerationRequest("Software", "Layers", null, 3));

        Assert.Equal(3, questions.Count);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(["Question 0", "Question 1", "Question 2"], questions.Select(question => question.Content));
    }

    [Fact]
    public async Task SingleQuestionRequestMakesExactlyOneCall()
    {
        var handler = new CountingHandler(_ => Envelope(Question(1)));
        var questions = await Generator(handler).GenerateAsync(
            new QuestionGenerationRequest("Software", "Layers", null, 1));

        Assert.Single(questions);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task SingleObjectResponseIsAcceptedForOneQuestion()
    {
        var handler = new CountingHandler(_ => Envelope(Question(1)));
        var question = Assert.Single(await Generator(handler).GenerateAsync(
            new QuestionGenerationRequest("Software", "Layers", null, 1)));

        Assert.Equal("Question 1", question.Content);
        Assert.Equal(2, question.FollowUpQuestions.Count);
    }

    [Fact]
    public async Task MissingBloomLevelIsRejected()
    {
        var handler = new CountingHandler(_ => Envelope(JsonSerializer.Serialize(new
        {
            content = "Question",
            expectedAnswer = "Answer",
            bloomLevel = "Invented",
            followUpQuestions = new[] { "a", "b" }
        })));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(handler).GenerateAsync(
            new QuestionGenerationRequest("Software", "Layers", null, 1)));
    }

    [Fact]
    public async Task ProviderErrorDoesNotLeakTheResponseBody()
    {
        var handler = new CountingHandler(_ => "private-ollama-detail", HttpStatusCode.InternalServerError);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(handler).GenerateAsync(
            new QuestionGenerationRequest("Software", "Layers", null, 1)));

        Assert.DoesNotContain("private-ollama-detail", error.Message);
    }

    [Fact]
    public async Task DisabledOllamaIsRejectedBeforeAnyHttpCall()
    {
        var handler = new CountingHandler(_ => Envelope(Question(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(handler, enabled: false).GenerateAsync(
            new QuestionGenerationRequest("Software", "Layers", null, 1)));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ReachabilityReportsTheLocalServerAndThePulledModel()
    {
        var handler = new CountingHandler(_ => JsonSerializer.Serialize(new
        {
            models = new[] { new { name = "phi3:mini" } }
        }));
        var generator = new OllamaQuestionGenerator(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") },
            Options.Create(new OllamaOptions { Enabled = true, Model = "phi3:mini" }),
            NullLogger<OllamaQuestionGenerator>.Instance);

        Assert.True(await generator.IsReachableAsync());
        Assert.Equal("/api/tags", handler.LastPath);
    }

    [Fact]
    public async Task ReachabilityIsFalseWhenTheModelIsNotPulled()
    {
        var handler = new CountingHandler(_ => JsonSerializer.Serialize(new
        {
            models = new[] { new { name = "llama3:8b" } }
        }), HttpStatusCode.OK);
        var generator = new OllamaQuestionGenerator(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") },
            Options.Create(new OllamaOptions { Enabled = true, Model = "phi3:mini" }),
            NullLogger<OllamaQuestionGenerator>.Instance);

        Assert.False(await generator.IsReachableAsync());
    }

    private sealed class CountingHandler(Func<int, string> body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls
        {
            get; private set;
        }
        public string? LastPath
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = Calls++;
            LastPath = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body(current)) });
        }
    }
}