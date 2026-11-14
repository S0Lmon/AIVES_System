using System.IO;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using AIVES.DTO;
using Microsoft.Extensions.Caching.Memory;

namespace AIVES.BLL.Services.Speech;

public sealed class SystemTextToSpeech(IMemoryCache cache, ISystemSpeechCatalog catalog) : ITextToSpeech
{
    public bool IsAvailable(AppLanguage language) => catalog.SelectVoice(language, null) is not null;

    public bool IsAvailable(AppLanguage language, string? preferredVoiceName) =>
        string.IsNullOrWhiteSpace(preferredVoiceName)
            ? IsAvailable(language)
            : catalog.SelectVoice(language, preferredVoiceName) is not null;

    public bool HasFemaleVoice(AppLanguage language) => catalog.Voices(language).Any(voice => voice.IsFemale);

    public Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default) =>
        SynthesizeWavAsync(text, language, speed, null, cancellationToken);

    public Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, string? preferredVoiceName, CancellationToken cancellationToken = default) =>
        SynthesizeWavAsync(text, language, speed, preferredVoiceName, useCache: true, cancellationToken: cancellationToken);

    public async Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, string? preferredVoiceName, bool useCache, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable(language) || string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("System.Speech TTS not available.");
        var voice = catalog.SelectVoice(language, preferredVoiceName) ?? catalog.SelectVoice(language, null)
            ?? throw new InvalidOperationException($"The configured System.Speech voice is unavailable for {language}.");
        speed = Math.Clamp(speed, 0.5, 2.0);
        var key = (nameof(SystemTextToSpeech), voice.Name, language, Math.Round(speed, 2), text.Trim());
        if (useCache && cache.TryGetValue(key, out byte[]? cached) && cached is not null)
            return await Task.FromResult(cached);

        var wav = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var synthesizer = new SpeechSynthesizer();
            synthesizer.SelectVoice(voice.Name);
            synthesizer.Rate = (int)Math.Round((speed - 1.0) * 10);
            using var stream = new MemoryStream();
            synthesizer.SetOutputToWaveStream(stream);
            synthesizer.Speak(text.Trim());
            return stream.ToArray();
        }, cancellationToken);
        if (useCache)
            cache.Set(key, wav, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
        return wav;
    }

    public async Task<SpeechAudio> SynthesizeAsync(
        string text,
        AppLanguage language,
        double speed,
        string? preferredVoiceName = null,
        bool useCache = true,
        CancellationToken cancellationToken = default) =>
        new(await SynthesizeWavAsync(text, language, speed, preferredVoiceName, useCache, cancellationToken), "audio/wav");
}
