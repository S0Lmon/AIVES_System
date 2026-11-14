using AIVES.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AIVES.BLL.Services.Speech;

public sealed record EdgeTtsVoice(string Id, string Name, AppLanguage Language, bool IsFemale);
public sealed record TtsVoiceOption(string Value, string Label);

public static class EdgeTtsVoiceCatalog
{
    public const string Prefix = "edge:";

    private static readonly EdgeTtsVoice[] Voices =
    [
        new("vi-VN-HoaiMyNeural", "HoaiMy", AppLanguage.Vi, true),
        new("vi-VN-NamMinhNeural", "NamMinh", AppLanguage.Vi, false),
        new("en-US-AriaNeural", "Aria", AppLanguage.En, true),
        new("en-US-GuyNeural", "Guy", AppLanguage.En, false)
    ];

    public static IReadOnlyList<EdgeTtsVoice> ForLanguage(AppLanguage language) =>
        Voices.Where(voice => voice.Language == language).ToArray();

    public static bool TryResolve(string? selection, out EdgeTtsVoice voice)
    {
        voice = Voices.FirstOrDefault(candidate =>
            string.Equals(selection, Prefix + candidate.Id, StringComparison.OrdinalIgnoreCase))!;
        return voice is not null;
    }

    public static string Selection(EdgeTtsVoice voice) => Prefix + voice.Id;
}

public sealed class EdgeTtsOptions
{
    public const string SectionName = "EdgeTts";

    public string PythonPath { get; set; } = "python";
    public int TimeoutSeconds { get; set; } = 30;
}

public interface IEdgeTtsProvider
{
    bool IsAvailable { get; }
    Task<byte[]> SynthesizeMp3Async(string text, string voiceId, double speed, bool useCache, CancellationToken cancellationToken);
}

public sealed class EdgeTtsProvider : IEdgeTtsProvider
{
    private const string SynthesisScript =
        "import asyncio, sys\n" +
        "import edge_tts\n" +
        "async def main():\n" +
        "    communicate = edge_tts.Communicate(sys.stdin.read(), voice=sys.argv[1], rate=sys.argv[2])\n" +
        "    async for chunk in communicate.stream():\n" +
        "        if chunk['type'] == 'audio':\n" +
        "            sys.stdout.buffer.write(chunk['data'])\n" +
        "    sys.stdout.buffer.flush()\n" +
        "asyncio.run(main())\n";

    private readonly EdgeTtsOptions settings;
    private readonly IMemoryCache cache;
    private readonly ILogger<EdgeTtsProvider> logger;
    private readonly Lazy<bool> availability;

    public bool IsAvailable => availability.Value;

