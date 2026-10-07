using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AIVES.BLL.Services.Gemini;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIVES.Tests;

public sealed class GeminiSpeechTests
{
    private static float[] Speech(double seconds) =>
        Enumerable.Range(0, (int)(seconds * Audio.SampleRate)).Select(i => 0.3f * MathF.Sin(i * 0.05f)).ToArray();

    private static (GeminiSpeechClient Client, StubHandler Handler, MovableClock Clock) Create(
        SpeechProvider provider = SpeechProvider.Gemini, SpeechProvider tts = SpeechProvider.Local, string key = "test-key")
    {
        var handler = new StubHandler();
        var clock = new MovableClock(DateTimeOffset.Parse("2026-10-08T00:00:00Z"));
        var client = new GeminiSpeechClient(new StubFactory(handler),
            Options.Create(new GeminiOptions { ApiKey = key }),
            Options.Create(new SpeechOptions { Provider = provider, TtsProvider = tts, GeminiTimeoutSeconds = 2 }),
            clock, NullLogger<GeminiSpeechClient>.Instance);
        return (client, handler, clock);
    }

    private static string Transcript(string text) =>
        $$"""{"status":"completed","steps":[{"type":"model_output","content":[{"type":"text","text":"{{text}}"}]}]}""";

    [Fact]
    public async Task SendsTheWholeAnswerWithLanguageAndVocabularyAndReadsTheText()
    {
        var (client, handler, _) = Create();
        handler.Respond(HttpStatusCode.OK, Transcript("Kiến trúc ba lớp trong ASP.NET Core"));

        var text = await client.TranscribeAnswerAsync(Speech(1), AppLanguage.Vi, ["ASP.NET Core", "SignalR", "ASP.NET Core", " "]);

        Assert.Equal("Kiến trúc ba lớp trong ASP.NET Core", text);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1beta/interactions", request.Path);
        Assert.Equal("test-key", request.ApiKey);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal("gemini-3.5-transcribe", (string?)body["model"]);
        Assert.Equal("audio/wav", (string?)body["input"]![0]!["mime_type"]);
        var wav = Convert.FromBase64String((string)body["input"]![0]!["data"]!);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        var config = body["generation_config"]!["transcription_config"]!;
        Assert.Equal("vi-VN", (string?)config["language_codes"]![0]);
        Assert.Null(config["mode"]); // verbatim: smart mode rewrites what was said
        Assert.Equal(["ASP.NET Core", "SignalR"], config["custom_vocabulary"]!.AsArray().Select(term => (string)term!));
    }

    [Fact]
    public async Task NothingIsSentWhenGeminiIsNotTheProviderOrHasNoKeyOrHearsSilence()
    {
        var (local, localHandler, _) = Create(provider: SpeechProvider.Local);
        var (noKey, noKeyHandler, _) = Create(key: "");
        var (gemini, geminiHandler, _) = Create();

        Assert.Null(await local.TranscribeAnswerAsync(Speech(1), AppLanguage.Vi, []));
        Assert.Null(await noKey.TranscribeAnswerAsync(Speech(1), AppLanguage.Vi, []));
        Assert.Null(await gemini.TranscribeAnswerAsync(new float[Audio.SampleRate], AppLanguage.Vi, []));
        Assert.False(local.IsEnabled);
        Assert.Empty(localHandler.Requests);
        Assert.Empty(noKeyHandler.Requests);
        Assert.Empty(geminiHandler.Requests);
    }

    [Fact]
    public async Task RateLimitFallsBackAndPausesCallsForTheDelayGeminiAsksFor()
    {
        var (client, handler, clock) = Create();
        handler.Respond(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit exceeded for model gemini-3.5-transcribe (limit: 3 requests per minute on Free Tier). Please retry in 21s or upgrade your tier."}}""");

        Assert.Null(await client.TranscribeAnswerAsync(Speech(1), AppLanguage.Vi, []));
        Assert.Null(await client.TranscribeAnswerAsync(Speech(1), AppLanguage.Vi, []));
        Assert.Single(handler.Requests);

        clock.Advance(TimeSpan.FromSeconds(22));
        handler.Respond(HttpStatusCode.OK, Transcript("đã hết chờ"));
        Assert.Equal("đã hết chờ", await client.TranscribeAnswerAsync(Speech(1), AppLanguage.Vi, []));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorsAndSlowAnswersFallBack()
    {
        var (client, handler, _) = Create();
        handler.Respond(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"overloaded"}}""");
        Assert.Null(await client.TranscribeAnswerAsync(Speech(1), AppLanguage.Vi, []));

        handler.Delay = TimeSpan.FromSeconds(5);
        handler.Respond(HttpStatusCode.OK, Transcript("quá muộn"));
        Assert.Null(await client.TranscribeAnswerAsync(Speech(1), AppLanguage.Vi, []));
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
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.OK;
        private string body = "{}";

        public sealed record Seen(string Path, string? ApiKey, string Body);

        public List<Seen> Requests { get; } = [];
        public TimeSpan Delay { get; set; }

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
