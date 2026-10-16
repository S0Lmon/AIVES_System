using System;
using System.Threading;
using System.Threading.Tasks;
using AIVES.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Speech;

public sealed class HybridTextToSpeechImpl : ITextToSpeech
{
    private readonly SherpaTextToSpeech sherpa;
    private readonly SystemTextToSpeech system;
    private readonly GeminiSpeechClient gemini;
    private readonly IMemoryCache cache;

    public HybridTextToSpeechImpl(SherpaTextToSpeech sherpa, SystemTextToSpeech system, GeminiSpeechClient gemini, IMemoryCache cache)
    {
        this.sherpa = sherpa;
        this.system = system;
        this.gemini = gemini;
        this.cache = cache;
    }

    public bool IsAvailable(AppLanguage language) => gemini.IsTtsEnabled || sherpa.IsAvailable(language) || system.IsAvailable(language);

    public async Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default)
    {
        if (gemini.IsTtsEnabled)
        {
            var key = (nameof(HybridTextToSpeechImpl), text.Trim());
            if (cache.TryGetValue(key, out byte[]? cached) && cached is not null)
                return cached;
            var wav = await gemini.SynthesizeWavAsync(text, cancellationToken);
            if (wav is not null)
            {
                cache.Set(key, wav, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
                return wav;
            }
        }

        if (sherpa.IsAvailable(language))
            return await sherpa.SynthesizeWavAsync(text, language, speed, cancellationToken);

        if (system.IsAvailable(language))
            return await system.SynthesizeWavAsync(text, language, speed, cancellationToken);

        throw new InvalidOperationException("No TTS available.");
    }
}
