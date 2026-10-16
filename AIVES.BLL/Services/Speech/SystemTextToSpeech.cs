using System;
using System.IO;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using AIVES.DTO;
using Microsoft.Extensions.Caching.Memory;

namespace AIVES.BLL.Services.Speech;

public sealed class SystemTextToSpeech : ITextToSpeech
{
    private readonly IMemoryCache cache;

    public SystemTextToSpeech(IMemoryCache cache)
    {
        this.cache = cache;
    }

    public bool IsAvailable(AppLanguage language) => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public async Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable(language) || string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("System.Speech TTS not available.");
        speed = Math.Clamp(speed, 0.5, 2.0);
        var key = (nameof(SystemTextToSpeech), language, Math.Round(speed, 2), text.Trim());
        if (cache.TryGetValue(key, out byte[]? cached) && cached is not null)
            return cached;

        using var synthesizer = new SpeechSynthesizer();
        synthesizer.Rate = (int)Math.Round((speed - 1.0) * 10);
        using var stream = new MemoryStream();
        synthesizer.SetOutputToWaveStream(stream);
        synthesizer.Speak(text.Trim());
        var wav = stream.ToArray();
        cache.Set(key, wav, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
        return await Task.FromResult(wav);
    }
}
