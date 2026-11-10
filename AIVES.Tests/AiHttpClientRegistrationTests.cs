using AIVES.BLL;
using AIVES.BLL.Services.Ai;
using AIVES.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System.Collections.Concurrent;
using System.Net;

namespace AIVES.Tests;

/// <summary>
/// Gemini and Ollama generators share an interface. Their typed HttpClients must still be
/// configured separately, otherwise the last registration's BaseAddress wins for both.
/// </summary>
[Trait("Requirement", "FR-04")]
public sealed class AiHttpClientRegistrationTests
{
    private const string OllamaUrl = "http://ollama-test:11434/";

    [Theory]
    [InlineData(AiProvider.Gemini, "generativelanguage.googleapis.com")]
    [InlineData(AiProvider.Ollama, "ollama-test")]
    public async Task QuestionGeneratorsCallTheirOwnHost(AiProvider provider, string expectedHost)
    {
        var (services, requests) = Build();
        var generator = services.GetServices<IQuestionGenerator>().Single(g => g.Provider == provider);

        await Assert.ThrowsAnyAsync<Exception>(() => generator.GenerateAsync(new QuestionGenerationRequest("Subject", "Topic", null, 1)));

        Assert.Equal(expectedHost, Assert.Single(requests).Host);
    }

    [Theory]
    [InlineData(AiProvider.Gemini, "generativelanguage.googleapis.com")]
    [InlineData(AiProvider.Ollama, "ollama-test")]
    public async Task RubricGeneratorsCallTheirOwnHost(AiProvider provider, string expectedHost)
    {
        var (services, requests) = Build();
        var generator = services.GetServices<IRubricGenerator>().Single(g => g.Provider == provider);

        await Assert.ThrowsAnyAsync<Exception>(() => generator.GenerateAsync(new RubricGenerationRequest("Subject", "Topic", 3, 3)));

        Assert.Equal(expectedHost, Assert.Single(requests).Host);
    }

    // Ollama:PreferLocal: the app's real registrations ask the local model first and fall back to Gemini.
    [Trait("Requirement", "FR-08")]
    [Trait("Requirement", "FR-09")]
    [Fact]
    public async Task PreferLocalSendsFollowUpsAndGradingToOllamaFirstThenGemini()
    {
        var (services, requests) = Build(preferLocal: true);
        using var scope = services.CreateScope();

        var followUps = scope.ServiceProvider.GetRequiredService<AIVES.BLL.Services.Interview.IFollowUpGenerator>();
        Assert.IsType<AIVES.BLL.Services.Interview.RoutedFollowUpGenerator>(followUps);
        await Assert.ThrowsAnyAsync<Exception>(() => followUps.DecideAsync(new FollowUpRequest(AppLanguage.Vi, "S", "Q?", "A", [new("Q?", "a")], 1)));
        Assert.Equal("ollama-test", requests.First().Host);
        Assert.Contains(requests, uri => uri.Host == "generativelanguage.googleapis.com");

        requests.Clear();
        var grader = scope.ServiceProvider.GetRequiredService<AIVES.BLL.Services.Grading.IAnswerGrader>();
        Assert.Equal("ollama/test-model", grader.ModelName);
        await Assert.ThrowsAnyAsync<Exception>(() => grader.GradeAsync(new GradingRequest(AppLanguage.Vi, "S", "Q?", "A", "Understand", null, 10, [new("Q?", "a")], [])));
        Assert.Equal("ollama-test", requests.First().Host);
        Assert.Contains(requests, uri => uri.Host == "generativelanguage.googleapis.com");
    }

    private static (ServiceProvider Services, ConcurrentQueue<Uri> Requests) Build(bool preferLocal = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "test-key",
            ["Ollama:Enabled"] = "true",
            ["Ollama:BaseUrl"] = OllamaUrl,
            ["Ollama:Model"] = "test-model",
            ["Ollama:PreferLocal"] = preferLocal ? "true" : "false"
        }).Build();
        var requests = new ConcurrentQueue<Uri>();
        var services = new ServiceCollection();
        services.AddAives(configuration);
        // Every client gets a handler that records the target and fails, so nothing leaves the test.
        services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder =>
            builder.PrimaryHandler = new RecordingHandler(requests)));
        return (services.BuildServiceProvider(), requests);
    }

    private sealed class RecordingHandler(ConcurrentQueue<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