    public EdgeTtsProvider(
        IOptions<EdgeTtsOptions> options,
        IMemoryCache cache,
        ILogger<EdgeTtsProvider> logger)
    {
        settings = options.Value;
        this.cache = cache;
        this.logger = logger;
        availability = new Lazy<bool>(CheckAvailability, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<byte[]> SynthesizeMp3Async(
        string text,
        string voiceId,
        double speed,
        bool useCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text to synthesize cannot be empty.", nameof(text));
        if (!IsKnownVoice(voiceId))
            throw new ArgumentException("The selected Edge TTS voice is not supported.", nameof(voiceId));
        if (!IsAvailable)
            throw new InvalidOperationException($"Edge TTS is not installed or unavailable. Install scripts/requirements-edge-tts.txt for Python '{settings.PythonPath}'.");

        speed = Math.Clamp(speed, 0.6, 1.4);
        var key = (nameof(EdgeTtsProvider), voiceId, Math.Round(speed, 2), text.Trim());
        if (useCache && cache.TryGetValue(key, out byte[]? cached) && cached is not null)
            return cached;

        var percent = Math.Round((speed - 1) * 100, MidpointRounding.AwayFromZero);
        var rate = percent >= 0
            ? $"+{percent.ToString("0", CultureInfo.InvariantCulture)}%"
            : $"{percent.ToString("0", CultureInfo.InvariantCulture)}%";
        var audio = await RunSynthesisAsync(text.Trim(), voiceId, rate, cancellationToken);
        if (useCache)
            cache.Set(key, audio, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
        return audio;
    }

    private bool CheckAvailability()
    {
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo() };
            process.StartInfo.ArgumentList.Add("-X");
            process.StartInfo.ArgumentList.Add("utf8");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("import edge_tts");
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            if (!process.Start())
                return false;
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogInformation("Edge TTS is unavailable because its Python runtime could not be started: {Message}", ex.Message);
            return false;
        }
    }

    private async Task<byte[]> RunSynthesisAsync(string text, string voiceId, string rate, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateStartInfo() };
        process.StartInfo.ArgumentList.Add("-X");
        process.StartInfo.ArgumentList.Add("utf8");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(SynthesisScript);
        process.StartInfo.ArgumentList.Add(voiceId);
        process.StartInfo.ArgumentList.Add(rate);

        if (!process.Start())
            throw new InvalidOperationException("Could not start the Edge TTS Python process.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var audio = new MemoryStream();
        try
        {
            var outputTask = process.StandardOutput.BaseStream.CopyToAsync(audio, linked.Token);
            var errorTask = process.StandardError.ReadToEndAsync(linked.Token);
            await process.StandardInput.WriteAsync(text.AsMemory(), linked.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(linked.Token);
            await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Edge TTS failed: {TrimError(error)}");
            if (audio.Length == 0)
                throw new InvalidOperationException("Edge TTS returned no audio.");
            return audio.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new TimeoutException($"Edge TTS did not finish within {Math.Clamp(settings.TimeoutSeconds, 5, 120)} seconds.");
        }
        finally
        {
            Kill(process);
        }
    }

    private ProcessStartInfo CreateStartInfo() => new()
    {
        FileName = settings.PythonPath,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
    };

    private static bool IsKnownVoice(string voiceId) =>
        EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.Vi).Any(voice => voice.Id == voiceId)
        || EdgeTtsVoiceCatalog.ForLanguage(AppLanguage.En).Any(voice => voice.Id == voiceId);

    private static string TrimError(string error)
    {
        var trimmed = error.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class ConfiguredTextToSpeech(
    SystemTextToSpeech systemSpeech,
    IEdgeTtsProvider edgeTts) : ITextToSpeech
{
    public bool IsAvailable(AppLanguage language) =>
        systemSpeech.IsAvailable(language) || edgeTts.IsAvailable;

    /// <summary>
    /// Whether this language can be read aloud. A saved voice that has since gone away — a
    /// System.Speech voice uninstalled from the server, or an Edge TTS voice whose Python runtime
    /// was removed — must not silence the examiner: synthesis falls back to another installed voice
    /// for the language, so availability follows the language rather than the saved name.
    /// </summary>
    public bool IsAvailable(AppLanguage language, string? preferredVoiceName)
    {
        if (string.IsNullOrWhiteSpace(preferredVoiceName))
            return IsAvailable(language);
        if (EdgeTtsVoiceCatalog.TryResolve(preferredVoiceName, out var edgeVoice))
            return edgeVoice.Language == language && (edgeTts.IsAvailable || systemSpeech.IsAvailable(language));
        return systemSpeech.IsAvailable(language);
    }

    public bool UsesOnlineService(AppLanguage language, string? preferredVoiceName) =>
        EdgeTtsVoiceCatalog.TryResolve(preferredVoiceName, out var selectedEdgeVoice)
            ? selectedEdgeVoice.Language == language && edgeTts.IsAvailable
            : string.IsNullOrWhiteSpace(preferredVoiceName)
                && !systemSpeech.HasFemaleVoice(language)
                && edgeTts.IsAvailable
                && EdgeTtsVoiceCatalog.ForLanguage(language).Any(voice => voice.IsFemale);

    public Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default) =>
        systemSpeech.SynthesizeWavAsync(text, language, speed, cancellationToken);

    public Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, string? preferredVoiceName, CancellationToken cancellationToken = default) =>
        systemSpeech.SynthesizeWavAsync(text, language, speed, preferredVoiceName, cancellationToken);

    public async Task<SpeechAudio> SynthesizeAsync(
        string text,
        AppLanguage language,
        double speed,
        string? preferredVoiceName = null,
        bool useCache = true,
        CancellationToken cancellationToken = default)
    {
        if (EdgeTtsVoiceCatalog.TryResolve(preferredVoiceName, out var selectedEdgeVoice))
        {
            if (selectedEdgeVoice.Language != language)
                throw new ArgumentException($"The selected Edge TTS voice does not support {language}.", nameof(preferredVoiceName));
            if (edgeTts.IsAvailable)
            {
                var mp3 = await edgeTts.SynthesizeMp3Async(text, selectedEdgeVoice.Id, speed, useCache, cancellationToken);
                return new SpeechAudio(mp3, "audio/mpeg");
            }
            // The Edge runtime went away after the voice was saved. Read the question with an
            // installed voice instead of leaving the examiner silent mid-viva.
            preferredVoiceName = null;
        }
        else if (!string.IsNullOrWhiteSpace(preferredVoiceName) && !systemSpeech.IsAvailable(language, preferredVoiceName))
        {
            // Same for a System.Speech voice that is no longer installed on this server.
            preferredVoiceName = null;
        }

        if (!string.IsNullOrWhiteSpace(preferredVoiceName))
        {
            var wav = await systemSpeech.SynthesizeWavAsync(text, language, speed, preferredVoiceName, useCache, cancellationToken);
            return new SpeechAudio(wav, "audio/wav");
        }

        if (systemSpeech.HasFemaleVoice(language))
            return await systemSpeech.SynthesizeAsync(text, language, speed, useCache: useCache, cancellationToken: cancellationToken);

        var defaultEdgeVoice = EdgeTtsVoiceCatalog.ForLanguage(language).FirstOrDefault(voice => voice.IsFemale);
        if (defaultEdgeVoice is not null && edgeTts.IsAvailable)
        {
            var mp3 = await edgeTts.SynthesizeMp3Async(text, defaultEdgeVoice.Id, speed, useCache, cancellationToken);
            return new SpeechAudio(mp3, "audio/mpeg");
        }

        if (systemSpeech.IsAvailable(language))
            return await systemSpeech.SynthesizeAsync(text, language, speed, useCache: useCache, cancellationToken: cancellationToken);

        throw new InvalidOperationException($"No text-to-speech voice is available for {language}.");
    }
}
