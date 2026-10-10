using AIVES.BLL.Services.Gemini;
using AIVES.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AIVES.BLL.Services.Speech;

/// <summary>Writes the kept transcript of a whole answer; null means "use the local one".</summary>
public interface IAnswerTranscriber
{
    bool IsEnabled
    {
        get;
    }

    Task<string?> TranscribeAnswerAsync(ReadOnlyMemory<float> samples, AppLanguage language, IReadOnlyList<string> vocabulary,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Gemini speech over the Interactions API: answer transcripts with the transcription model and,
/// optionally, question audio with a TTS model. Never throws for a failed call: it logs and returns
/// null so the caller keeps its local result. After a 429 it stops calling that model until the
/// delay Gemini asked for has passed, so a rate-limited exam does not wait on doomed requests.
/// </summary>
public sealed partial class GeminiSpeechClient(
    IHttpClientFactory httpFactory,
    IOptions<GeminiOptions> gemini,
    IOptions<SpeechOptions> speech,
    TimeProvider clock,
    ILogger<GeminiSpeechClient> logger) : IAnswerTranscriber
{
    // Inline requests are capped at 20 MB; base64 adds a third, so leave room for the JSON around it.
    private const int MaxInlineAudioBytes = 14 * 1024 * 1024;
    private const int MaxVocabulary = 100;
    public const string HttpClientName = "GeminiSpeech";
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private readonly ConcurrentDictionary<string, DateTimeOffset> cooldownUntil = new();

    private bool HasKey => !string.IsNullOrWhiteSpace(gemini.Value.ApiKey);

    public bool IsEnabled => HasKey && speech.Value.Provider == SpeechProvider.Gemini;

    public bool IsTtsEnabled => HasKey && speech.Value.TtsProvider == SpeechProvider.Gemini;

    public async Task<string?> TranscribeAnswerAsync(ReadOnlyMemory<float> samples, AppLanguage language, IReadOnlyList<string> vocabulary,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || !Audio.HasSpeech(samples.Span))
            return null;
        var wav = Audio.ToWav(samples.Span, Audio.SampleRate);
        if (wav.Length > MaxInlineAudioBytes)
        {
            logger.LogInformation("Answer audio is {Seconds:F0} s, too long to send inline; keeping the local transcript", samples.Length / (double)Audio.SampleRate);
            return null;
        }

        var options = speech.Value;
        // Verbatim (the default mode), never "smart": smart mode rewrites for readability, and on
        // 2026-10-08 it turned an unclear "SignalR … qua WebSocket" into "Server-Sent Events … qua HTTP",
        // so the examiner probed the candidate for something they never said.
        var config = new JsonObject
        {
            ["language_codes"] = new JsonArray(language.ToSpeechLocale())
        };
        var terms = Vocabulary(vocabulary);
        if (terms.Count > 0)
            config["custom_vocabulary"] = new JsonArray(terms.Select(term => (JsonNode)term!).ToArray());
        var body = new JsonObject
        {
            ["model"] = options.GeminiTranscribeModel,
            ["input"] = new JsonArray(new JsonObject
            {
                ["type"] = "audio",
                ["data"] = Convert.ToBase64String(wav),
                ["mime_type"] = "audio/wav"
            }),
            ["generation_config"] = new JsonObject { ["transcription_config"] = config }
        };

        var result = await SendAsync(options.GeminiTranscribeModel, body, options.GeminiTimeoutSeconds, cancellationToken);
        var text = result is null ? null : OutputText(result);
        return text is null ? null : WhisperSpeechToText.Clean(text);
    }

    /// <summary>A WAV of the question in Gemini's voice, or null to use the local voice.</summary>
    public async Task<byte[]?> SynthesizeWavAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsTtsEnabled || string.IsNullOrWhiteSpace(text))
            return null;
        var options = speech.Value;
        var body = new JsonObject
        {
            ["model"] = options.GeminiTtsModel,
            ["input"] = new JsonArray(new JsonObject
            {
                ["type"] = "user_input",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text.Trim(),
                    ["annotations"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "speech_metadata",
                        ["style"] = "a calm, clear examiner at a measured pace"
                    })
                })
            }),
            ["response_format"] = new JsonObject { ["type"] = "audio", ["mime_type"] = "audio/wav" },
            ["generation_config"] = new JsonObject { ["speech_config"] = new JsonArray(new JsonObject { ["voice"] = options.GeminiVoice }) }
        };

        var result = await SendAsync(options.GeminiTtsModel, body, options.GeminiTtsTimeoutSeconds, cancellationToken);
        var data = result is null ? null : OutputAudio(result);
        try
        {
            return data is null ? null : Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            logger.LogWarning("Gemini TTS returned audio that is not base64");
            return null;
        }
    }

    private async Task<JsonNode?> SendAsync(string model, JsonObject body, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (cooldownUntil.TryGetValue(model, out var until) && clock.GetUtcNow() < until)
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "v1beta/interactions");
            message.Headers.Add("x-goog-api-key", gemini.Value.ApiKey);
            message.Content = JsonContent.Create(body, options: JsonOptions);
            using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(message, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = RetryDelay(text) ?? DefaultCooldown;
                cooldownUntil[model] = clock.GetUtcNow() + wait;
                logger.LogWarning("Gemini {Model} is rate limited; using local speech for {Seconds:F0} s", model, wait.TotalSeconds);
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini {Model} returned HTTP {StatusCode}; using local speech", model, (int)response.StatusCode);
                return null;
            }
            return JsonNode.Parse(text);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Gemini {Model} took longer than {Seconds} s; using local speech", model, timeoutSeconds);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            logger.LogWarning(ex, "Gemini {Model} could not be used; using local speech", model);
            return null;
        }
    }

    /// <summary>Text parts of the model's output steps, joined.</summary>
    internal static string? OutputText(JsonNode result)
    {
        var parts = Outputs(result)
            .Where(part => (string?)part["type"] == "text")
            .Select(part => (string?)part["text"])
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    internal static string? OutputAudio(JsonNode result) =>
        Outputs(result).Where(part => (string?)part["type"] == "audio").Select(part => (string?)part["data"]).FirstOrDefault(data => !string.IsNullOrEmpty(data));

    private static IEnumerable<JsonNode> Outputs(JsonNode result) =>
        (result["steps"] as JsonArray ?? [])
            .Where(step => (string?)step?["type"] == "model_output")
            .SelectMany(step => step!["content"] as JsonArray ?? [])
            .OfType<JsonNode>();

    /// <summary>"Please retry in 21s" from the error message, when Gemini says how long to wait.</summary>
    public static TimeSpan? RetryDelay(string error)
    {
        var match = RetryIn().Match(error);
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300))
            : null;
    }

    /// <summary>Distinct, trimmed terms, at most the ~100 Gemini handles best.</summary>
    public static IReadOnlyList<string> Vocabulary(IEnumerable<string> terms) =>
        terms.Select(term => term.Trim()).Where(term => term.Length is > 1 and <= 60)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxVocabulary).ToList();

    [GeneratedRegex(@"retry in ([0-9.]+)\s*s", RegexOptions.IgnoreCase)]
    private static partial Regex RetryIn();
}

/// <summary>Reads questions with Gemini when that is switched on, otherwise (or on failure) locally.</summary>
public sealed class HybridTextToSpeech(SherpaTextToSpeech local, GeminiSpeechClient gemini,
    IMemoryCache cache) : ITextToSpeech
{
    public bool IsAvailable(AppLanguage language) => gemini.IsTtsEnabled || local.IsAvailable(language);

    public async Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default)
    {
        if (gemini.IsTtsEnabled)
        {
            var key = (nameof(HybridTextToSpeech), text.Trim());
            if (cache.TryGetValue(key, out byte[]? cached) && cached is not null)
                return cached;
            var wav = await gemini.SynthesizeWavAsync(text, cancellationToken);
            if (wav is not null)
            {
                cache.Set(key, wav, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
                return wav;
            }
        }
        return await local.SynthesizeWavAsync(text, language, speed, cancellationToken);
    }
}

/// <summary>Stands in for Whisper when no model is installed but Gemini writes the transcript.</summary>
public sealed class NoPreviewSpeechToText : ISpeechToText
{
    public static readonly NoPreviewSpeechToText Instance = new();

    public bool IsAvailable => false;

    public Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, AppLanguage language, string? prompt, bool preview,
        CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
}
