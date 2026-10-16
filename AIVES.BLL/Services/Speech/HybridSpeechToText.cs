using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIVES.DTO;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Speech;

public sealed class HybridSpeechToText : ISpeechToText
{
    private readonly WhisperSpeechToText whisper;
    private readonly SystemSpeechToText system;
    private readonly GeminiSpeechClient gemini;

    public HybridSpeechToText(WhisperSpeechToText whisper, SystemSpeechToText system, GeminiSpeechClient gemini)
    {
        this.whisper = whisper;
        this.system = system;
        this.gemini = gemini;
    }

    public bool IsAvailable => whisper.IsAvailable || system.IsAvailable || gemini.IsEnabled;

    public async Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, AppLanguage language, string? prompt, bool preview,
        CancellationToken cancellationToken = default)
    {
        if (whisper.IsAvailable)
        {
            var result = await whisper.TranscribeAsync(samples, language, prompt, preview, cancellationToken);
            if (!string.IsNullOrEmpty(result))
                return result;
        }

        if (gemini.IsEnabled && !preview)
        {
            var result = await gemini.TranscribeAnswerAsync(samples, language, Vocabulary(prompt), cancellationToken);
            if (!string.IsNullOrEmpty(result))
                return result;
        }

        if (system.IsAvailable)
        {
            return await system.TranscribeAsync(samples, language, prompt, preview, cancellationToken);
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> Vocabulary(string? prompt) =>
        string.IsNullOrWhiteSpace(prompt)
            ? Array.Empty<string>()
            : prompt.Split(new[] { ' ', '\n', '\r', ',', ';', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2 && t.Length <= 60)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToList();
}
