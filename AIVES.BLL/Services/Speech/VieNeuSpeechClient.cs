using AIVES.DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AIVES.BLL.Services.Speech;

/// <summary>
/// Reads questions with VieNeu-TTS (github.com/pnnbao97/VieNeu-TTS, Apache-2.0), a Vietnamese voice
/// model that runs as its own container with an OpenAI-style <c>POST /v1/audio/speech</c>. A failure,
/// a full queue or a slow answer returns null, and the caller falls back to the local Piper voice.
/// </summary>
public sealed class VieNeuSpeechClient(
    IHttpClientFactory httpFactory,
    IOptions<SpeechOptions> speech,
    TimeProvider clock,
    ILogger<VieNeuSpeechClient> logger)
{
    public const string HttpClientName = "VieNeu";
    public const string Model = "vieneu-v3-turbo";
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(5);
    // UTC ticks; read and written with Interlocked because several candidates can be asked at once.
    private long cooldownUntilTicks;

    public bool IsEnabled =>
        speech.Value.TtsProvider == SpeechProvider.VieNeu && !string.IsNullOrWhiteSpace(speech.Value.VieNeuUrl);

    /// <summary>VieNeu voices are Vietnamese speakers; English questions use it only when switched on.</summary>
    public bool Reads(AppLanguage language) =>
        IsEnabled && (language == AppLanguage.Vi || speech.Value.VieNeuReadsEnglish);

    /// <param name="timeout">How long the candidate may wait; null uses <see cref="SpeechOptions.VieNeuTimeoutSeconds"/>.</param>
    public async Task<byte[]?> SynthesizeWavAsync(string text, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text) || clock.GetUtcNow().UtcTicks < Interlocked.Read(ref cooldownUntilTicks))
            return null;

        var options = speech.Value;
        var sampleRate = options.VieNeuSampleRate;
        var body = new JsonObject
        {
            ["model"] = Model,
            ["input"] = text.Trim(),
            // Raw PCM, wrapped here: VieNeu's own WAV header declares an unknown length because it streams.
            ["response_format"] = "pcm",
            ["sample_rate"] = sampleRate
        };
        // No voice means the server's default speaker.
        if (!string.IsNullOrWhiteSpace(options.VieNeuVoice))
            body["voice"] = options.VieNeuVoice.Trim();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var limit = timeout ?? TimeSpan.FromSeconds(Math.Max(1, options.VieNeuTimeoutSeconds));
        deadline.CancelAfter(limit);
        var started = clock.GetTimestamp();
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "v1/audio/speech") { Content = JsonContent.Create(body) };
            if (!string.IsNullOrWhiteSpace(options.VieNeuApiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.VieNeuApiKey);
            using var response = await httpFactory.CreateClient(HttpClientName)
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? DefaultCooldown;
                CoolDown(wait);
                logger.LogWarning("VieNeu is busy; using the local voice for {Seconds:F0} s", wait.TotalSeconds);
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("VieNeu returned HTTP {StatusCode}; using the local voice", (int)response.StatusCode);
                return null;
            }

            var pcm = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            if (pcm.Length < 2)
                return null;
            if (response.Headers.TryGetValues("X-Sample-Rate", out var rates) && int.TryParse(rates.FirstOrDefault(), out var actual))
                sampleRate = actual;
            logger.LogInformation("VieNeu read {Characters} characters ({Seconds:F1} s of audio) in {Elapsed:F1} s",
                text.Length, pcm.Length / 2.0 / sampleRate, clock.GetElapsedTime(started).TotalSeconds);
            return Audio.ToWav(Audio.FromPcm16(pcm.AsSpan(0, pcm.Length & ~1)), sampleRate);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("VieNeu took longer than {Seconds:F0} s; using the local voice", limit.TotalSeconds);
            return null;
        }
        catch (HttpRequestException ex)
        {
            CoolDown(DefaultCooldown);
            logger.LogWarning(ex, "VieNeu could not be reached; using the local voice");
            return null;
        }
    }

    private void CoolDown(TimeSpan wait) => Interlocked.Exchange(ref cooldownUntilTicks, (clock.GetUtcNow() + wait).UtcTicks);
}
