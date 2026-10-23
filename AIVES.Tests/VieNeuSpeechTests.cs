using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json.Nodes;

namespace AIVES.Tests;

public sealed class VieNeuSpeechTests
{
    private static readonly byte[] Wav = [.. "RIFF"u8, .. new byte[60]];

    private static (VieNeuSpeechClient Client, Handler Handler, MovableClock Clock) Create(string? url = "http://vieneu.test:8800")
    {
        var handler = new Handler();
        var clock = new MovableClock(DateTimeOffset.Parse("2026-10-24T00:00:00Z"));
        var client = new VieNeuSpeechClient(new Factory(handler),
            Options.Create(new SpeechOptions { VieNeuUrl = url, VieNeuVoice = "Mai Anh", VieNeuTimeoutSeconds = 2 }),
            clock, NullLogger<VieNeuSpeechClient>.Instance);
        return (client, handler, clock);
    }

    [Fact]
    public async Task PostsTheQuestionToTheOpenAiEndpointAndReturnsTheWav()
    {
        var (client, handler, _) = Create();
        handler.Reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Wav) };

        var wav = await client.SynthesizeWavAsync("  Lớp nghiệp vụ là gì?  ", 1.25);

        Assert.Equal(Wav, wav);
        var request = Assert.Single(handler.Seen);
        Assert.Equal("/v1/audio/speech", request.Path);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal("vieneu-v3-turbo", (string?)body["model"]);
        Assert.Equal("Mai Anh", (string?)body["voice"]);
        Assert.Equal("Lớp nghiệp vụ là gì?", (string?)body["input"]);
        Assert.Equal("wav", (string?)body["response_format"]);
        Assert.Equal(1.25, (double?)body["speed"]);
    }

    [Fact]
    public void AStreamedHeaderOfUnknownLengthGetsTheRealSizes()
    {
        var wav = new byte[44 + 100];
        "RIFF"u8.CopyTo(wav);
        "data"u8.CopyTo(wav.AsSpan(36));
        BitConverter.TryWriteBytes(wav.AsSpan(4, 4), int.MaxValue);
        BitConverter.TryWriteBytes(wav.AsSpan(40, 4), int.MaxValue);

        var fixedWav = VieNeuSpeechClient.WithKnownLength(wav);

        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(fixedWav, 4));
        Assert.Equal(100, BitConverter.ToInt32(fixedWav, 40));
    }

    [Fact]
    public async Task OnlyVietnameseWithAConfiguredServerUsesIt()
    {
        var (on, _, _) = Create();
        var (off, offHandler, _) = Create(url: "");

        Assert.True(on.IsAvailable(AppLanguage.Vi));
        Assert.False(on.IsAvailable(AppLanguage.En));
        Assert.False(off.IsAvailable(AppLanguage.Vi));
        Assert.Null(await off.SynthesizeWavAsync("Xin chào", 1));
        Assert.Empty(offHandler.Seen);
    }

    [Fact]
    public async Task AFailureOrANonWavReplyPausesTheServerForThirtySeconds()
    {
        var (client, handler, clock) = Create();
        handler.Reply = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        Assert.Null(await client.SynthesizeWavAsync("Câu một", 1));
        Assert.False(client.IsAvailable(AppLanguage.Vi));
        Assert.Null(await client.SynthesizeWavAsync("Câu hai", 1));
        Assert.Single(handler.Seen); // the second call never left the app

        clock.Advance(VieNeuSpeechClient.Cooldown + TimeSpan.FromSeconds(1));
        handler.Reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not audio at all, just text that is long enough") };
        Assert.Null(await client.SynthesizeWavAsync("Câu ba", 1));
        Assert.Equal(2, handler.Seen.Count);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public sealed record Request(string Path, string Body);

        public List<Request> Seen { get; } = [];

        public HttpResponseMessage Reply { get; set; } = new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add(new Request(request.RequestUri!.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Reply;
        }
    }
}
