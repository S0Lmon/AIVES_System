using System.Collections.Concurrent;
using AIVES.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace AIVES.BLL.Services.Speech;

/// <summary>
/// Reads questions aloud with sherpa-onnx and a Piper voice per language. Every candidate in an exam
/// hears the same questions, so the WAV is cached by text, language and speed.
/// </summary>
public sealed class SherpaTextToSpeech(IOptions<SpeechOptions> options, IHostEnvironment environment, IMemoryCache cache,
    ILogger<SherpaTextToSpeech> logger) : ITextToSpeech, IDisposable
{
    private readonly ConcurrentDictionary<AppLanguage, Lazy<Voice>> voices = new();

    private sealed record Voice(OfflineTts Engine, Lock Gate);

    public bool IsAvailable(AppLanguage language) => ModelFile(VoiceFolder(language)) is not null;

    public async Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Nothing to read aloud.", nameof(text));
        speed = Math.Clamp(speed, 0.5, 2.0);
        var key = (nameof(SherpaTextToSpeech), language, Math.Round(speed, 2), text.Trim());
        if (cache.TryGetValue(key, out byte[]? cached) && cached is not null)
            return cached;

        var voice = voices.GetOrAdd(language, lang => new Lazy<Voice>(() => Load(lang))).Value;
        // Synthesis is CPU-bound native work; keep it off the request thread. One sentence at a time
        // per voice, because the engine is not documented as thread-safe.
        var wav = await Task.Run(() =>
        {
            lock (voice.Gate)
            {
                var audio = voice.Engine.Generate(text.Trim(), (float)speed, 0);
                return Audio.ToWav(audio.Samples, audio.SampleRate);
            }
        }, cancellationToken);
        cache.Set(key, wav, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
        return wav;
    }

    private Voice Load(AppLanguage language)
    {
        var folder = VoiceFolder(language);
        var model = ModelFile(folder) ?? throw new InvalidOperationException($"No {language} voice is installed.");
        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = model;
        config.Model.Vits.Tokens = Path.Combine(folder, "tokens.txt");
        config.Model.Vits.DataDir = Path.Combine(folder, "espeak-ng-data");
        config.Model.NumThreads = 4;
        config.MaxNumSentences = 1;
        var engine = new OfflineTts(config);
        logger.LogInformation("Loaded {Language} voice {Voice}", language, Path.GetFileName(folder));
        return new Voice(engine, new Lock());
    }

    private string VoiceFolder(AppLanguage language)
    {
        var value = options.Value;
        return Path.Combine(SpeechPaths.Root(value, environment), language == AppLanguage.Vi ? value.VietnameseVoice : value.EnglishVoice);
    }

    private static string? ModelFile(string folder) =>
        Directory.Exists(folder) && File.Exists(Path.Combine(folder, "tokens.txt"))
            ? Directory.EnumerateFiles(folder, "*.onnx").FirstOrDefault()
            : null;

    public void Dispose()
    {
        foreach (var voice in voices.Values.Where(voice => voice.IsValueCreated))
            voice.Value.Engine.Dispose();
    }
}
