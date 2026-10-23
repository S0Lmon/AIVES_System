using AIVES.BLL.Services.Gemini;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace AIVES.Tests;

public sealed class GeminiSpeechTests
{
    private static (GeminiSpeechClient Client, StubHandler Handler, MovableClock Clock) Create(
        SpeechProvider tts = SpeechProvider.Local, string key = "test-key")
    {
        var handler = new StubHandler();
        var clock = new MovableClock(DateTimeOffset.Parse("2026-10-08T00:00:00Z"));
        var client = new GeminiSpeechClient(new StubFactory(handler),
            Options.Create(new GeminiOptions { ApiKey = key }),
            Options.Create(new SpeechOptions { TtsProvider = tts, GeminiTtsTimeoutSeconds = 2 }),
            clock, NullLogger<GeminiSpeechClient>.Instance);
        return (client, handler, clock);
    }

    [Fact]
    public async Task NothingIsSentWhenGeminiIsNotTheVoiceOrHasNoKey()
    {
        var (local, localHandler, _) = Create(tts: SpeechProvider.Local);
        var (noKey, noKeyHandler, _) = Create(tts: SpeechProvider.Gemini, key: "");

        Assert.False(local.IsTtsEnabled);
        Assert.False(noKey.IsTtsEnabled);
        Assert.Null(await local.SynthesizeWavAsync("Câu hỏi"));
        Assert.Null(await noKey.SynthesizeWavAsync("Câu hỏi"));
        Assert.Empty(localHandler.Requests);
        Assert.Empty(noKeyHandler.Requests);
    }

    [Fact]
    public async Task RateLimitFallsBackAndPausesCallsForTheDelayGeminiAsksFor()
    {
        var (client, handler, clock) = Create(tts: SpeechProvider.Gemini);
        handler.Respond(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit exceeded for model gemini-3.8-flash-lite-tts. Please retry in 21s or upgrade your tier."}}""");

        Assert.Null(await client.SynthesizeWavAsync("Câu một"));
        Assert.Null(await client.SynthesizeWavAsync("Câu hai"));
        Assert.Single(handler.Requests);

        clock.Advance(TimeSpan.FromSeconds(22));
        var wav = Audio.ToWav([0.1f, -0.1f], 24000);
        handler.Respond(HttpStatusCode.OK,
            $$"""{"steps":[{"type":"model_output","content":[{"type":"audio","mime_type":"audio/wav","data":"{{Convert.ToBase64String(wav)}}"}]}]}""");
        Assert.Equal(wav, await client.SynthesizeWavAsync("Câu ba"));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorsAndSlowAnswersFallBack()
    {
        var (client, handler, _) = Create(tts: SpeechProvider.Gemini);
        handler.Respond(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"overloaded"}}""");
        Assert.Null(await client.SynthesizeWavAsync("Câu hỏi"));

        handler.Delay = TimeSpan.FromSeconds(5);
        handler.Respond(HttpStatusCode.OK, """{"steps":[]}""");
        Assert.Null(await client.SynthesizeWavAsync("Câu hỏi khác"));
    }

    [Fact]
    public async Task TtsReturnsTheWavGeminiSent()
    {
        var (client, handler, _) = Create(tts: SpeechProvider.Gemini);
        var wav = Audio.ToWav([0.1f, -0.1f], 24000);
        handler.Respond(HttpStatusCode.OK,
            $$"""{"steps":[{"type":"model_output","content":[{"type":"audio","mime_type":"audio/wav","data":"{{Convert.ToBase64String(wav)}}"}]}]}""");

        Assert.Equal(wav, await client.SynthesizeWavAsync("Câu hỏi"));
        var body = JsonNode.Parse(Assert.Single(handler.Requests).Body)!;
        Assert.Equal("gemini-3.8-flash-lite-tts", (string?)body["model"]);
        Assert.Equal("Kore", (string?)body["generation_config"]!["speech_config"]![0]!["voice"]);
    }

    [Theory]
    [InlineData("Please retry in 21s or upgrade", 21)]
    [InlineData("Please retry in 3.5s.", 3.5)]
    public void RetryDelayIsReadFromTheMessage(string message, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), GeminiSpeechClient.RetryDelay(message));

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
            };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.OK;
        private string body = "{}";

        public sealed record Seen(string Path, string? ApiKey, string Body);

        public List<Seen> Requests { get; } = [];
        public TimeSpan Delay
        {
            get; set;
        }

        public void Respond(HttpStatusCode code, string json)
        {
            status = code;
            body = json;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(request.RequestUri!.AbsolutePath, request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

internal sealed class MovableClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset now = now;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
