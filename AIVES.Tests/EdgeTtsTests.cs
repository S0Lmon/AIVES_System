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
    public void ASavedVoiceThatIsNoLongerInstalledStillCountsAsAvailable()
    {
        var speech = new ConfiguredTextToSpeech(
            new SystemTextToSpeech(new MemoryCache(new MemoryCacheOptions()),
                new FakeSpeechCatalog(new InstalledSpeechEngine("Microsoft Zira Desktop", "en-US", true))),
            new FakeEdgeTtsProvider(available: false));

        // A stale System.Speech name and an Edge voice without its runtime both fall back to the
        // installed voice, so the viva keeps speaking instead of going silent.
        Assert.True(speech.IsAvailable(AppLanguage.En, "Microsoft HoaiMy"));
        Assert.True(speech.IsAvailable(AppLanguage.En, EdgeTtsVoiceCatalog.Selection(EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.En)[0])));
        Assert.False(speech.UsesOnlineService(AppLanguage.En, EdgeTtsVoiceCatalog.Selection(EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.En)[0])));
        // A language nothing can read stays unavailable, and an Edge voice for another language stays rejected.
        Assert.False(speech.IsAvailable(AppLanguage.Vi, "Microsoft An"));
        Assert.False(speech.IsAvailable(AppLanguage.En, EdgeTtsVoiceCatalog.Selection(EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.Vi)[0])));
    }

    [Fact]
    public async Task AMissingEdgeRuntimeStillReadsTheQuestionWithAnInstalledVoice()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var speech = new ConfiguredTextToSpeech(
            new SystemTextToSpeech(new MemoryCache(new MemoryCacheOptions()), new SystemSpeechCatalog()),
            new FakeEdgeTtsProvider(available: false));
        if (!speech.IsAvailable(AppLanguage.En))
            return;   // the runner has no English voice at all; nothing to fall back to

        var saved = EdgeTtsVoiceCatalog.Selection(EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.En)[0]);
        var audio = await speech.SynthesizeAsync("Hello, I am the virtual examiner.", AppLanguage.En, 1, saved);

        Assert.Equal("audio/wav", audio.ContentType);
        Assert.True(audio.Data.Length > 44);   // more than the WAV header
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

    private sealed class FakeSpeechCatalog(params InstalledSpeechEngine[] voices) : ISystemSpeechCatalog
    {
        public IReadOnlyList<InstalledSpeechEngine> Voices(AppLanguage language) =>
            voices.Where(voice => language == AppLanguage.Vi
                ? voice.Culture.StartsWith("vi", StringComparison.OrdinalIgnoreCase)
                : voice.Culture.StartsWith("en", StringComparison.OrdinalIgnoreCase)).ToArray();

        public InstalledSpeechEngine? SelectVoice(AppLanguage language, string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? Voices(language).FirstOrDefault(voice => voice.IsFemale) ?? Voices(language).FirstOrDefault()
                : Voices(language).FirstOrDefault(voice => string.Equals(voice.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeEdgeTtsProvider(bool available = true) : IEdgeTtsProvider
    {
        public bool IsAvailable { get; } = available;
        public string? LastVoice { get; private set; }

        public Task<byte[]> SynthesizeMp3Async(string text, string voiceId, double speed, bool useCache, CancellationToken cancellationToken)
        {
            LastVoice = voiceId;
            return Task.FromResult<byte[]>([1, 2, 3]);
        }
    }
}
