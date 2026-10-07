using System.Text;
using System.Text.RegularExpressions;
using AIVES.DTO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;

namespace AIVES.BLL.Services.Speech;

/// <summary>
/// Whisper.net (whisper.cpp) speech recognition. Models load on first use and stay in memory;
/// a semaphore bounds how many answers are transcribed at once.
/// </summary>
public sealed partial class WhisperSpeechToText : ISpeechToText, IDisposable
{
    // Segments Whisper is likely to have heard nothing in.
    private const float NoSpeechCutoff = 0.6f;

    private readonly SpeechOptions options;
    private readonly ILogger<WhisperSpeechToText> logger;
    private readonly string finalPath;
    private readonly string previewPath;
    private readonly Lazy<WhisperFactory> finalFactory;
    private readonly Lazy<WhisperFactory> previewFactory;
    private readonly SemaphoreSlim gate;

    public WhisperSpeechToText(IOptions<SpeechOptions> options, IHostEnvironment environment, ILogger<WhisperSpeechToText> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        var root = SpeechPaths.Root(this.options, environment);
        finalPath = Path.Combine(root, this.options.WhisperModel);
        previewPath = string.IsNullOrWhiteSpace(this.options.WhisperPreviewModel)
            ? finalPath
            : Path.Combine(root, this.options.WhisperPreviewModel);
        if (!File.Exists(previewPath))
            previewPath = finalPath;
        finalFactory = new Lazy<WhisperFactory>(() => Load(finalPath));
        previewFactory = previewPath == finalPath ? finalFactory : new Lazy<WhisperFactory>(() => Load(previewPath));
        gate = new SemaphoreSlim(Math.Max(1, this.options.MaxConcurrentTranscriptions));
    }

    public bool IsAvailable => File.Exists(finalPath);

    public async Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, AppLanguage language, string? prompt, bool preview,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("No Whisper model is installed.");
        if (!Audio.HasSpeech(samples.Span))
            return string.Empty;

        await gate.WaitAsync(cancellationToken);
        try
        {
            var factory = (preview ? previewFactory : finalFactory).Value;
            var builder = factory.CreateBuilder()
                .WithLanguage(language.ToCultureCode())
                .WithThreads(Math.Max(1, options.Threads))
                .WithNoContext();
            if (!string.IsNullOrWhiteSpace(prompt))
                builder = builder.WithPrompt(prompt);
            await using var processor = builder.Build();

            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples.ToArray(), cancellationToken))
            {
                if (segment.NoSpeechProbability >= NoSpeechCutoff || IsHallucination(segment.Text))
                    continue;
                text.Append(segment.Text);
            }
            return Clean(text.ToString());
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Collapses whitespace and drops the bracketed tags Whisper adds, like "[âm nhạc]".</summary>
    public static string Clean(string text) =>
        Whitespace().Replace(Tags().Replace(text, " "), " ").Trim();

    /// <summary>Lines Whisper is known to make up from noise in Vietnamese and English video subtitles.</summary>
    public static bool IsHallucination(string text) => Hallucinations().IsMatch(text) || BareUrl().IsMatch(text);

    private WhisperFactory Load(string path)
    {
        var factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = options.UseGpu });
        logger.LogInformation("Loaded Whisper model {Model} ({Runtime})", Path.GetFileName(path), WhisperFactory.GetRuntimeInfo());
        return factory;
    }

    public void Dispose()
    {
        if (finalFactory.IsValueCreated)
            finalFactory.Value.Dispose();
        if (previewFactory != finalFactory && previewFactory.IsValueCreated)
            previewFactory.Value.Dispose();
        gate.Dispose();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*(nhạc|music|applause|vỗ tay)[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex Tags();

    [GeneratedRegex(@"subscribe|đăng ký kênh|ghiền mì gõ|like và share|thanks for watching|cảm ơn các bạn đã (theo dõi|xem)|hẹn gặp lại các bạn trong", RegexOptions.IgnoreCase)]
    private static partial Regex Hallucinations();

    // A segment that is nothing but a web address ("www.thichews.ac.com") is noise read as speech.
    [GeneratedRegex(@"^\s*(https?://)?(www\.)?[\w-]+(\.[\w-]+)+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrl();
}

internal static class SpeechPaths
{
    public static string Root(SpeechOptions options, IHostEnvironment environment) =>
        Path.IsPathRooted(options.ModelsPath) ? options.ModelsPath : Path.Combine(environment.ContentRootPath, options.ModelsPath);
}
