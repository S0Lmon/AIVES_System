using AIVES.DTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AIVES.BLL.Services.Speech;

/// <summary>
/// Vietnamese question audio from a VieNeu-TTS server (OpenAI-compatible <c>/v1/audio/speech</c>).
/// Never throws for a failed call: it logs and returns null so the caller falls back to another voice.
/// After a failure it stays quiet for <see cref="Cooldown"/>, so a stopped server costs one slow
/// request rather than one per question.
/// </summary>
public sealed class VieNeuSpeechClient(
    IHttpClientFactory httpFactory,
    IOptions<SpeechOptions> speech,
    TimeProvider clock,
    ILogger<VieNeuSpeechClient> logger)
{
    public const string HttpClientName = "VieNeuSpeech";
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    private long retryAtTicks;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(speech.Value.VieNeuUrl);

    public bool IsAvailable(AppLanguage language) =>
        IsEnabled && language == AppLanguage.Vi && clock.GetUtcNow().UtcTicks >= Interlocked.Read(ref retryAtTicks);

    public async Task<byte[]?> SynthesizeWavAsync(string text, double speed, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable(AppLanguage.Vi) || string.IsNullOrWhiteSpace(text))
            return null;
        var options = speech.Value;
        var started = clock.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.VieNeuTimeoutSeconds)));
        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.VieNeuUrl!.TrimEnd('/') + "/"), "v1/audio/speech"))
            {
                Content = JsonContent.Create(new
                {
                    model = options.VieNeuModel,
                    voice = options.VieNeuVoice,
                    input = text.Trim(),
                    response_format = "wav",
                    speed = Math.Clamp(speed, 0.5, 2.0)
                })
            };
            if (!string.IsNullOrWhiteSpace(options.VieNeuApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.VieNeuApiKey);
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("VieNeu TTS answered {Status}; reading the question with another voice", (int)response.StatusCode);
                Pause();
                return null;
            }
            var wav = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
            {
                logger.LogWarning("VieNeu TTS returned {Bytes} bytes that are not a WAV file", wav.Length);
                Pause();
                return null;
            }
            logger.LogInformation("VieNeu TTS read {Chars} characters ({Bytes} bytes) in {Ms:F0} ms", text.Length, wav.Length, clock.GetElapsedTime(started).TotalMilliseconds);
            return WithKnownLength(wav);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "VieNeu TTS is unreachable or too slow; reading the question with another voice");
            Pause();
            return null;
        }
    }

    /// <summary>
    /// The server streams its WAV with a header of unknown length (sizes of 0x7FFFFFFF or more), which
    /// leaves a browser unable to tell how long the question is. Once the whole body is here, write the real sizes.
    /// </summary>
    public static byte[] WithKnownLength(byte[] wav)
    {
        if (wav.Length <= 44 || wav[36] != 'd' || wav[37] != 'a' || wav[38] != 't' || wav[39] != 'a')
            return wav;
        BitConverter.TryWriteBytes(wav.AsSpan(4, 4), wav.Length - 8);
        BitConverter.TryWriteBytes(wav.AsSpan(40, 4), wav.Length - 44);
        return wav;
    }

    private void Pause() => Interlocked.Exchange(ref retryAtTicks, (clock.GetUtcNow() + Cooldown).UtcTicks);
}
