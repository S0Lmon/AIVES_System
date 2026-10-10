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

    private static (ServiceProvider Services, ConcurrentQueue<Uri> Requests) Build()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "test-key",
            ["Ollama:Enabled"] = "true",
            ["Ollama:BaseUrl"] = OllamaUrl,
            ["Ollama:Model"] = "test-model"
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
