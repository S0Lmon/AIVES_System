using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIVES.Tests;

public sealed class EdgeTtsTests
{
    [Fact]
    public void EdgeVoiceCatalogExposesOneFemaleAndOneMaleVoicePerLanguage()
    {
        var vietnamese = EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.Vi);
        var english = EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.En);

        Assert.Equal(2, vietnamese.Count);
        Assert.Single(vietnamese, voice => voice.IsFemale);
        Assert.Single(vietnamese, voice => !voice.IsFemale);
        Assert.Equal(2, english.Count);
        Assert.Single(english, voice => voice.IsFemale);
        Assert.Single(english, voice => !voice.IsFemale);
        Assert.Contains(vietnamese, voice => voice.Id == "vi-VN-HoaiMyNeural" && voice.IsFemale);
        Assert.Contains(vietnamese, voice => voice.Id == "vi-VN-NamMinhNeural" && !voice.IsFemale);
        Assert.Contains(english, voice => voice.Id == "en-US-AriaNeural" && voice.IsFemale);
        Assert.Contains(english, voice => voice.Id == "en-US-GuyNeural" && !voice.IsFemale);
    }

    [Fact]
    public async Task ConfiguredTtsRoutesSelectedEdgeVoiceAndReturnsItsMp3ContentType()
    {
        var provider = new FakeEdgeTtsProvider();
        var speech = CreateConfiguredSpeech(provider);
        var voice = EdgeTtsVoiceCatalog.Selection(EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.Vi).Single(item => !item.IsFemale));

        var audio = await speech.SynthesizeAsync("Xin chào", AppLanguage.Vi, 1, voice);

        Assert.Equal("audio/mpeg", audio.ContentType);
        Assert.Equal([1, 2, 3], audio.Data);
        Assert.Equal("vi-VN-NamMinhNeural", provider.LastVoice);
        Assert.True(speech.IsAvailable(AppLanguage.Vi, voice));
        Assert.True(speech.UsesOnlineService(AppLanguage.Vi, voice));
    }

    [Fact]
    public async Task UnselectedVoicePrefersEdgeFemaleWhenSystemSpeechHasNoFemaleVoice()
    {
        var provider = new FakeEdgeTtsProvider();
        var speech = CreateConfiguredSpeech(provider);

        var audio = await speech.SynthesizeAsync("Xin chào", AppLanguage.Vi, 1);

        Assert.Equal("audio/mpeg", audio.ContentType);
        Assert.Equal("vi-VN-HoaiMyNeural", provider.LastVoice);
        Assert.True(speech.UsesOnlineService(AppLanguage.Vi, null));
    }

    [Fact]
    public async Task EdgeVoiceCannotBeUsedForAnotherLanguage()
    {
        var speech = CreateConfiguredSpeech(new FakeEdgeTtsProvider());
        var vietnameseVoice = EdgeTtsVoiceCatalog.Selection(EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.Vi)[0]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            speech.SynthesizeAsync("Hello", AppLanguage.En, 1, vietnameseVoice));
        Assert.False(speech.IsAvailable(AppLanguage.En, vietnameseVoice));
    }

    [Fact]
    public async Task LiveEdgeTtsVoicesReturnMp3WhenExplicitlyEnabled()
    {
        if (Environment.GetEnvironmentVariable("AIVES_EDGE_TTS_LIVE_TESTS") != "1")
            return;

        var python = Environment.GetEnvironmentVariable("EDGE_TTS_PYTHON") ?? "python";
        var provider = new EdgeTtsProvider(
            Options.Create(new EdgeTtsOptions { PythonPath = python, TimeoutSeconds = 45 }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<EdgeTtsProvider>.Instance);
        Assert.True(provider.IsAvailable);

        foreach (var voice in EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.Vi)
                     .Concat(EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.En)))
        {
            var sample = voice.Language == AppLanguage.Vi ? "Xin chào." : "Hello.";
            var audio = await provider.SynthesizeMp3Async(sample, voice.Id, 1, useCache: false, cancellationToken: CancellationToken.None);
            Assert.True(audio.Length > 4);
            Assert.Equal(0xFF, audio[0]);
            Assert.Equal(0xE0, audio[1] & 0xE0);
        }
    }

    private static ConfiguredTextToSpeech CreateConfiguredSpeech(IEdgeTtsProvider edge) =>
        new(new SystemTextToSpeech(new MemoryCache(new MemoryCacheOptions()), new EmptySpeechCatalog()), edge);

    private sealed class EmptySpeechCatalog : ISystemSpeechCatalog
    {
        public IReadOnlyList<InstalledSpeechEngine> Voices(AppLanguage language) => [];
        public InstalledSpeechEngine? SelectVoice(AppLanguage language, string? name) => null;
    }

    private sealed class FakeEdgeTtsProvider : IEdgeTtsProvider
    {
        public bool IsAvailable => true;
        public string? LastVoice { get; private set; }

        public Task<byte[]> SynthesizeMp3Async(string text, string voiceId, double speed, bool useCache, CancellationToken cancellationToken)
        {
            LastVoice = voiceId;
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }
}
